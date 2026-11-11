
import argparse
import base64
import json
import os
import queue
import socket
import sys
import threading
import time

from dataclasses import asdict

import cv2
import mediapipe as mp
import numpy as np


# =========================================================
# PATH
# =========================================================

ROOT = os.path.dirname(os.path.abspath(__file__))

for folder in (
    "role1_hand_tracking",
    "role2_geometry_reasoning"
):
    path = os.path.join(ROOT, folder)

    if path not in sys.path:
        sys.path.insert(0, path)


# =========================================================
# ROLE 1
# =========================================================

from role1_week2_stream import (
    CAMERA_INDEX,
    MAX_HANDS,
    distance_2d,
    get_gesture,
    get_state,
    handle_missing_hands,
    make_hand_data,
    smooth_point,
)
from hand_pose import HandPoseEstimator
from phone_hand_tracking import PhoneHandTracking


# =========================================================
# ROLE 2
# =========================================================

from reasoning_engine import GeometryReasoningEngine
from schemas import CVDataInput, VertexInput
from shape_drop_engine import ShapeDropEngine


# =========================================================
# CONFIG
# =========================================================

HOST = "0.0.0.0"
PORT = 5053

VIDEO_FPS = 12
MAX_VIDEO_WIDTH = 720

mp_hands = mp.solutions.hands


# =========================================================
# NEW: TWO HAND GRIP OWNER
#
# Một tay đã nắm cạnh thì tay đó giữ quyền điều khiển.
#
# Tay còn lại OPEN không được nhả cạnh.
# Tay còn lại PINCH không được cướp cạnh.
#
# Chỉ tay đang nắm mới được gửi PINCH_END.
# =========================================================

class PinchOwnerGuard:

    def __init__(self):
        self.owner = None
        self.last_hand = None
        self.missing_frames = 0
        self.max_missing_frames = 10

    @staticmethod
    def hand_id(hand):
        return str(hand.get("hand", ""))

    @staticmethod
    def gesture(hand):
        return str(
            hand.get("gesture", "")
        ).upper()

    def reset(self):
        self.owner = None
        self.last_hand = None
        self.missing_frames = 0

    def filter(self, detected_hands):

        if not detected_hands:
            detected_hands = []

        detected_hands = [
            hand
            for hand in detected_hands
            if isinstance(hand, dict)
        ]

        # =============================================
        # CASE 1:
        # ĐÃ CÓ TAY ĐANG GIỮ CẠNH
        # =============================================

        if self.owner is not None:

            owner_hand = None

            for hand in detected_hands:

                if self.hand_id(hand) == self.owner:
                    owner_hand = hand
                    break

            # -----------------------------------------
            # TAY ĐANG GIỮ VẪN ĐƯỢC NHẬN DIỆN
            # -----------------------------------------

            if owner_hand is not None:

                self.missing_frames = 0

                gesture = self.gesture(owner_hand)

                self.last_hand = dict(owner_hand)

                # Chỉ tay đang giữ mới được nhả.
                if gesture in (
                    "PINCH_END",
                    "OPEN"
                ):

                    print(
                        "[GRIP] Release by owner:",
                        self.owner,
                        flush=True
                    )

                    self.reset()

                    # Chỉ gửi trạng thái của tay
                    # vừa nhả, không gửi tay thứ hai.
                    return [owner_hand]

                # Tay đang giữ tiếp tục điều khiển.
                # Bỏ qua hoàn toàn tay còn lại.
                return [owner_hand]

            # -----------------------------------------
            # TAY ĐANG GIỮ TẠM THỜI MẤT TRACKING
            # -----------------------------------------

            self.missing_frames += 1

            if (
                self.last_hand is not None
                and self.missing_frames <=
                    self.max_missing_frames
            ):

                ghost = dict(self.last_hand)

                # Không cho tay thứ hai OPEN
                # khiến Unity nhả cạnh.
                ghost["gesture"] = "PINCH_HOLD"

                if "spatial" in ghost:
                    ghost["spatial"] = {"valid": False}

                return [ghost]

            # -----------------------------------------
            # MẤT TAY GIỮ QUÁ LÂU
            # -----------------------------------------

            if self.last_hand is not None:

                lost_hand = dict(self.last_hand)

                lost_hand["gesture"] = "PINCH_END"

                if "spatial" in lost_hand:
                    lost_hand["spatial"] = {"valid": False}

                print(
                    "[GRIP] Owner tracking lost:",
                    self.owner,
                    flush=True
                )

                self.reset()

                return [lost_hand]

            self.reset()

        # =============================================
        # CASE 2:
        # CHƯA CÓ TAY NÀO GIỮ CẠNH
        # =============================================

        # Ưu tiên PINCH_START.
        # Không phụ thuộc tay nằm ở vị trí
        # đầu tiên hay thứ hai trong JSON.

        active_hand = None

        for hand in detected_hands:

            if self.gesture(hand) == "PINCH_START":
                active_hand = hand
                break

        # Nếu thiếu PINCH_START nhưng đã
        # thấy PINCH_HOLD thì nhận tay đó.

        if active_hand is None:

            for hand in detected_hands:

                if self.gesture(hand) == "PINCH_HOLD":
                    active_hand = hand
                    break

        # -----------------------------------------
        # CÓ TAY BẮT ĐẦU PINCH
        # -----------------------------------------

        if active_hand is not None:

            hand_name = self.hand_id(active_hand)

            if hand_name:

                self.owner = hand_name

                self.last_hand = dict(active_hand)

                self.missing_frames = 0

                print(
                    "[GRIP] Owner acquired:",
                    self.owner,
                    flush=True
                )

                # Chỉ gửi tay đang nắm.
                return [active_hand]

        # -----------------------------------------
        # KHÔNG CÓ TAY NẮM
        # -----------------------------------------

        # Vẫn giữ khả năng nhận diện cả hai tay.
        # Khi một tay bắt đầu pinch ở frame sau,
        # tay đó sẽ được khóa quyền điều khiển.

        return detected_hands


# =========================================================
# TCP CONNECTION
# =========================================================

class AppConnection:

    def __init__(self, sock, camera_source="phone"):

        self.sock = sock

        self.lock = threading.Lock()

        self.alive = True
        self.camera_source = camera_source
        self.phone_frames = queue.Queue(maxsize=1)

    def send(self, packet):

        if not self.alive:
            return False

        payload = (
            json.dumps(
                packet,
                ensure_ascii=False,
                separators=(",", ":")
            ) + "\n"
        ).encode("utf-8")

        try:

            with self.lock:
                self.sock.sendall(payload)

            return True

        except OSError:

            self.alive = False

            return False

    def close(self):

        self.alive = False

        try:
            self.sock.shutdown(socket.SHUT_RDWR)
        except OSError:
            pass

        try:
            self.sock.close()
        except OSError:
            pass


# =========================================================
# ROLE 2 - GRAPH REASONING
# GIỮ LUỒNG KIỂM TRA CẠNH CŨ
# =========================================================

def evaluate_graph(packet):

    shape = packet.get("target_shape")

    if shape not in (
        "cube",
        "rectangular_prism",
        "square_pyramid"
    ):
        raise ValueError("Loại hình không hợp lệ")

    raw_vertices = packet.get("vertices")
    raw_edges = packet.get("detected_edges")

    if not isinstance(raw_vertices, list):
        raise ValueError(
            "vertices phải là danh sách"
        )

    if not isinstance(raw_edges, list):
        raise ValueError(
            "detected_edges phải là danh sách"
        )

    if (
        len(raw_vertices) > 100
        or len(raw_edges) > 500
    ):
        raise ValueError(
            "Dữ liệu đồ thị quá lớn"
        )

    vertices = []

    for v in raw_vertices:

        vertices.append(
            VertexInput(
                id=str(v["id"]),
                x=float(v.get("x", 0)),
                y=float(v.get("y", 0)),
                z=float(v.get("z", 0)),
                confidence=float(
                    v.get("confidence", 1)
                )
            )
        )

    edges = []

    for edge in raw_edges:

        if (
            not isinstance(edge, list)
            or len(edge) != 2
        ):
            raise ValueError(
                "Định dạng cạnh không hợp lệ"
            )

        edges.append(
            (
                str(edge[0]),
                str(edge[1])
            )
        )

    frame_id = int(
        packet.get("frame_id", 0)
    )

    data = CVDataInput(
        timestamp=float(
            packet.get(
                "timestamp",
                time.time()
            )
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

    result = asdict(
        engine.process(data)
    )

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


# =========================================================
# RECEIVE REQUESTS FROM UNITY
# =========================================================

def receive_requests(client):

    shape_drop_engine = ShapeDropEngine()

    try:

        with client.sock.makefile(
            "r",
            encoding="utf-8"
        ) as reader:

            while client.alive:

                line = reader.readline(1000001)

                if not line:
                    break

                if not client.alive:
                    break

                if len(line) > 1000000:
                    break

                try:

                    packet = json.loads(line)

                    if not isinstance(packet, dict):
                        continue

                    if packet.get("source") != "role3_unity":
                        continue

                    packet_type = packet.get(
                        "type", ""
                    )

                    if packet_type == "phone_camera_frame":
                        if client.camera_source == "phone":
                            if client.phone_frames.full():
                                try:
                                    client.phone_frames.get_nowait()
                                except queue.Empty:
                                    pass
                            client.phone_frames.put_nowait(packet)
                        continue

                    if len(line) > 100000:
                        break

                    # ---------------------------------
                    # PING
                    # ---------------------------------

                    if packet_type == "ping":

                        client.send({
                            "source": "smartgeoar_backend",
                            "type": "pong"
                        })

                        continue

                    # ---------------------------------
                    # NEW MODE: THẢ HÌNH
                    # ---------------------------------

                    if packet_type == "shape_drop_request":

                        result = (
                            shape_drop_engine.evaluate(
                                packet
                            )
                        )

                        client.send(result)

                        continue

                    # ---------------------------------
                    # OLD MODE: GHÉP CẠNH
                    # ---------------------------------

                    result = evaluate_graph(
                        packet
                    )

                    client.send(result)

                except Exception as error:

                    print(
                        "[BACKEND ERROR]",
                        # Redirected Windows stdout may not support Vietnamese.
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


# =========================================================
# ROLE 1 - CAMERA + MEDIAPIPE
# =========================================================

def stream_phone_camera(client):

    tracker = PhoneHandTracking()
    owner = PinchOwnerGuard()
    last_heartbeat = 0.0
    last_frame = -1

    try:
        while client.alive:
            try:
                packet = client.phone_frames.get(timeout=0.25)
            except queue.Empty:
                if time.monotonic() - last_heartbeat >= 1.0:
                    last_heartbeat = time.monotonic()
                    if not client.send({"source": "smartgeoar_backend", "type": "pong"}):
                        break
                continue

            frame_id = packet.get("frame_id")

            if not isinstance(frame_id, int) or frame_id <= last_frame:
                continue

            last_frame = frame_id
            hands = []

            try:
                encoded = packet.get("jpeg", "")
                if not isinstance(encoded, str) or len(encoded) > 700000:
                    raise ValueError("Phone camera frame is too large")
                data = base64.b64decode(encoded, validate=True)
                frame = cv2.imdecode(np.frombuffer(data, dtype=np.uint8), cv2.IMREAD_COLOR)
                if frame is None or frame.shape[0] * frame.shape[1] > 1280 * 1280:
                    raise ValueError("Invalid phone camera image")
                intrinsics = packet.get("intrinsics")
                if not isinstance(intrinsics, dict):
                    raise ValueError("Phone camera intrinsics are missing")
                hands = owner.filter(tracker.process(frame, frame_id, intrinsics))
            except (ValueError, TypeError, cv2.error) as error:
                print("[PHONE CAMERA]", ascii(error), flush=True)

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

def stream_camera(client, stream_video=True):

    # Mỗi phiên kết nối có bộ khóa tay riêng.
    hand_owner_guard = PinchOwnerGuard()
    hand_pose = HandPoseEstimator()

    api = (
        cv2.CAP_DSHOW
        if sys.platform == "win32"
        else cv2.CAP_ANY
    )

    camera = cv2.VideoCapture(
        CAMERA_INDEX,
        api
    )

    if not camera.isOpened():

        client.send({
            "source": "smartgeoar_backend",
            "type": "camera_error",
            "message": "Không mở được webcam"
        })

        client.close()

        return

    camera.set(
        cv2.CAP_PROP_FRAME_WIDTH,
        640
    )

    camera.set(
        cv2.CAP_PROP_FRAME_HEIGHT,
        480
    )

    tracker = mp_hands.Hands(
        static_image_mode=False,
        max_num_hands=MAX_HANDS,
        min_detection_confidence=0.6,
        min_tracking_confidence=0.6
    )

    frame_id = 0

    last_video = 0.0

    try:

        while client.alive:

            ok, frame = camera.read()

            if not ok:
                time.sleep(0.05)
                continue

            # Lật gương webcam.
            frame = cv2.flip(frame, 1)

            height, width = frame.shape[:2]

            rgb = cv2.cvtColor(
                frame,
                cv2.COLOR_BGR2RGB
            )

            result = tracker.process(rgb)

            detected_hands = []

            visible_hands = set()

            # =================================================
            # NHẬN DIỆN TỐI ĐA 2 TAY
            # =================================================

            if result.multi_hand_landmarks:

                for index, landmarks in enumerate(
                    result.multi_hand_landmarks
                ):

                    hand_name = "Unknown"

                    if (
                        result.multi_handedness
                        and index <
                            len(result.multi_handedness)
                    ):

                        hand_name = (
                            result.multi_handedness[index]
                            .classification[0].label
                        )

                    if hand_name in visible_hands:
                        hand_name += "_2"

                    visible_hands.add(hand_name)

                    state = get_state(
                        hand_name
                    )

                    state["lost"] = 0

                    points = landmarks.landmark

                    thumb = points[
                        mp_hands.HandLandmark.THUMB_TIP
                    ]

                    finger = points[
                        mp_hands.HandLandmark.INDEX_FINGER_TIP
                    ]

                    wrist = points[
                        mp_hands.HandLandmark.WRIST
                    ]

                    middle = points[
                        mp_hands.HandLandmark.MIDDLE_FINGER_MCP
                    ]

                    palm_size = distance_2d(
                        wrist,
                        middle
                    )

                    ratio = (
                        distance_2d(
                            thumb,
                            finger
                        ) / palm_size
                        if palm_size > 1e-6
                        else 999.0
                    )

                    gesture = get_gesture(
                        state,
                        ratio
                    )

                    raw_x = float(
                        np.clip(
                            (thumb.x + finger.x) * 0.5,
                            0,
                            1
                        )
                    )

                    raw_y = float(
                        np.clip(
                            (thumb.y + finger.y) * 0.5,
                            0,
                            1
                        )
                    )

                    x, y = smooth_point(
                        state,
                        raw_x,
                        raw_y
                    )

                    state["last_pos"] = (
                        x, y
                    )

                    state["last_ratio"] = ratio

                    world_landmarks = None

                    if (
                        result.multi_hand_world_landmarks
                        and index < len(result.multi_hand_world_landmarks)
                    ):
                        world_landmarks = result.multi_hand_world_landmarks[index]

                    spatial = hand_pose.estimate(
                        hand_name,
                        landmarks,
                        world_landmarks,
                        width,
                        height
                    )

                    detected_hands.append(
                        make_hand_data(
                            hand_name,
                            gesture,
                            x,
                            y,
                            ratio,
                            spatial
                        )
                    )

            # =================================================
            # XỬ LÝ MẤT TRACKING TỪ ROLE 1
            # =================================================

            hand_pose.forget_missing(visible_hands)

            handle_missing_hands(
                visible_hands,
                detected_hands,
                frame_id
            )

            # =================================================
            # FIX TWO HAND BUG
            #
            # CÓ 2 TAY:
            #
            # Right -> PINCH_HOLD
            # Left  -> OPEN
            #
            # UNITY CHỈ NHẬN:
            #
            # Right -> PINCH_HOLD
            #
            # Left OPEN KHÔNG ĐƯỢC NHẢ CẠNH.
            # =================================================

            detected_hands = (
                hand_owner_guard.filter(
                    detected_hands
                )
            )

            # =================================================
            # GỬI GESTURE TỚI UNITY
            # =================================================

            if not client.send({
                "version": "1.0",
                "source": "role1_hand_tracking",
                "frame_id": frame_id,
                "coordinate_system": "normalized_image",
                "hands": detected_hands
            }):
                break

            frame_id += 1

            # A phone running ARCore already has its own camera background.
            # Keep laptop hand tracking active without sending redundant JPEGs.
            if not stream_video:
                continue

            # =================================================
            # STREAM CAMERA
            # =================================================

            now = time.monotonic()

            if (
                now - last_video
                < 1.0 / VIDEO_FPS
            ):
                continue

            last_video = now

            video = frame

            if width > MAX_VIDEO_WIDTH:

                scale = (
                    MAX_VIDEO_WIDTH / width
                )

                video = cv2.resize(
                    frame,
                    (
                        MAX_VIDEO_WIDTH,
                        int(height * scale)
                    )
                )

            ok, jpeg = cv2.imencode(
                ".jpg",
                video,
                [
                    cv2.IMWRITE_JPEG_QUALITY,
                    70
                ]
            )

            if ok:

                encoded = base64.b64encode(
                    jpeg.tobytes()
                ).decode("ascii")

                if not client.send({
                    "source": "role1_camera",
                    "jpeg": encoded
                }):
                    break

    finally:

        tracker.close()

        camera.release()

        client.close()


# =========================================================
# MAIN
# =========================================================

def main():

    parser = argparse.ArgumentParser()

    parser.add_argument(
        "--port",
        type=int,
        default=PORT
    )

    parser.add_argument(
        "--host",
        default=HOST,
        help="Address to listen on (use 0.0.0.0 for LAN clients)"
    )

    parser.add_argument(
        "--camera-source",
        choices=("phone", "webcam"),
        default="phone",
        help="Use the phone rear camera (default), or explicitly opt into a laptop webcam"
    )

    parser.add_argument(
        "--no-camera-stream",
        action="store_true",
        help="Do not send laptop webcam JPEG frames to Unity"
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

        server.bind(
            (args.host, args.port)
        )

        server.listen(1)

        print(
            f"SmartGeoAR backend: {args.host}:{args.port}",
            flush=True
        )
        print("Hand camera source:", args.camera_source, flush=True)

        while True:

            sock, _ = server.accept()

            sock.setsockopt(
                socket.IPPROTO_TCP,
                socket.TCP_NODELAY,
                1
            )

            client = AppConnection(sock, camera_source=args.camera_source)

            client.send({
                "source": "smartgeoar_backend",
                "type": "hello",
                "camera_source": args.camera_source
            })

            threading.Thread(
                target=receive_requests,
                args=(client,),
                daemon=True
            ).start()

            if args.camera_source == "phone":
                stream_phone_camera(client)
            else:
                stream_camera(client, stream_video=not args.no_camera_stream)


if __name__ == "__main__":
    main()
