
import cv2
import mediapipe as mp
import math
import json
import socket

# ==========================================================
# SMARTGEOAR - ROLE 1 / TUAN 4-5
# Hand tracking + pinch debounce + adaptive smoothing
# Lost tracking + UDP localhost
# ==========================================================

CAMERA_INDEX = 0
MAX_HANDS = 2

# ==========================================
# 1. CAU HINH GESTURE
# ==========================================

PINCH_START_THRESHOLD = 0.40
PINCH_END_THRESHOLD = 0.55

PINCH_CONFIRM_FRAMES = 2
RELEASE_CONFIRM_FRAMES = 2

LOST_FRAMES_TO_RELEASE = 5

# ==========================================
# 2. CAU HINH SMOOTHING
# ==========================================

SMOOTH_ALPHA_MIN = 0.18
SMOOTH_ALPHA_MAX = 0.72

MOVE_SCALE = 0.06
JITTER_DEADZONE = 0.002

# ==========================================
# 3. CAU HINH UDP LOCAL
# ==========================================

UDP_HOST = "127.0.0.1"
UDP_PORT = 5052

PRINT_JSON_EVERY_N_FRAMES = 30
SHOW_RAW_POINT = True

# ==========================================
# 4. MEDIAPIPE
# ==========================================

mp_hands = mp.solutions.hands
mp_drawing = mp.solutions.drawing_utils
mp_styles = mp.solutions.drawing_styles

hand_states = {}


# ==========================================
# 5. LUU TRANG THAI TUNG TAY
# ==========================================

def get_state(hand_name):

    if hand_name not in hand_states:

        hand_states[hand_name] = {
            "pinching": False,
            "pinch_frames": 0,
            "release_frames": 0,
            "smooth": None,
            "lost": 0,
            "last_pos": None,
            "last_ratio": 0.0
        }

    return hand_states[hand_name]


# ==========================================
# 6. TINH KHOANG CACH
# ==========================================

def distance_2d(a, b):

    return math.hypot(
        a.x - b.x,
        a.y - b.y
    )


# ==========================================
# 7. NHAN DIEN GESTURE
# ==========================================

def get_gesture(state, ratio):

    # Chua pinch
    if not state["pinching"]:

        state["release_frames"] = 0

        if ratio < PINCH_START_THRESHOLD:
            state["pinch_frames"] += 1
        else:
            state["pinch_frames"] = 0

        # Xac nhan nhieu frame
        if state["pinch_frames"] >= PINCH_CONFIRM_FRAMES:

            state["pinching"] = True
            state["pinch_frames"] = 0

            return "PINCH_START"

        return "OPEN"

    # Dang pinch
    state["pinch_frames"] = 0

    if ratio > PINCH_END_THRESHOLD:
        state["release_frames"] += 1
    else:
        state["release_frames"] = 0

    # Xac nhan tha tay
    if state["release_frames"] >= RELEASE_CONFIRM_FRAMES:

        state["pinching"] = False
        state["release_frames"] = 0

        return "PINCH_END"

    return "PINCH_HOLD"


# ==========================================
# 8. ADAPTIVE SMOOTHING
# ==========================================

def smooth_point(state, x, y):

    prev = state["smooth"]

    # Frame dau tien
    if prev is None:

        state["smooth"] = (x, y)
        return x, y

    # Khoang cach tu vi tri cu
    distance = math.hypot(
        x - prev[0],
        y - prev[1]
    )

    # Toc do di chuyen tuong doi
    speed_factor = min(
        1.0,
        distance / MOVE_SCALE
    )

    # Tu dong thay doi alpha
    alpha = (
        SMOOTH_ALPHA_MIN
        +
        (SMOOTH_ALPHA_MAX - SMOOTH_ALPHA_MIN)
        * speed_factor
    )

    # Tinh toa do sau khi loc
    sx = prev[0] + alpha * (x - prev[0])
    sy = prev[1] + alpha * (y - prev[1])

    # Bo qua rung nho
    if math.hypot(
        sx - prev[0],
        sy - prev[1]
    ) < JITTER_DEADZONE:

        return prev

    state["smooth"] = (sx, sy)

    return sx, sy


# ==========================================
# 9. TAO HAND JSON
# ==========================================

def make_hand_data(
    hand_name,
    gesture,
    x,
    y,
    ratio
):

    return {
        "hand": hand_name,
        "gesture": gesture,
        "interaction": {
            "x": round(float(x), 4),
            "y": round(float(y), 4)
        },
        "pinch_ratio": round(float(ratio), 4)
    }


# ==========================================
# 10. XU LY MAT TRACKING
# ==========================================

def handle_missing_hands(
    seen_hands,
    detected_hands,
    frame_id
):

    for hand_name, state in hand_states.items():

        # Tay van duoc nhan dien
        if hand_name in seen_hands:
            continue

        # Da xu ly mat tracking
        if state["lost"] >= LOST_FRAMES_TO_RELEASE:
            continue

        state["lost"] += 1

        # Mat tay du 5 frame
        if state["lost"] == LOST_FRAMES_TO_RELEASE:

            if (
                state["pinching"]
                and state["last_pos"] is not None
            ):

                x, y = state["last_pos"]

                # Gui PINCH_END ve Unity
                detected_hands.append(
                    make_hand_data(
                        hand_name,
                        "PINCH_END",
                        x,
                        y,
                        state["last_ratio"]
                    )
                )

                print(
                    f"Frame {frame_id} | "
                    f"{hand_name} | "
                    "PINCH_END (TRACKING LOST)"
                )

            # Reset trang thai
            state["pinching"] = False
            state["pinch_frames"] = 0
            state["release_frames"] = 0
            state["smooth"] = None


# ==========================================
# 11. MAIN
# ==========================================

def main():

    tracker = mp_hands.Hands(
        static_image_mode=False,
        max_num_hands=MAX_HANDS,
        min_detection_confidence=0.6,
        min_tracking_confidence=0.6
    )

    cap = cv2.VideoCapture(
        CAMERA_INDEX,
        cv2.CAP_DSHOW
    )

    udp_socket = socket.socket(
        socket.AF_INET,
        socket.SOCK_DGRAM
    )

    try:

        if not cap.isOpened():

            print(
                "Khong mo duoc camera. "
                "Kiem tra CAMERA_INDEX."
            )

            return

        print(
            f"ROLE 1 -> UNITY: "
            f"UDP {UDP_HOST}:{UDP_PORT}"
        )

        print("Q: Quit")

        frame_id = 0

        # ==================================
        # MAIN LOOP
        # ==================================

        while True:

            ok, frame = cap.read()

            if not ok:

                print("Khong doc duoc frame")
                break

            # Lat camera
            frame = cv2.flip(frame, 1)

            height, width = frame.shape[:2]

            # BGR -> RGB
            rgb = cv2.cvtColor(
                frame,
                cv2.COLOR_BGR2RGB
            )

            # Nhan dien tay
            results = tracker.process(rgb)

            detected_hands = []
            seen_hands = set()

            # ==================================
            # 12. XU LY TAY DUOC PHAT HIEN
            # ==================================

            if results.multi_hand_landmarks:

                for i, landmarks in enumerate(
                    results.multi_hand_landmarks
                ):

                    # Xac dinh tay trai/phai
                    if (
                        results.multi_handedness
                        and
                        i < len(results.multi_handedness)
                    ):

                        hand_name = (
                            results
                            .multi_handedness[i]
                            .classification[0]
                            .label
                        )

                    else:
                        hand_name = "Unknown"

                    # Lay state
                    state = get_state(hand_name)

                    state["lost"] = 0
                    seen_hands.add(hand_name)

                    # Lay landmark
                    thumb = landmarks.landmark[
                        mp_hands.HandLandmark.THUMB_TIP
                    ]

                    index = landmarks.landmark[
                        mp_hands.HandLandmark.INDEX_FINGER_TIP
                    ]

                    wrist = landmarks.landmark[
                        mp_hands.HandLandmark.WRIST
                    ]

                    middle = landmarks.landmark[
                        mp_hands.HandLandmark.MIDDLE_FINGER_MCP
                    ]

                    # ==================================
                    # 13. TINH PINCH RATIO
                    # ==================================

                    palm_size = distance_2d(
                        wrist,
                        middle
                    )

                    if palm_size > 1e-6:

                        ratio = (
                            distance_2d(thumb, index)
                            /
                            palm_size
                        )

                    else:
                        ratio = 999.0

                    # ==================================
                    # 14. NHAN DIEN GESTURE
                    # ==================================

                    gesture = get_gesture(
                        state,
                        ratio
                    )

                    # ==================================
                    # 15. TOA DO GOC
                    # ==================================

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

                    # ==================================
                    # 16. LAM MUOT TOA DO
                    # ==================================

                    x, y = smooth_point(
                        state,
                        raw_x,
                        raw_y
                    )

                    state["last_pos"] = (x, y)
                    state["last_ratio"] = ratio

                    # ==================================
                    # 17. TAO HAND DATA
                    # ==================================

                    detected_hands.append(
                        make_hand_data(
                            hand_name,
                            gesture,
                            x,
                            y,
                            ratio
                        )
                    )

                    # Log khi pinch/thả
                    if gesture in (
                        "PINCH_START",
                        "PINCH_END"
                    ):

                        print(
                            f"Frame {frame_id} | "
                            f"{hand_name} | "
                            f"{gesture} | "
                            f"ratio={ratio:.2f}"
                        )

                    # ==================================
                    # 18. VE LANDMARK
                    # ==================================

                    mp_drawing.draw_landmarks(
                        frame,
                        landmarks,
                        mp_hands.HAND_CONNECTIONS,
                        mp_styles.get_default_hand_landmarks_style(),
                        mp_styles.get_default_hand_connections_style()
                    )

                    px = int(x * width)
                    py = int(y * height)

                    # Diem goc mau do
                    if SHOW_RAW_POINT:

                        cv2.circle(
                            frame,
                            (
                                int(raw_x * width),
                                int(raw_y * height)
                            ),
                            5,
                            (0, 0, 255),
                            -1
                        )

                    # Diem sau smoothing
                    if state["pinching"]:
                        color = (0, 255, 0)
                    else:
                        color = (255, 255, 0)

                    cv2.circle(
                        frame,
                        (px, py),
                        11,
                        color,
                        2
                    )

                    # Hien thi gesture
                    cv2.putText(
                        frame,
                        f"{hand_name}: "
                        f"{gesture} ({ratio:.2f})",
                        (20, 40 + i * 35),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.65,
                        color,
                        2
                    )

            # ==================================
            # 19. XU LY MAT TRACKING
            # ==================================

            handle_missing_hands(
                seen_hands,
                detected_hands,
                frame_id
            )

            # ==================================
            # 20. TAO JSON PACKET
            # ==================================

            packet = {
                "version": "1.0",
                "source": "role1_hand_tracking",
                "frame_id": frame_id,
                "coordinate_system": "normalized_image",
                "hands": detected_hands
            }

            # ==================================
            # 21. GUI JSON SANG UNITY
            # ==================================

            try:

                message = json.dumps(
                    packet,
                    ensure_ascii=False
                ).encode("utf-8")

                udp_socket.sendto(
                    message,
                    (UDP_HOST, UDP_PORT)
                )

            except OSError as error:

                print("UDP ERROR:", error)

            # In JSON debug
            if frame_id % PRINT_JSON_EVERY_N_FRAMES == 0:

                print(
                    json.dumps(
                        packet,
                        ensure_ascii=False,
                        indent=2
                    )
                )

            # ==================================
            # 22. HIEN THI CAMERA
            # ==================================

            cv2.putText(
                frame,
                "RAW: red | "
                "FILTERED: yellow/green | "
                "Q: Quit",
                (20, height - 20),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.55,
                (255, 255, 255),
                1
            )

            cv2.imshow(
                "SmartGeoAR - Role 1",
                frame
            )

            frame_id += 1

            key = cv2.waitKey(1) & 0xFF

            if key == ord("q"):
                break

    finally:

        cap.release()
        tracker.close()
        udp_socket.close()

        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
