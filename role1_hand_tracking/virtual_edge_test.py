import cv2
import mediapipe as mp
import math


# =========================================================
# 1. CẤU HÌNH
# =========================================================

CAMERA_INDEX = 0

# Ngưỡng nhận diện pinch
PINCH_START_THRESHOLD = 0.40
PINCH_END_THRESHOLD = 0.55

# Kích thước cạnh ảo
EDGE_LENGTH = 160
EDGE_THICKNESS = 12

# Khoảng cách cho phép để gắp cạnh
GRAB_DISTANCE = 35

# Làm mượt chuyển động
# nhỏ hơn = mượt hơn nhưng chậm hơn
# lớn hơn = nhanh hơn nhưng rung hơn
SMOOTHING_ALPHA = 0.25


# =========================================================
# 2. MEDIAPIPE
# =========================================================

mp_hands = mp.solutions.hands
mp_drawing = mp.solutions.drawing_utils
mp_drawing_styles = mp.solutions.drawing_styles

hands = mp_hands.Hands(
    static_image_mode=False,
    max_num_hands=2,
    min_detection_confidence=0.6,
    min_tracking_confidence=0.6
)


# =========================================================
# 3. BIẾN TRẠNG THÁI
# =========================================================

pinch_state = {
    "Left": False,
    "Right": False
}

# Tâm ban đầu của cạnh ảo
edge_x = 400.0
edge_y = 250.0

# Cạnh đang bị cầm hay không
edge_grabbed = False

# Tay nào đang cầm
grabbed_by = None

# Offset để cạnh không bị nhảy vào giữa tay
grab_offset_x = 0
grab_offset_y = 0


# =========================================================
# 4. HÀM TÍNH KHOẢNG CÁCH
# =========================================================

def distance_2d(p1, p2):
    return math.sqrt(
        (p1.x - p2.x) ** 2 +
        (p1.y - p2.y) ** 2
    )


def point_to_segment_distance(px, py, x1, y1, x2, y2):

    dx = x2 - x1
    dy = y2 - y1

    if dx == 0 and dy == 0:
        return math.sqrt(
            (px - x1) ** 2 +
            (py - y1) ** 2
        )

    t = (
        (px - x1) * dx +
        (py - y1) * dy
    ) / (
        dx * dx + dy * dy
    )

    t = max(0, min(1, t))

    nearest_x = x1 + t * dx
    nearest_y = y1 + t * dy

    return math.sqrt(
        (px - nearest_x) ** 2 +
        (py - nearest_y) ** 2
    )


# =========================================================
# 5. HÀM XÁC ĐỊNH GESTURE
# =========================================================

def get_gesture(hand_name, pinch_ratio):

    was_pinching = pinch_state.get(
        hand_name,
        False
    )

    if not was_pinching:

        if pinch_ratio < PINCH_START_THRESHOLD:

            pinch_state[hand_name] = True

            return "PINCH_START"

        return "OPEN"

    else:

        if pinch_ratio > PINCH_END_THRESHOLD:

            pinch_state[hand_name] = False

            return "PINCH_END"

        return "PINCH_HOLD"


# =========================================================
# 6. MỞ CAMERA
# =========================================================

cap = cv2.VideoCapture(
    CAMERA_INDEX,
    cv2.CAP_DSHOW
)

if not cap.isOpened():
    print("Không mở được camera")
    raise SystemExit


# =========================================================
# 7. VÒNG LẶP CHÍNH
# =========================================================

while True:

    ret, frame = cap.read()

    if not ret:
        print("Không đọc được frame")
        break

    frame = cv2.flip(frame, 1)

    height, width, _ = frame.shape


    # =====================================================
    # 8. TỌA ĐỘ 2 ĐẦU CẠNH ẢO
    # =====================================================

    edge_start_x = int(
        edge_x - EDGE_LENGTH / 2
    )

    edge_start_y = int(edge_y)

    edge_end_x = int(
        edge_x + EDGE_LENGTH / 2
    )

    edge_end_y = int(edge_y)


    # =====================================================
    # 9. HAND TRACKING
    # =====================================================

    rgb = cv2.cvtColor(
        frame,
        cv2.COLOR_BGR2RGB
    )

    results = hands.process(rgb)


    if results.multi_hand_landmarks:

        for i, hand_landmarks in enumerate(
            results.multi_hand_landmarks
        ):

            # ---------------------------------------------
            # Xác định tay trái / phải
            # ---------------------------------------------

            if results.multi_handedness:

                hand_name = (
                    results
                    .multi_handedness[i]
                    .classification[0]
                    .label
                )

            else:
                hand_name = "Unknown"


            # ---------------------------------------------
            # Vẽ landmark
            # ---------------------------------------------

            mp_drawing.draw_landmarks(
                frame,
                hand_landmarks,
                mp_hands.HAND_CONNECTIONS,
                mp_drawing_styles
                .get_default_hand_landmarks_style(),
                mp_drawing_styles
                .get_default_hand_connections_style()
            )


            # =================================================
            # 10. LẤY LANDMARK
            # =================================================

            thumb_tip = hand_landmarks.landmark[
                mp_hands.HandLandmark.THUMB_TIP
            ]

            index_tip = hand_landmarks.landmark[
                mp_hands.HandLandmark.INDEX_FINGER_TIP
            ]

            wrist = hand_landmarks.landmark[
                mp_hands.HandLandmark.WRIST
            ]

            middle_mcp = hand_landmarks.landmark[
                mp_hands.HandLandmark.MIDDLE_FINGER_MCP
            ]


            # =================================================
            # 11. PINCH RATIO
            # =================================================

            finger_distance = distance_2d(
                thumb_tip,
                index_tip
            )

            palm_size = distance_2d(
                wrist,
                middle_mcp
            )

            if palm_size > 0:

                pinch_ratio = (
                    finger_distance / palm_size
                )

            else:
                pinch_ratio = 999


            gesture = get_gesture(
                hand_name,
                pinch_ratio
            )


            # =================================================
            # 12. TỌA ĐỘ PIXEL
            # =================================================

            thumb_x = int(
                thumb_tip.x * width
            )

            thumb_y = int(
                thumb_tip.y * height
            )

            index_x = int(
                index_tip.x * width
            )

            index_y = int(
                index_tip.y * height
            )


            # Điểm tương tác nằm giữa 2 ngón
            interaction_x = int(
                (thumb_x + index_x) / 2
            )

            interaction_y = int(
                (thumb_y + index_y) / 2
            )


            # =================================================
            # 13. KIỂM TRA TAY CÓ GẦN CẠNH KHÔNG
            # =================================================

            distance_to_edge = (
                point_to_segment_distance(
                    interaction_x,
                    interaction_y,

                    edge_start_x,
                    edge_start_y,

                    edge_end_x,
                    edge_end_y
                )
            )

            near_edge = (
                distance_to_edge
                <
                GRAB_DISTANCE
            )


            # =================================================
            # 14. GRAB
            # =================================================

            if (
                gesture == "PINCH_START"
                and
                near_edge
                and
                not edge_grabbed
            ):

                edge_grabbed = True
                grabbed_by = hand_name

                grab_offset_x = (
                    edge_x - interaction_x
                )

                grab_offset_y = (
                    edge_y - interaction_y
                )

                print(
                    f"{hand_name}: GRAB"
                )


            # =================================================
            # 15. MOVE + SMOOTHING
            # =================================================

            if (
                edge_grabbed
                and
                grabbed_by == hand_name
                and
                gesture in [
                    "PINCH_START",
                    "PINCH_HOLD"
                ]
            ):

                # Vị trí mục tiêu theo tay
                target_x = (
                    interaction_x
                    +
                    grab_offset_x
                )

                target_y = (
                    interaction_y
                    +
                    grab_offset_y
                )

                # -----------------------------
                # SMOOTHING
                # -----------------------------

                edge_x = (
                    edge_x
                    *
                    (1 - SMOOTHING_ALPHA)
                    +
                    target_x
                    *
                    SMOOTHING_ALPHA
                )

                edge_y = (
                    edge_y
                    *
                    (1 - SMOOTHING_ALPHA)
                    +
                    target_y
                    *
                    SMOOTHING_ALPHA
                )


                # Không cho cạnh chạy ra ngoài
                edge_x = max(
                    EDGE_LENGTH // 2,
                    min(
                        width - EDGE_LENGTH // 2,
                        edge_x
                    )
                )

                edge_y = max(
                    30,
                    min(
                        height - 30,
                        edge_y
                    )
                )


            # =================================================
            # 16. RELEASE
            # =================================================

            if (
                edge_grabbed
                and
                grabbed_by == hand_name
                and
                gesture == "PINCH_END"
            ):

                edge_grabbed = False
                grabbed_by = None

                print(
                    f"{hand_name}: RELEASE"
                )


            # =================================================
            # 17. MÀU GESTURE
            # =================================================

            if gesture == "PINCH_START":

                hand_color = (0, 255, 0)

            elif gesture == "PINCH_HOLD":

                hand_color = (0, 255, 255)

            elif gesture == "PINCH_END":

                hand_color = (255, 0, 255)

            else:

                hand_color = (0, 0, 255)


            # =================================================
            # 18. VẼ ĐIỂM INTERACTION
            # =================================================

            cv2.circle(
                frame,
                (
                    interaction_x,
                    interaction_y
                ),
                12,
                hand_color,
                3
            )

            cv2.putText(
                frame,
                f"{hand_name}: {gesture}",
                (
                    20,
                    70 + i * 40
                ),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.7,
                hand_color,
                2
            )


    # =========================================================
    # 19. TÍNH LẠI CẠNH SAU KHI MOVE
    # =========================================================

    edge_start_x = int(
        edge_x - EDGE_LENGTH / 2
    )

    edge_start_y = int(edge_y)

    edge_end_x = int(
        edge_x + EDGE_LENGTH / 2
    )

    edge_end_y = int(edge_y)


    # =========================================================
    # 20. VẼ CẠNH ẢO
    # =========================================================

    if edge_grabbed:

        edge_color = (0, 255, 0)

        status_text = (
            f"GRABBED BY {grabbed_by}"
        )

    else:

        edge_color = (255, 255, 0)

        status_text = "FREE"


    cv2.line(
        frame,
        (
            edge_start_x,
            edge_start_y
        ),
        (
            edge_end_x,
            edge_end_y
        ),
        edge_color,
        EDGE_THICKNESS
    )


    cv2.circle(
        frame,
        (
            edge_start_x,
            edge_start_y
        ),
        15,
        edge_color,
        -1
    )


    cv2.circle(
        frame,
        (
            edge_end_x,
            edge_end_y
        ),
        15,
        edge_color,
        -1
    )


    cv2.putText(
        frame,
        "VIRTUAL EDGE",
        (
            int(edge_x) - 70,
            int(edge_y) - 25
        ),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.6,
        edge_color,
        2
    )


    # =========================================================
    # 21. TRẠNG THÁI
    # =========================================================

    cv2.putText(
        frame,
        f"Edge: {status_text}",
        (20, 30),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.7,
        edge_color,
        2
    )


    cv2.putText(
        frame,
        f"Smoothing: {SMOOTHING_ALPHA}",
        (20, height - 20),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.55,
        (255, 255, 255),
        1
    )


    cv2.imshow(
        "SmartGeoAR - Virtual Edge Smooth",
        frame
    )


    key = cv2.waitKey(1) & 0xFF

    if key == ord("q"):
        break


# =========================================================
# 22. DỌN TÀI NGUYÊN
# =========================================================

cap.release()
hands.close()
cv2.destroyAllWindows()