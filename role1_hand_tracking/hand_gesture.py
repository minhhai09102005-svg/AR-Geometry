import cv2
import mediapipe as mp
import math
import json
from pathlib import Path


# =========================================
# 1. CẤU HÌNH
# =========================================

CAMERA_INDEX = 0

# Ngưỡng bắt đầu pinch
PINCH_START_THRESHOLD = 0.40

# Ngưỡng thả pinch
# Đặt lớn hơn START để tránh trạng thái nhảy liên tục
PINCH_END_THRESHOLD = 0.55

OUTPUT_DIR = Path(__file__).resolve().parent / "output"
OUTPUT_DIR.mkdir(exist_ok=True)


# =========================================
# 2. MEDIAPIPE HANDS
# =========================================

mp_hands = mp.solutions.hands
mp_drawing = mp.solutions.drawing_utils
mp_drawing_styles = mp.solutions.drawing_styles

hands = mp_hands.Hands(
    static_image_mode=False,

    # 1 người dùng → tối đa 2 tay
    max_num_hands=2,

    min_detection_confidence=0.6,
    min_tracking_confidence=0.6
)


# =========================================
# 3. TRẠNG THÁI CỦA MỖI TAY
# =========================================

# Ví dụ:
#
# {
#     "Left": False,
#     "Right": True
# }
#
# True = tay đang pinch
pinch_state = {
    "Left": False,
    "Right": False
}


# =========================================
# 4. HÀM TÍNH KHOẢNG CÁCH 2 ĐIỂM
# =========================================

def distance_2d(point1, point2):
    return math.sqrt(
        (point1.x - point2.x) ** 2
        +
        (point1.y - point2.y) ** 2
    )


# =========================================
# 5. HÀM XÁC ĐỊNH GESTURE
# =========================================

def get_gesture(hand_name, pinch_ratio):

    was_pinching = pinch_state.get(hand_name, False)

    # -------------------------------------
    # Trước đó chưa pinch
    # -------------------------------------
    if not was_pinching:

        if pinch_ratio < PINCH_START_THRESHOLD:

            # Vừa bắt đầu chụm ngón
            pinch_state[hand_name] = True

            return "PINCH_START"

        return "OPEN"

    # -------------------------------------
    # Trước đó đang pinch
    # -------------------------------------
    else:

        if pinch_ratio > PINCH_END_THRESHOLD:

            # Vừa tách hai ngón ra
            pinch_state[hand_name] = False

            return "PINCH_END"

        # Vẫn giữ hai ngón
        return "PINCH_HOLD"


# =========================================
# 6. MỞ CAMERA
# =========================================

cap = cv2.VideoCapture(
    CAMERA_INDEX,
    cv2.CAP_DSHOW
)

if not cap.isOpened():
    print("Không mở được camera")
    raise SystemExit


print("SmartGeoAR Hand Gesture")
print("-----------------------")
print("PINCH_START = bắt đầu chụm")
print("PINCH_HOLD  = đang giữ")
print("PINCH_END   = vừa thả")
print("OPEN        = tay đang mở")
print()


# =========================================
# 7. VÒNG LẶP CAMERA
# =========================================

while True:

    ret, frame = cap.read()

    if not ret:
        print("Không đọc được frame")
        break

    # Camera dạng gương
    frame = cv2.flip(frame, 1)

    height, width, _ = frame.shape

    # OpenCV BGR → MediaPipe RGB
    rgb = cv2.cvtColor(
        frame,
        cv2.COLOR_BGR2RGB
    )

    results = hands.process(rgb)

    current_hands = []


    # =====================================
    # 8. NẾU PHÁT HIỆN BÀN TAY
    # =====================================

    if results.multi_hand_landmarks:

        for i, hand_landmarks in enumerate(
            results.multi_hand_landmarks
        ):

            # ---------------------------------
            # Xác định tay trái / phải
            # ---------------------------------

            if results.multi_handedness:

                hand_name = (
                    results
                    .multi_handedness[i]
                    .classification[0]
                    .label
                )

            else:
                hand_name = "Unknown"


            # ---------------------------------
            # Vẽ 21 landmark
            # ---------------------------------

            mp_drawing.draw_landmarks(
                frame,
                hand_landmarks,
                mp_hands.HAND_CONNECTIONS,
                mp_drawing_styles
                .get_default_hand_landmarks_style(),
                mp_drawing_styles
                .get_default_hand_connections_style()
            )


            # =================================
            # 9. LẤY CÁC LANDMARK QUAN TRỌNG
            # =================================

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


            # =================================
            # 10. TÍNH PINCH RATIO
            # =================================

            # Khoảng cách ngón cái - ngón trỏ
            finger_distance = distance_2d(
                thumb_tip,
                index_tip
            )

            # Kích thước tương đối của bàn tay
            palm_size = distance_2d(
                wrist,
                middle_mcp
            )

            if palm_size > 0:

                pinch_ratio = (
                    finger_distance
                    /
                    palm_size
                )

            else:
                pinch_ratio = 999


            # =================================
            # 11. XÁC ĐỊNH GESTURE
            # =================================

            gesture = get_gesture(
                hand_name,
                pinch_ratio
            )


            # =================================
            # 12. TỌA ĐỘ PIXEL
            # =================================

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


            # Tâm giữa ngón cái và trỏ
            # Đây là vị trí interaction chính
            interaction_x = int(
                (thumb_x + index_x) / 2
            )

            interaction_y = int(
                (thumb_y + index_y) / 2
            )


            # =================================
            # 13. MÀU THEO TRẠNG THÁI
            # =================================

            if gesture == "PINCH_START":

                color = (0, 255, 0)

            elif gesture == "PINCH_HOLD":

                color = (0, 255, 255)

            elif gesture == "PINCH_END":

                color = (255, 0, 255)

            else:

                color = (0, 0, 255)


            # =================================
            # 14. VẼ LÊN CAMERA
            # =================================

            # đầu ngón cái
            cv2.circle(
                frame,
                (thumb_x, thumb_y),
                8,
                color,
                -1
            )

            # đầu ngón trỏ
            cv2.circle(
                frame,
                (index_x, index_y),
                8,
                color,
                -1
            )

            # đường nối hai ngón
            cv2.line(
                frame,
                (thumb_x, thumb_y),
                (index_x, index_y),
                color,
                3
            )

            # vị trí tương tác
            cv2.circle(
                frame,
                (
                    interaction_x,
                    interaction_y
                ),
                10,
                (255, 255, 255),
                2
            )


            # =================================
            # 15. HIỂN THỊ TEXT
            # =================================

            cv2.putText(
                frame,
                f"{hand_name}: {gesture}",
                (
                    20,
                    70 + i * 70
                ),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.8,
                color,
                2
            )

            cv2.putText(
                frame,
                f"ratio: {pinch_ratio:.2f}",
                (
                    20,
                    100 + i * 70
                ),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.6,
                color,
                2
            )


            # =================================
            # 16. DATA ĐẦU RA ROLE 1
            # =================================

            hand_data = {

                "hand": hand_name,

                "gesture": gesture,

                # vị trí interaction chuẩn hóa 0 → 1
                "interaction": {

                    "x": round(
                        interaction_x / width,
                        4
                    ),

                    "y": round(
                        interaction_y / height,
                        4
                    ),

                    "pixel_x": interaction_x,
                    "pixel_y": interaction_y
                },

                "thumb_tip": {

                    "x": round(
                        float(thumb_tip.x),
                        4
                    ),

                    "y": round(
                        float(thumb_tip.y),
                        4
                    )
                },

                "index_tip": {

                    "x": round(
                        float(index_tip.x),
                        4
                    ),

                    "y": round(
                        float(index_tip.y),
                        4
                    )
                },

                "pinch_ratio": round(
                    float(pinch_ratio),
                    4
                )
            }

            current_hands.append(
                hand_data
            )


    # =====================================
    # 17. HƯỚNG DẪN
    # =====================================

    cv2.putText(
        frame,
        "S: Save JSON | Q: Quit",
        (20, 30),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.7,
        (255, 255, 255),
        2
    )


    cv2.imshow(
        "SmartGeoAR - Hand Gesture",
        frame
    )


    key = cv2.waitKey(1) & 0xFF


    # =====================================
    # 18. SAVE JSON
    # =====================================

    if key == ord("s"):

        data = {

            "source": "role1_hand_tracking",

            "coordinate_system":
                "normalized_image",

            "hands": current_hands
        }


        json_path = (
            OUTPUT_DIR
            /
            "hand_gesture.json"
        )


        with open(
            json_path,
            "w",
            encoding="utf-8"
        ) as f:

            json.dump(
                data,
                f,
                indent=4,
                ensure_ascii=False
            )


        cv2.imwrite(
            str(
                OUTPUT_DIR
                /
                "hand_gesture.png"
            ),
            frame
        )


        print()
        print("Đã lưu:")
        print(json_path)

        print(
            json.dumps(
                data,
                indent=4,
                ensure_ascii=False
            )
        )


    # =====================================
    # 19. THOÁT
    # =====================================

    if key == ord("q"):
        break


# =========================================
# 20. GIẢI PHÓNG
# =========================================

cap.release()
hands.close()
cv2.destroyAllWindows()