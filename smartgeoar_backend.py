
"""SmartGeoAR PC: CLAHE, filtering, Optical Flow and Kalman."""

import argparse
import base64
import json
import os
import queue
import socket
import sys
import threading
import time

from collections import deque
from dataclasses import asdict
from itertools import permutations

import cv2
import mediapipe as mp
import numpy as np


# ==========================================
# PROJECT PATHS
# ==========================================

ROOT = os.path.dirname(os.path.abspath(__file__))

for folder in (
    "role1_hand_tracking",
    "role2_geometry_reasoning"
):
    path = os.path.join(ROOT, folder)

    if path not in sys.path:
        sys.path.insert(0, path)


from role1_week2_stream import (
    CAMERA_INDEX,
    get_gesture,
    get_state,
    hand_states,
    make_hand_data
)

from phone_hand_tracking import PhoneHandTracking
from reasoning_engine import GeometryReasoningEngine
from schemas import CVDataInput, VertexInput
from shape_drop_engine import ShapeDropEngine


# ==========================================
# PERFORMANCE SETTINGS
# ==========================================

CAPTURE_WIDTH = 640
CAPTURE_HEIGHT = 480
CAPTURE_FPS = 30

VIDEO_FPS = 30
TRACK_FPS = 30

TRACK_WIDTH = 384

# MediaPipe periodically refreshes landmarks.
# Optical Flow fills the frames between detections.
DETECT_INTERVAL = 0.075

# Never rely on Optical Flow indefinitely.
MAX_FLOW_AGE = 0.25

JPEG_QUALITY = 60

# Wrist, thumb tip, index tip, middle finger base.
TRACK_JOINTS = (0, 4, 8, 9)


# ==========================================
# NETWORK CLIENT
# ==========================================

class Client:

    def __init__(self, sock, camera_source):
        self.sock = sock
        self.camera_source = camera_source
        self.phone_frames = queue.Queue(maxsize=1)

        self.alive = True
        self.cv = threading.Condition()

        self.controls = deque()
        self.transitions = deque()

        self.latest_hand = None
        self.latest_video = None

        self.last_sent_hand_frame = -1

        self.sender = threading.Thread(
            target=self._send_loop,
            daemon=True
        )

        self.sender.start()

    def send(self, packet):
        if not self.alive:
            return False

        source = packet.get("source", "")

        with self.cv:

            if source == "role1_camera":
                # Drop old video frames.
                self.latest_video = packet

            elif source == "role1_hand_tracking":

                is_transition = any(
                    hand.get("gesture") in (
                        "PINCH_START",
                        "PINCH_END"
                    )
                    for hand in packet.get("hands", [])
                )

                if is_transition:
                    if len(self.transitions) >= 16:
                        self.transitions.popleft()

                    self.transitions.append(packet)

                else:
                    self.latest_hand = packet

            else:
                # Geometry results are preserved.
                self.controls.append(packet)

            self.cv.notify()

        return True

    def _send_loop(self):

        try:
            while self.alive:

                with self.cv:
                    self.cv.wait_for(
                        lambda:
                            not self.alive
                            or self.controls
                            or self.transitions
                            or self.latest_hand is not None
                            or self.latest_video is not None,
                        timeout=0.25
                    )

                    if not self.alive:
                        break

                    if self.controls:
                        packet = self.controls.popleft()

                    elif self.transitions:
                        packet = self.transitions.popleft()

                    elif self.latest_hand is not None:
                        packet = self.latest_hand
                        self.latest_hand = None

                    elif self.latest_video is not None:
                        packet = self.latest_video
                        self.latest_video = None

                    else:
                        continue

                if packet.get("source") == "role1_hand_tracking":

                    frame_id = int(
                        packet.get("frame_id", -1)
                    )

                    # Do not deliver older HOLD packets
                    # after newer release events.
                    if frame_id <= self.last_sent_hand_frame:
                        continue

                    self.last_sent_hand_frame = frame_id

                payload = (
                    json.dumps(
                        packet,
                        ensure_ascii=False,
                        separators=(",", ":")
                    ) + "\n"
                ).encode("utf-8")

                self.sock.sendall(payload)

        except OSError:
            pass

        finally:
            self.close()

    def close(self):

        with self.cv:
            self.alive = False
            self.cv.notify_all()

        try:
            self.sock.shutdown(socket.SHUT_RDWR)
        except OSError:
            pass

        try:
            self.sock.close()
        except OSError:
            pass


# ==========================================
# GEOMETRY REASONING
# ==========================================

def evaluate_graph(packet):

    shape = packet.get("target_shape")

    if shape not in (
        "cube",
        "rectangular_prism",
        "square_pyramid"
    ):
        raise ValueError("Unknown shape")

    raw_vertices = packet.get("vertices")
    raw_edges = packet.get("detected_edges")

    if (
        not isinstance(raw_vertices, list)
        or not isinstance(raw_edges, list)
    ):
        raise ValueError("Invalid geometry data")

    if (
        len(raw_vertices) > 100
        or len(raw_edges) > 500
    ):
        raise ValueError("Too many geometry elements")

    vertices = [
        VertexInput(
            id=str(v["id"]),
            x=float(v.get("x", 0)),
            y=float(v.get("y", 0)),
            z=float(v.get("z", 0)),
            confidence=float(
                v.get("confidence", 1)
            )
        )
        for v in raw_vertices
    ]

    edges = []

    for edge in raw_edges:

        if (
            not isinstance(edge, (list, tuple))
            or len(edge) != 2
        ):
            raise ValueError("Invalid edge")

        edges.append((
            str(edge[0]),
            str(edge[1])
        ))

    frame_id = int(packet.get("frame_id", 0))

    data = CVDataInput(
        timestamp=float(
            packet.get("timestamp", time.time())
        ),
        frame_id=frame_id,
        target_shape=shape,
        vertices=vertices,
        detected_edges=edges
    )

    engine = GeometryReasoningEngine(
        target_shape=shape,
        auto_detect_shape=False,
        enable_3d_validation=True,
        enable_smoothing=False
    )

    result = asdict(engine.process(data))

    result.update({
        "source": "role2_geometry_reasoning",
        "session_id": str(
            packet.get("session_id", "")
        ),
        "frame_id": frame_id,
        "vertex_count": len(vertices),
        "edge_count": len(edges)
    })

    return result


# ==========================================
# UNITY REQUEST RECEIVER
# ==========================================

def receive_requests(client):

    shape_engine = ShapeDropEngine()

    try:
        with client.sock.makefile(
            "r",
            encoding="utf-8"
        ) as reader:

            while client.alive:

                line = reader.readline(1000001)

                if not line or len(line) > 1000000:
                    break

                try:
                    packet = json.loads(line)

                    if (
                        not isinstance(packet, dict)
                        or packet.get("source") != "role3_unity"
                    ):
                        continue

                    kind = packet.get("type", "")

                    if kind == "phone_camera_frame":

                        if client.camera_source == "phone":

                            try:
                                if client.phone_frames.full():
                                    client.phone_frames.get_nowait()

                                client.phone_frames.put_nowait(
                                    packet
                                )
                            except queue.Full:
                                pass

                        continue

                    if len(line) > 100000:
                        break

                    if kind == "ping":
                        client.send({
                            "source": "smartgeoar_backend",
                            "type": "pong"
                        })

                    elif kind == "shape_drop_request":
                        client.send(
                            shape_engine.evaluate(packet)
                        )

                    else:
                        client.send(
                            evaluate_graph(packet)
                        )

                except (
                    ValueError,
                    TypeError,
                    KeyError,
                    IndexError
                ) as error:

                    print(
                        "[BACKEND]",
                        ascii(error),
                        flush=True
                    )

                    client.send({
                        "source": "role2_geometry_reasoning",
                        "type": "error",
                        "message": str(error)
                    })

    except (OSError, UnicodeError):
        pass

    finally:
        client.close()


# ==========================================
# CONTINUOUS CAMERA CAPTURE
# ==========================================

class LatestCapture:

    def __init__(self, camera_index):

        api = (
            cv2.CAP_DSHOW
            if sys.platform == "win32"
            else cv2.CAP_ANY
        )

        self.cap = cv2.VideoCapture(
            camera_index,
            api
        )

        if not self.cap.isOpened():
            raise RuntimeError("Cannot open webcam")

        if sys.platform == "win32":
            self.cap.set(
                cv2.CAP_PROP_FOURCC,
                cv2.VideoWriter_fourcc(*"MJPG")
            )

        self.cap.set(
            cv2.CAP_PROP_FRAME_WIDTH,
            CAPTURE_WIDTH
        )

        self.cap.set(
            cv2.CAP_PROP_FRAME_HEIGHT,
            CAPTURE_HEIGHT
        )

        self.cap.set(
            cv2.CAP_PROP_FPS,
            CAPTURE_FPS
        )

        self.cap.set(
            cv2.CAP_PROP_BUFFERSIZE,
            1
        )

        self.lock = threading.Lock()

        self.frame = None
        self.seq = -1
        self.running = True

        self.thread = threading.Thread(
            target=self._read,
            daemon=True
        )

        self.thread.start()

    def _read(self):

        while self.running:

            ok, frame = self.cap.read()

            if not ok:
                time.sleep(0.02)
                continue

            frame = cv2.flip(frame, 1)

            with self.lock:
                self.frame = frame
                self.seq += 1

    def newest(self, seq):

        with self.lock:

            if (
                self.frame is None
                or self.seq == seq
            ):
                return None, seq

            return self.frame.copy(), self.seq

    def close(self):

        self.running = False

        self.thread.join(timeout=0.8)
        self.cap.release()


# ==========================================
# 1. CLAHE + NOISE FILTERING
# ==========================================

class ImagePreprocessor:

    def __init__(
        self,
        use_clahe=True,
        denoise="gaussian"
    ):

        self.clahe_enabled = use_clahe
        self.denoise = denoise

        self.clahe = cv2.createCLAHE(
            clipLimit=2.0,
            tileGridSize=(8, 8)
        )

    def run(self, bgr):

        # CLAHE works on the luminance channel.
        # Avoid enhancing a normally lit image
        # unnecessarily.

        if self.clahe_enabled:

            ycrcb = cv2.cvtColor(
                bgr,
                cv2.COLOR_BGR2YCrCb
            )

            y = ycrcb[:, :, 0]

            if (
                y.mean() < 90.0
                or y.std() < 38.0
            ):
                ycrcb[:, :, 0] = self.clahe.apply(y)

                bgr = cv2.cvtColor(
                    ycrcb,
                    cv2.COLOR_YCrCb2BGR
                )

        # Gaussian is the default lightweight filter.
        if self.denoise == "gaussian":

            bgr = cv2.GaussianBlur(
                bgr,
                (3, 3),
                0.7
            )

        # Bilateral preserves edges but costs more CPU.
        elif self.denoise == "bilateral":

            bgr = cv2.bilateralFilter(
                bgr,
                5,
                30,
                30
            )

        return bgr


# ==========================================
# 2. KALMAN FILTER
# ==========================================

class HandKalman:

    def __init__(self, xy, now):

        # State = [x, y, velocity_x, velocity_y]
        # Measurement = [x, y]

        self.filter = cv2.KalmanFilter(4, 2)

        self.filter.measurementMatrix = np.array(
            [
                [1, 0, 0, 0],
                [0, 1, 0, 0]
            ],
            dtype=np.float32
        )

        self.filter.processNoiseCov = np.diag(
            [
                1.0e-4,
                1.0e-4,
                2.0e-3,
                2.0e-3
            ]
        ).astype(np.float32)

        self.filter.measurementNoiseCov = (
            np.eye(2, dtype=np.float32)
            * 7e-5
        )

        self.filter.errorCovPost = (
            np.eye(4, dtype=np.float32)
            * 0.02
        )

        self.filter.statePost = np.array(
            [
                [xy[0]],
                [xy[1]],
                [0],
                [0]
            ],
            dtype=np.float32
        )

        self.last_time = now

    def update(self, xy, now):

        dt = float(np.clip(
            now - self.last_time,
            0.008,
            0.12
        ))

        self.last_time = now

        self.filter.transitionMatrix = np.array(
            [
                [1, 0, dt, 0],
                [0, 1, 0, dt],
                [0, 0, 1, 0],
                [0, 0, 0, 1]
            ],
            dtype=np.float32
        )

        predicted = self.filter.predict()

        # Reset if tracking jumps to a different
        # point rather than producing a realistic
        # continuous movement.
        if (
            np.linalg.norm(
                predicted[:2, 0] - xy
            ) > 0.22
        ):
            self.__init__(xy, now)
            return xy

        measured = np.array(
            [
                [xy[0]],
                [xy[1]]
            ],
            dtype=np.float32
        )

        corrected = self.filter.correct(
            measured
        )[:2, 0]

        # Adaptive response: when the hand moves
        # quickly, trust the current measurement
        # a little more to reduce lag.
        speed = np.linalg.norm(
            xy - corrected
        )

        weight = (
            0.35
            if speed > 0.018
            else 0.10
        )

        return np.clip(
            (1 - weight) * corrected
            + weight * xy,
            0,
            1
        )


# ==========================================
# 3. STABLE TWO-HAND IDENTIFICATION
# ==========================================

class TwoHandIdentity:

    def __init__(self):
        self.last = {}

    def assign(self, candidates, now):

        self.last = {
            name: item
            for name, item in self.last.items()
            if now - item[1] < 0.7
        }

        if not candidates:
            return []

        names = ("Left", "Right")

        def cost(candidate, name):

            penalty = (
                0.15
                if candidate["label"] != name
                else 0.0
            )

            if name in self.last:

                penalty += float(
                    np.linalg.norm(
                        candidate["wrist"]
                        - self.last[name][0]
                    )
                )

            else:
                penalty += 0.12

            return penalty

        selected = min(
            permutations(
                names,
                len(candidates)
            ),
            key=lambda assigned: sum(
                cost(candidate, name)
                for candidate, name
                in zip(candidates, assigned)
            )
        )

        for candidate, name in zip(
            candidates,
            selected
        ):
            candidate["hand"] = name

            self.last[name] = (
                candidate["wrist"],
                now
            )

        return candidates


# ==========================================
# 4. MEDIAPIPE + LUCAS-KANADE OPTICAL FLOW
# ==========================================

class Tracking:

    def __init__(
        self,
        use_flow=True,
        use_kalman=True
    ):

        self.use_flow = use_flow
        self.use_kalman = use_kalman

        self.identity = TwoHandIdentity()

        self.tracker = mp.solutions.hands.Hands(
            static_image_mode=False,
            max_num_hands=2,
            min_detection_confidence=0.6,
            min_tracking_confidence=0.6,
            model_complexity=0
        )

        self.previous_gray = None
        self.last_detect = -100.0

        self.tracks = {}
        self.filters = {}
        self.last_seen = {}

    # ======================================
    # COMPUTE PINCH RATIO
    # ======================================

    @staticmethod
    def ratio_and_mid(
        points,
        width,
        height
    ):

        norm = points / np.array(
            [width, height],
            dtype=np.float32
        )

        wrist, thumb, index, middle = norm

        palm = float(
            np.linalg.norm(
                wrist - middle
            )
        )

        if palm < 1e-5:
            return None

        ratio = float(
            np.linalg.norm(
                thumb - index
            ) / palm
        )

        middle_point = np.clip(
            (thumb + index) * 0.5,
            0,
            1
        )

        return ratio, middle_point

    # ======================================
    # PYRAMIDAL LUCAS-KANADE
    # ======================================

    def _flow(
        self,
        gray,
        now,
        exclude
    ):

        if self.previous_gray is None:
            return {}

        result = {}

        for name, track in list(
            self.tracks.items()
        ):

            if (
                name in exclude
                or now - track["detected_at"]
                    > MAX_FLOW_AGE
            ):
                continue

            original = (
                track["points"]
                .reshape(-1, 1, 2)
                .astype(np.float32)
            )

            forward, valid, _ = (
                cv2.calcOpticalFlowPyrLK(
                    self.previous_gray,
                    gray,
                    original,
                    None,
                    winSize=(17, 17),
                    maxLevel=2,
                    criteria=(
                        cv2.TERM_CRITERIA_EPS
                        | cv2.TERM_CRITERIA_COUNT,
                        15,
                        0.03
                    )
                )
            )

            if (
                forward is None
                or valid is None
                or not bool(np.all(valid))
            ):
                continue

            # Backward verification helps reject
            # an optical-flow point that jumped
            # onto the background.
            back, back_valid, _ = (
                cv2.calcOpticalFlowPyrLK(
                    gray,
                    self.previous_gray,
                    forward,
                    None,
                    winSize=(17, 17),
                    maxLevel=2
                )
            )

            if (
                back is None
                or back_valid is None
                or not bool(np.all(back_valid))
            ):
                continue

            backward_error = np.max(
                np.linalg.norm(
                    back.reshape(-1, 2)
                    - original.reshape(-1, 2),
                    axis=1
                )
            )

            if backward_error > 3.0:
                continue

            p = forward.reshape(-1, 2)

            height, width = gray.shape

            if (
                not np.all(np.isfinite(p))
                or np.any(p < 0)
                or np.any(p[:, 0] >= width)
                or np.any(p[:, 1] >= height)
            ):
                continue

            movement = np.max(
                np.linalg.norm(
                    p - original.reshape(-1, 2),
                    axis=1
                )
            )

            if movement > 55:
                continue

            self.tracks[name]["points"] = p

            result[name] = p

        return result

    # ======================================
    # PROCESS ONE HAND
    # ======================================

    def _send_hand(
        self,
        name,
        points,
        width,
        height,
        now
    ):

        info = self.ratio_and_mid(
            points,
            width,
            height
        )

        if info is None:
            return None

        ratio, raw = info

        if (
            not np.isfinite(ratio)
            or ratio > 6.0
        ):
            return None

        state = get_state(name)

        state["lost"] = 0

        # Preserve the original project's
        # independent pinch hysteresis.
        gesture = get_gesture(
            state,
            ratio
        )

        if self.use_kalman:

            if name not in self.filters:
                self.filters[name] = HandKalman(
                    raw,
                    now
                )

            x, y = self.filters[name].update(
                raw,
                now
            )

        else:
            x, y = raw

        state["last_pos"] = (
            float(x),
            float(y)
        )

        state["last_ratio"] = ratio
        self.last_seen[name] = now

        return make_hand_data(
            name,
            gesture,
            x,
            y,
            ratio
        )

    # ======================================
    # PROCESS CURRENT CAMERA FRAME
    # ======================================

    def process(self, bgr, now):

        gray = cv2.cvtColor(
            bgr,
            cv2.COLOR_BGR2GRAY
        )

        height, width = gray.shape

        observed = {}
        detector_ran = False

        # MediaPipe: periodically correct tracks.
        # Optical Flow: intermediate updates.
        due = (
            not self.use_flow
            or now - self.last_detect
                >= DETECT_INTERVAL
        )

        if due:

            detector_ran = True

            rgb = cv2.cvtColor(
                bgr,
                cv2.COLOR_BGR2RGB
            )

            rgb.flags.writeable = False

            detection = self.tracker.process(
                rgb
            )

            self.last_detect = now

            candidates = []

            for i, landmarks in enumerate(
                detection.multi_hand_landmarks or []
            ):

                label = "Left"

                if (
                    detection.multi_handedness
                    and i <
                        len(detection.multi_handedness)
                ):
                    label = (
                        detection.multi_handedness[i]
                        .classification[0]
                        .label
                    )

                points = landmarks.landmark

                keypoints = np.array(
                    [
                        [
                            points[j].x * width,
                            points[j].y * height
                        ]
                        for j in TRACK_JOINTS
                    ],
                    dtype=np.float32
                )

                candidates.append({
                    "label": label,
                    "wrist": keypoints[0]
                        / [width, height],
                    "keypoints": keypoints
                })

            # Both hands have independent IDs.
            for candidate in self.identity.assign(
                candidates,
                now
            ):

                name = candidate["hand"]
                points = candidate["keypoints"]

                observed[name] = points

                self.tracks[name] = {
                    "points": points,
                    "detected_at": now
                }

        # Follow fingers between MediaPipe updates.
        if (
            self.use_flow
            and self.previous_gray is not None
        ):
            observed.update(
                self._flow(
                    gray,
                    now,
                    exclude=set(observed)
                )
            )

        self.previous_gray = gray

        # Do not keep stale optical-flow tracks.
        for name in list(self.tracks):

            if (
                now -
                self.tracks[name]["detected_at"]
                > MAX_FLOW_AGE
            ):
                del self.tracks[name]

        # Reset only the hand that disappeared.
        # Never create a fake PINCH_END.
        for name, seen_at in list(
            self.last_seen.items()
        ):

            if now - seen_at > 0.65:

                self.last_seen.pop(
                    name,
                    None
                )

                self.filters.pop(
                    name,
                    None
                )

                hand_states.pop(
                    name,
                    None
                )

        hands = []

        for name, points in observed.items():

            hand = self._send_hand(
                name,
                points,
                width,
                height,
                now
            )

            if hand is not None:
                hands.append(hand)

        return hands, detector_ran

    def close(self):
        self.tracker.close()


# ==========================================
# WEBCAM STREAM
# ==========================================

def stream_webcam(client, args):

    hand_states.clear()

    try:
        capture = LatestCapture(CAMERA_INDEX)

    except Exception as error:

        client.send({
            "source": "smartgeoar_backend",
            "type": "camera_error",
            "message": str(error)
        })

        client.close()
        return

    preprocessor = ImagePreprocessor(
        use_clahe=not args.no_clahe,
        denoise=args.denoise
    )

    tracker = Tracking(
        use_flow=not args.no_optical_flow,
        use_kalman=not args.no_kalman
    )

    stats = {
        "video": 0,
        "hands": 0,
        "detector": 0,
        "flow": 0,
        "start": time.monotonic()
    }

    done = threading.Event()

    # ======================================
    # VIDEO THREAD
    # ======================================

    def video_loop():

        last_seq = -1
        next_time = 0.0

        while (
            client.alive
            and not done.is_set()
        ):

            now = time.monotonic()

            if now < next_time:
                time.sleep(
                    min(
                        0.005,
                        next_time - now
                    )
                )
                continue

            frame, seq = capture.newest(
                last_seq
            )

            if frame is None:
                time.sleep(0.002)
                continue

            last_seq = seq

            next_time = (
                now + 1.0 / VIDEO_FPS
            )

            ok, encoded = cv2.imencode(
                ".jpg",
                frame,
                [
                    cv2.IMWRITE_JPEG_QUALITY,
                    JPEG_QUALITY
                ]
            )

            if ok:

                packet = {
                    "source": "role1_camera",
                    "jpeg": base64.b64encode(
                        encoded
                    ).decode("ascii")
                }

                if not client.send(packet):
                    break

                stats["video"] += 1

    video_thread = None

    if not args.no_camera_stream:

        video_thread = threading.Thread(
            target=video_loop,
            daemon=True
        )

        video_thread.start()

    # ======================================
    # TRACKING THREAD
    # ======================================

    last_seq = -1
    next_track = 0.0

    try:
        while client.alive:

            now = time.monotonic()

            if now < next_track:
                time.sleep(
                    min(
                        0.003,
                        next_track - now
                    )
                )
                continue

            frame, seq = capture.newest(
                last_seq
            )

            if frame is None:
                time.sleep(0.002)
                continue

            last_seq = seq

            next_track = (
                now + 1.0 / TRACK_FPS
            )

            height, width = frame.shape[:2]

            # Reduce processing cost for MediaPipe
            # and Optical Flow only.
            # The displayed webcam is not resized.
            if width > TRACK_WIDTH:

                small_height = max(
                    1,
                    int(
                        height
                        * TRACK_WIDTH / width
                    )
                )

                frame = cv2.resize(
                    frame,
                    (
                        TRACK_WIDTH,
                        small_height
                    ),
                    interpolation=cv2.INTER_AREA
                )

            processed = preprocessor.run(
                frame
            )

            now = time.monotonic()

            hands, detected = tracker.process(
                processed,
                now
            )

            # One packet may contain BOTH hands.
            # Unity's existing JSON format is kept.
            if not client.send({
                "version": "1.0",
                "source": "role1_hand_tracking",
                "frame_id": seq,
                "coordinate_system": "normalized_image",
                "hands": hands
            }):
                break

            stats["hands"] += 1

            if detected:
                stats["detector"] += 1
            else:
                stats["flow"] += 1

            # Real throughput statistics.
            if now - stats["start"] >= 5:

                seconds = (
                    now - stats["start"]
                )

                print(
                    "[FPS] hand=%.1f "
                    "video=%.1f "
                    "MP=%.1f "
                    "LK=%.1f | "
                    "CLAHE=%s "
                    "denoise=%s "
                    "flow=%s "
                    "kalman=%s"
                    % (
                        stats["hands"] / seconds,
                        stats["video"] / seconds,
                        stats["detector"] / seconds,
                        stats["flow"] / seconds,
                        not args.no_clahe,
                        args.denoise,
                        not args.no_optical_flow,
                        not args.no_kalman
                    ),
                    flush=True
                )

                stats = {
                    "video": 0,
                    "hands": 0,
                    "detector": 0,
                    "flow": 0,
                    "start": now
                }

    finally:

        done.set()

        tracker.close()
        capture.close()
        client.close()

        if video_thread is not None:
            video_thread.join(timeout=0.5)


# ==========================================
# PHONE COMPATIBILITY
# ==========================================

def stream_phone(client):

    tracker = PhoneHandTracking()

    last_id = -1

    try:
        while client.alive:

            try:
                packet = client.phone_frames.get(
                    timeout=0.25
                )

            except queue.Empty:

                client.send({
                    "source": "smartgeoar_backend",
                    "type": "pong"
                })

                continue

            frame_id = packet.get(
                "frame_id",
                -1
            )

            if (
                not isinstance(frame_id, int)
                or frame_id <= last_id
            ):
                continue

            last_id = frame_id
            hands = []

            try:
                encoded = packet.get(
                    "jpeg",
                    ""
                )

                if (
                    not isinstance(encoded, str)
                    or len(encoded) > 700000
                ):
                    raise ValueError(
                        "Invalid phone JPEG"
                    )

                frame = cv2.imdecode(
                    np.frombuffer(
                        base64.b64decode(encoded),
                        dtype=np.uint8
                    ),
                    cv2.IMREAD_COLOR
                )

                intrinsics = packet.get(
                    "intrinsics"
                )

                if (
                    frame is None
                    or not isinstance(
                        intrinsics,
                        dict
                    )
                ):
                    raise ValueError(
                        "Invalid phone camera parameters"
                    )

                hands = tracker.process(
                    frame,
                    frame_id,
                    intrinsics
                )

            except (
                ValueError,
                TypeError,
                cv2.error
            ) as error:

                print(
                    "[PHONE]",
                    ascii(error),
                    flush=True
                )

            if not client.send({
                "version": "1.0",
                "source": "role1_hand_tracking",
                "camera_source": "phone_rear",
                "frame_id": frame_id,
                "coordinate_system": "normalized_image",
                "hands": hands
            }):
                break

    finally:

        tracker.close()
        client.close()


# ==========================================
# SERVER
# ==========================================

def main():

    parser = argparse.ArgumentParser()

    parser.add_argument(
        "--host",
        default="127.0.0.1"
    )

    parser.add_argument(
        "--port",
        type=int,
        default=5053
    )

    parser.add_argument(
        "--camera-source",
        choices=("phone", "webcam"),
        default="webcam"
    )

    parser.add_argument(
        "--no-camera-stream",
        action="store_true"
    )

    # Algorithm switches for academic comparison.
    parser.add_argument(
        "--no-clahe",
        action="store_true"
    )

    parser.add_argument(
        "--denoise",
        choices=(
            "none",
            "gaussian",
            "bilateral"
        ),
        default="gaussian"
    )

    parser.add_argument(
        "--no-optical-flow",
        action="store_true"
    )

    parser.add_argument(
        "--no-kalman",
        action="store_true"
    )

    args = parser.parse_args()

    with socket.socket(
        socket.AF_INET,
        socket.SOCK_STREAM
    ) as server:

        server.setsockopt(
            socket.SOL_SOCKET,
            socket.SO_REUSEADDR,
            1
        )

        server.bind((
            args.host,
            args.port
        ))

        server.listen(1)

        print(
            "SmartGeoAR: %s:%s "
            "camera=%s "
            "CLAHE=%s "
            "denoise=%s "
            "flow=%s "
            "kalman=%s"
            % (
                args.host,
                args.port,
                args.camera_source,
                not args.no_clahe,
                args.denoise,
                not args.no_optical_flow,
                not args.no_kalman
            ),
            flush=True
        )

        while True:

            sock, _ = server.accept()

            sock.setsockopt(
                socket.IPPROTO_TCP,
                socket.TCP_NODELAY,
                1
            )

            client = Client(
                sock,
                args.camera_source
            )

            client.send({
                "source": "smartgeoar_backend",
                "type": "hello",
                "camera_source":
                    args.camera_source
            })

            threading.Thread(
                target=receive_requests,
                args=(client,),
                daemon=True
            ).start()

            if args.camera_source == "phone":
                stream_phone(client)
            else:
                stream_webcam(
                    client,
                    args
                )


if __name__ == "__main__":
    main()
