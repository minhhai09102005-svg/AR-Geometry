
import csv
import json
import math
import socket
import statistics
import time
from datetime import datetime
from pathlib import Path

import cv2
import mediapipe as mp


# ==========================================
# SMARTGEOAR - ROLE 1
# FPS OPTIMIZED BENCHMARK
# ==========================================

# CAMERA
CAMERA_INDEX = 0
CAMERA_WIDTH = 640
CAMERA_HEIGHT = 480
CAMERA_FPS = 30

# Giam do phan giai xu ly
PROCESS_WIDTH = 480

# MVP chi can 1 tay
MAX_HANDS = 1

# Thu che do MJPG de webcam co the
# truyen frame nhanh hon
USE_MJPG = True

# Hien thi camera
SHOW_PREVIEW = True

# Tat ve 21 landmark de tang toc
DRAW_LANDMARKS = False


# ==========================================
# GESTURE CONFIG
# ==========================================

PINCH_START_THRESHOLD = 0.40
PINCH_END_THRESHOLD = 0.55

PINCH_CONFIRM_FRAMES = 2
RELEASE_CONFIRM_FRAMES = 2

LOST_FRAMES_TO_RELEASE = 5


# ==========================================
# SMOOTHING CONFIG
# ==========================================

SMOOTH_ALPHA_MIN = 0.18
SMOOTH_ALPHA_MAX = 0.72

MOVE_SCALE = 0.06
JITTER_DEADZONE = 0.002


# ==========================================
# UDP CONFIG
# ==========================================

UDP_HOST = "127.0.0.1"
UDP_PORT = 5052


# ==========================================
# MEDIAPIPE
# ==========================================

mp_hands = mp.solutions.hands
mp_draw = mp.solutions.drawing_utils

states = {}


# ==========================================
# LUU TRANG THAI TUNG TAY
# ==========================================

def get_state(name):

    if name not in states:

        states[name] = {
            "pinching": False,
            "pinch_frames": 0,
            "release_frames": 0,
            "lost": LOST_FRAMES_TO_RELEASE,
            "smooth": None,
            "prev_raw": None,
            "last_pos": None,
            "last_ratio": 0.0
        }

    return states[name]


# ==========================================
# GESTURE DETECTION
# ==========================================

def get_gesture(state, ratio):

    if not state["pinching"]:

        state["release_frames"] = 0

        if ratio < PINCH_START_THRESHOLD:
            state["pinch_frames"] += 1
        else:
            state["pinch_frames"] = 0

        if state["pinch_frames"] >= PINCH_CONFIRM_FRAMES:

            state["pinching"] = True
            state["pinch_frames"] = 0

            return "PINCH_START"

        return "OPEN"

    state["pinch_frames"] = 0

    if ratio > PINCH_END_THRESHOLD:
        state["release_frames"] += 1
    else:
        state["release_frames"] = 0

    if state["release_frames"] >= RELEASE_CONFIRM_FRAMES:

        state["pinching"] = False
        state["release_frames"] = 0

        return "PINCH_END"

    return "PINCH_HOLD"


# ==========================================
# ADAPTIVE SMOOTHING
# ==========================================

def smooth_point(state, x, y):

    old = state["smooth"]

    if old is None:
        state["smooth"] = (x, y)
        return x, y

    distance = math.hypot(
        x - old[0],
        y - old[1]
    )

    factor = min(
        1.0,
        distance / MOVE_SCALE
    )

    alpha = (
        SMOOTH_ALPHA_MIN
        + (SMOOTH_ALPHA_MAX - SMOOTH_ALPHA_MIN)
        * factor
    )

    new_x = old[0] + alpha * (x - old[0])
    new_y = old[1] + alpha * (y - old[1])

    if math.hypot(
        new_x - old[0],
        new_y - old[1]
    ) < JITTER_DEADZONE:

        return old

    state["smooth"] = (new_x, new_y)

    return new_x, new_y


# ==========================================
# JSON COMPATIBLE UNITY
# ==========================================

def hand_json(name, gesture, x, y, ratio):

    return {
        "hand": name,
        "gesture": gesture,
        "interaction": {
            "x": round(float(x), 4),
            "y": round(float(y), 4)
        },
        "pinch_ratio": round(float(ratio), 4)
    }


# ==========================================
# STATISTICS
# ==========================================

def mean(values):

    if not values:
        return 0.0

    return statistics.mean(values)


def p95(values):

    if not values:
        return 0.0

    ordered = sorted(values)

    index = math.ceil(
        len(ordered) * 0.95
    ) - 1

    return ordered[index]


# ==========================================
# CAMERA CONFIGURATION
# ==========================================

def open_camera():

    cap = cv2.VideoCapture(
        CAMERA_INDEX,
        cv2.CAP_DSHOW
    )

    if not cap.isOpened():

        cap.release()

        cap = cv2.VideoCapture(
            CAMERA_INDEX
        )

    if not cap.isOpened():
        return cap

    # MJPG co the giup tang FPS
    if USE_MJPG:

        cap.set(
            cv2.CAP_PROP_FOURCC,
            cv2.VideoWriter_fourcc(*"MJPG")
        )

    cap.set(
        cv2.CAP_PROP_FRAME_WIDTH,
        CAMERA_WIDTH
    )

    cap.set(
        cv2.CAP_PROP_FRAME_HEIGHT,
        CAMERA_HEIGHT
    )

    cap.set(
        cv2.CAP_PROP_FPS,
        CAMERA_FPS
    )

    cap.set(
        cv2.CAP_PROP_BUFFERSIZE,
        1
    )

    return cap


# ==========================================
# MAIN
# ==========================================

def main():

    folder = (
        Path(__file__).resolve().parent
        / "test_results"
    )

    folder.mkdir(
        parents=True,
        exist_ok=True
    )

    filename = folder / (
        f"role1_fps_optimized_"
        f"{datetime.now():%Y%m%d_%H%M%S}.csv"
    )

    camera = open_camera()

    if not camera.isOpened():

        print("ERROR: Khong mo duoc webcam")
        camera.release()
        return

    tracker = None

    sock = socket.socket(
        socket.AF_INET,
        socket.SOCK_DGRAM
    )

    fps_values = []
    mp_values = []
    processing_values = []

    still_raw = []
    still_filtered = []

    event_counts = {
        "PINCH_START": 0,
        "PINCH_END": 0,
        "LOST_RELEASE": 0
    }

    frame_id = 0

    still_mode = False
    still_begin_frame = 0

    previous_frame_start = None

    start_time = time.perf_counter()
    last_log = start_time

    columns = [
        "elapsed_s",
        "frame_id",
        "mode",
        "fps",
        "mediapipe_ms",
        "processing_ms",
        "hand_count",
        "hand",
        "gesture",
        "pinch_ratio",
        "raw_x",
        "raw_y",
        "smooth_x",
        "smooth_y",
        "raw_step_px",
        "smooth_step_px"
    ]

    try:

        # ==================================
        # MEDIAPIPE MODEL NHE
        # ==================================

        tracker = mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=MAX_HANDS,
            model_complexity=0,
            min_detection_confidence=0.6,
            min_tracking_confidence=0.6
        )

        print("=== SMARTGEOAR FPS BENCHMARK ===")

        print(
            "Camera resolution:",
            int(camera.get(cv2.CAP_PROP_FRAME_WIDTH)),
            "x",
            int(camera.get(cv2.CAP_PROP_FRAME_HEIGHT))
        )

        print(
            "Camera requested FPS:",
            CAMERA_FPS
        )

        print(
            "Camera reported FPS:",
            camera.get(cv2.CAP_PROP_FPS)
        )

        print(
            "MediaPipe input width:",
            PROCESS_WIDTH
        )

        print(
            f"UDP -> {UDP_HOST}:{UDP_PORT}"
        )

        print("S: STILL TEST")
        print("Q: QUIT")
        print("CSV:", filename)

        with filename.open(
            "w",
            newline="",
            encoding="utf-8-sig"
        ) as file:

            writer = csv.DictWriter(
                file,
                fieldnames=columns
            )

            writer.writeheader()

            # ==================================
            # MAIN LOOP
            # ==================================

            while True:

                frame_start = time.perf_counter()

                # ==========================
                # 1. DO FPS
                # ==========================

                fps = 0.0

                if previous_frame_start is not None:

                    dt = (
                        frame_start
                        - previous_frame_start
                    )

                    if dt > 0:

                        fps = 1.0 / dt
                        fps_values.append(fps)

                previous_frame_start = frame_start

                # ==========================
                # 2. DOC CAMERA
                # ==========================

                ok, frame = camera.read()

                if not ok:

                    print("Khong doc duoc frame")
                    break

                frame = cv2.flip(frame, 1)

                height, width = frame.shape[:2]

                # ==========================
                # 3. GIAM RESOLUTION
                # ==========================

                if width > PROCESS_WIDTH:

                    small_height = max(
                        1,
                        round(
                            height
                            * PROCESS_WIDTH
                            / width
                        )
                    )

                    small = cv2.resize(
                        frame,
                        (
                            PROCESS_WIDTH,
                            small_height
                        ),
                        interpolation=cv2.INTER_AREA
                    )

                else:
                    small = frame

                rgb = cv2.cvtColor(
                    small,
                    cv2.COLOR_BGR2RGB
                )

                # Toi uu MediaPipe
                rgb.flags.writeable = False

                # ==========================
                # 4. MEDIAPIPE PROCESS
                # ==========================

                mp_start = time.perf_counter()

                results = tracker.process(rgb)

                mp_ms = (
                    time.perf_counter()
                    - mp_start
                ) * 1000

                mp_values.append(mp_ms)

                hands_out = []
                rows = []
                seen_hands = set()

                landmarks_list = (
                    results.multi_hand_landmarks
                    or []
                )

                # ==========================
                # 5. XU LY TUNG TAY
                # ==========================

                for i, landmarks in enumerate(
                    landmarks_list
                ):

                    name = "Unknown"

                    if (
                        results.multi_handedness
                        and i < len(results.multi_handedness)
                    ):

                        name = (
                            results.multi_handedness[i]
                            .classification[0].label
                        )

                    state = get_state(name)

                    state["lost"] = 0

                    seen_hands.add(name)

                    points = landmarks.landmark

                    thumb = points[
                        mp_hands.HandLandmark.THUMB_TIP
                    ]

                    index = points[
                        mp_hands.HandLandmark.INDEX_FINGER_TIP
                    ]

                    wrist = points[
                        mp_hands.HandLandmark.WRIST
                    ]

                    middle = points[
                        mp_hands.HandLandmark.MIDDLE_FINGER_MCP
                    ]

                    # ==========================
                    # 6. PINCH RATIO
                    # ==========================

                    finger_size = math.hypot(
                        thumb.x - index.x,
                        thumb.y - index.y
                    )

                    palm_size = math.hypot(
                        wrist.x - middle.x,
                        wrist.y - middle.y
                    )

                    if palm_size > 1e-6:

                        ratio = (
                            finger_size
                            / palm_size
                        )

                    else:
                        ratio = 999.0

                    gesture = get_gesture(
                        state,
                        ratio
                    )

                    if gesture in (
                        "PINCH_START",
                        "PINCH_END"
                    ):

                        event_counts[gesture] += 1

                        print(
                            f"Frame {frame_id} | "
                            f"{name} | {gesture}"
                        )

                    # ==========================
                    # 7. RAW COORDINATES
                    # ==========================

                    raw_x = (
                        thumb.x + index.x
                    ) / 2

                    raw_y = (
                        thumb.y + index.y
                    ) / 2

                    raw_x = max(
                        0.0,
                        min(1.0, raw_x)
                    )

                    raw_y = max(
                        0.0,
                        min(1.0, raw_y)
                    )

                    old_raw = state["prev_raw"]
                    old_smooth = state["smooth"]

                    # ==========================
                    # 8. SMOOTHING
                    # ==========================

                    x, y = smooth_point(
                        state,
                        raw_x,
                        raw_y
                    )

                    raw_step = 0.0
                    smooth_step = 0.0

                    if old_raw is not None:

                        raw_step = math.hypot(
                            (raw_x - old_raw[0]) * width,
                            (raw_y - old_raw[1]) * height
                        )

                    if old_smooth is not None:

                        smooth_step = math.hypot(
                            (x - old_smooth[0]) * width,
                            (y - old_smooth[1]) * height
                        )

                    state["prev_raw"] = (
                        raw_x,
                        raw_y
                    )

                    state["last_pos"] = (
                        x,
                        y
                    )

                    state["last_ratio"] = ratio

                    # ==========================
                    # 9. JSON HAND DATA
                    # ==========================

                    hands_out.append(
                        hand_json(
                            name,
                            gesture,
                            x,
                            y,
                            ratio
                        )
                    )

                    # ==========================
                    # 10. SAVE METRICS
                    # ==========================

                    rows.append({
                        "hand": name,
                        "gesture": gesture,
                        "pinch_ratio": round(ratio, 4),
                        "raw_x": round(raw_x, 4),
                        "raw_y": round(raw_y, 4),
                        "smooth_x": round(x, 4),
                        "smooth_y": round(y, 4),
                        "raw_step_px": round(raw_step, 3),
                        "smooth_step_px": round(
                            smooth_step, 3
                        )
                    })

                    if (
                        still_mode
                        and frame_id - still_begin_frame >= 3
                    ):

                        if (
                            old_raw is not None
                            and old_smooth is not None
                        ):

                            still_raw.append(raw_step)

                            still_filtered.append(
                                smooth_step
                            )

                    # ==========================
                    # 11. CAMERA DEBUG
                    # ==========================

                    if SHOW_PREVIEW:

                        if DRAW_LANDMARKS:

                            mp_draw.draw_landmarks(
                                frame,
                                landmarks,
                                mp_hands.HAND_CONNECTIONS
                            )

                        # Raw point
                        cv2.circle(
                            frame,
                            (
                                int(raw_x * width),
                                int(raw_y * height)
                            ),
                            4,
                            (0, 0, 255),
                            -1
                        )

                        if state["pinching"]:
                            color = (0, 255, 0)
                        else:
                            color = (0, 255, 255)

                        # Smooth point
                        cv2.circle(
                            frame,
                            (
                                int(x * width),
                                int(y * height)
                            ),
                            10,
                            color,
                            2
                        )

                        cv2.putText(
                            frame,
                            f"{name}: {gesture} "
                            f"({ratio:.2f})",
                            (15, 35 + i * 30),
                            cv2.FONT_HERSHEY_SIMPLEX,
                            0.6,
                            color,
                            2
                        )

                # ==========================
                # 12. LOST TRACKING
                # ==========================

                for name, state in states.items():

                    if (
                        name in seen_hands
                        or state["lost"]
                        >= LOST_FRAMES_TO_RELEASE
                    ):
                        continue

                    state["lost"] += 1

                    if (
                        state["lost"]
                        == LOST_FRAMES_TO_RELEASE
                    ):

                        if (
                            state["pinching"]
                            and state["last_pos"] is not None
                        ):

                            x, y = state["last_pos"]

                            hands_out.append(
                                hand_json(
                                    name,
                                    "PINCH_END",
                                    x,
                                    y,
                                    state["last_ratio"]
                                )
                            )

                            event_counts[
                                "LOST_RELEASE"
                            ] += 1

                            event_counts[
                                "PINCH_END"
                            ] += 1

                            print(
                                f"Frame {frame_id} | "
                                f"{name} | "
                                "PINCH_END (LOST)"
                            )

                        state["pinching"] = False
                        state["pinch_frames"] = 0
                        state["release_frames"] = 0
                        state["smooth"] = None
                        state["prev_raw"] = None

                # ==========================
                # 13. CREATE JSON PACKET
                # ==========================

                packet = {
                    "version": "1.0",
                    "source": "role1_hand_tracking",
                    "frame_id": frame_id,
                    "coordinate_system": "normalized_image",
                    "hands": hands_out
                }

                # ==========================
                # 14. SEND UDP
                # ==========================

                try:

                    message = json.dumps(
                        packet
                    ).encode("utf-8")

                    sock.sendto(
                        message,
                        (UDP_HOST, UDP_PORT)
                    )

                except OSError as error:

                    print("UDP ERROR:", error)

                # ==========================
                # 15. PROCESSING TIME
                # ==========================

                processing_ms = (
                    time.perf_counter()
                    - frame_start
                ) * 1000

                processing_values.append(
                    processing_ms
                )

                # ==========================
                # 16. WRITE CSV
                # ==========================

                common = {
                    "elapsed_s": round(
                        frame_start - start_time, 3
                    ),
                    "frame_id": frame_id,
                    "mode": (
                        "STILL"
                        if still_mode
                        else "NORMAL"
                    ),
                    "fps": round(fps, 2),
                    "mediapipe_ms": round(mp_ms, 2),
                    "processing_ms": round(
                        processing_ms, 2
                    ),
                    "hand_count": len(
                        landmarks_list
                    )
                }

                for row in rows or [{}]:

                    writer.writerow({
                        **common,
                        **row
                    })

                # ==========================
                # 17. LOG FPS
                # ==========================

                if (
                    frame_start - last_log
                    >= 3.0
                ):

                    print(
                        f"FPS: "
                        f"{mean(fps_values[-90:]):.1f}"
                        f" | MediaPipe: "
                        f"{mean(mp_values[-90:]):.1f} ms"
                        f" | Processing: "
                        f"{mean(processing_values[-90:]):.1f} ms"
                    )

                    last_log = frame_start

                # ==========================
                # 18. SHOW PREVIEW
                # ==========================

                if SHOW_PREVIEW:

                    cv2.putText(
                        frame,
                        f"FPS: {fps:.1f} | "
                        f"MP: {mp_ms:.1f} ms",
                        (15, height - 45),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.6,
                        (255, 255, 255),
                        2
                    )

                    cv2.putText(
                        frame,
                        "S: Still test | Q: Quit",
                        (15, height - 20),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.6,
                        (255, 255, 255),
                        2
                    )

                    cv2.imshow(
                        "SmartGeoAR - FPS Optimized",
                        frame
                    )

                    key = cv2.waitKey(1) & 0xFF

                    if key == ord("q"):
                        break

                    if key == ord("s"):

                        still_mode = not still_mode

                        if still_mode:
                            still_raw.clear()
                            still_filtered.clear()
                            still_begin_frame = frame_id

                        print(
                            "STILL TEST:",
                            "ON" if still_mode else "OFF"
                        )

                frame_id += 1

    except KeyboardInterrupt:

        print("\nDa dung bang Ctrl+C")

    finally:

        camera.release()

        if tracker is not None:
            tracker.close()

        sock.close()
        cv2.destroyAllWindows()

    # ==================================
    # FINAL REPORT
    # ==================================

    print("\n======== FPS BENCHMARK ========")

    print("So frame:", frame_id)

    print(
        f"FPS trung binh: "
        f"{mean(fps_values):.2f}"
    )

    print(
        f"MediaPipe TB / P95: "
        f"{mean(mp_values):.2f} / "
        f"{p95(mp_values):.2f} ms"
    )

    print(
        f"Processing TB / P95: "
        f"{mean(processing_values):.2f} / "
        f"{p95(processing_values):.2f} ms"
    )

    print(
        "PINCH_START:",
        event_counts["PINCH_START"]
    )

    print(
        "PINCH_END:",
        event_counts["PINCH_END"]
    )

    print(
        "LOST_RELEASE:",
        event_counts["LOST_RELEASE"]
    )

    if still_raw:

        raw = mean(still_raw)
        filtered = mean(still_filtered)

        print(
            f"Jitter RAW: {raw:.3f} px/frame"
        )

        print(
            f"Jitter FILTERED: "
            f"{filtered:.3f} px/frame"
        )

        if raw > 0:

            reduction = (
                1.0 - filtered / raw
            ) * 100

            print(
                f"Giam bien dong: "
                f"{reduction:.1f}%"
            )

    else:

        print(
            "Chua co du lieu STILL TEST"
        )

    print("CSV:", filename)

    print("===============================")


if __name__ == "__main__":
    main()
