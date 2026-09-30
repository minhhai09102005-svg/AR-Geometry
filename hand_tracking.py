import cv2
import mediapipe as mp
import math
import json
from pathlib import Path

# =========================
# 1. Cấu hình MediaPipe
# =========================
mp_hands = mp.solutions.hands
mp_drawing = mp.solutions.drawing_utils
mp_drawing_styles = mp.solutions.drawing_styles

hands = mp_hands.Hands(
    static_image_mode=False,
    max_num_hands=2,
    min_detection_confidence=0.6,
    min_tracking_confidence=0.6
)

# Thư mục lưu JSON
OUTPUT_DIR = Path(__file__).resolve().parent / "output"
OUTPUT_DIR.mkdir(exist_ok=True)

# =========================
# 2. Mở camera
# =========================
cap = cv2.VideoCapture(0, cv2.CAP_DSHOW)

if not cap.isOpened():
    print("Không mở được camera")
    raise SystemExit


while True:
    ret, frame = cap.read()

    if not ret:
        print("Không đọc được frame")
        break

    # Lật camera cho giống gương
    frame = cv2.flip(frame, 1)

    height, width, _ = frame.shape

    # OpenCV dùng BGR, MediaPipe dùng RGB
    rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)

    # =========================
    # 3. Nhận diện bàn tay
    # =========================
    results = hands.process(rgb)

    hand_data_list = []

    if results.multi_hand_landmarks:

        for i, hand_landmarks in enumerate(results.multi_hand_landmarks):

            # Vẽ 21 landmark lên bàn tay
            mp_drawing.draw_landmarks(
                frame,
                hand_landmarks,
                mp_hands.HAND_CONNECTIONS,
                mp_drawing_styles.get_default_hand_landmarks_style(),
                mp_drawing_styles.get_default_hand_connections_style()
            )

            # =========================
            # 4. Lấy landmark cần thiết
            # =========================

            # Đầu ngón cái - landmark 4
            thumb_tip = hand_landmarks.landmark[
                mp_hands.HandLandmark.THUMB_TIP
            ]

            # Đầu ngón trỏ - landmark 8
            index_tip = hand_landmarks.landmark[
                mp_hands.HandLandmark.INDEX_FINGER_TIP
            ]

            # Cổ tay - landmark 0
            wrist = hand_landmarks.landmark[
                mp_hands.HandLandmark.WRIST
            ]

            # Khớp giữa lòng bàn tay - landmark 9
            middle_mcp = hand_landmarks.landmark[
                mp_hands.HandLandmark.MIDDLE_FINGER_MCP
            ]

            # =========================
            # 5. Chuyển sang tọa độ pixel
            # =========================
            thumb_x = int(thumb_tip.x * width)
            thumb_y = int(thumb_tip.y * height)

            index_x = int(index_tip.x * width)
            index_y = int(index_tip.y * height)

            # =========================
            # 6. Tính khoảng cách pinch
            # =========================

            # Khoảng cách ngón cái -> ngón trỏ
            pinch_distance = math.sqrt(
                (thumb_tip.x - index_tip.x) ** 2 +
                (thumb_tip.y - index_tip.y) ** 2
            )

            # Kích thước bàn tay để chuẩn hóa
            palm_size = math.sqrt(
                (wrist.x - middle_mcp.x) ** 2 +
                (wrist.y - middle_mcp.y) ** 2
            )

            # Tránh chia cho 0
            if palm_size > 0:
                pinch_ratio = pinch_distance / palm_size
            else:
                pinch_ratio = 999

            # =========================
            # 7. Nhận diện gesture
            # =========================

            # Hai ngón gần nhau -> PINCH
            if pinch_ratio < 0.45:
                gesture = "PINCH"
                color = (0, 255, 0)
            else:
                gesture = "OPEN"
                color = (0, 0, 255)

            # =========================
            # 8. Vẽ hai đầu ngón
            # =========================
            cv2.circle(
                frame,
                (thumb_x, thumb_y),
                8,
                color,
                -1
            )

            cv2.circle(
                frame,
                (index_x, index_y),
                8,
                color,
                -1
            )

            # Vẽ đường nối ngón cái và trỏ
            cv2.line(
                frame,
                (thumb_x, thumb_y),
                (index_x, index_y),
                color,
                3
            )

            # =========================
            # 9. Xác định tay trái / phải
            # =========================
            hand_label = "Unknown"

            if results.multi_handedness:
                hand_label = (
                    results.multi_handedness[i]
                    .classification[0]
                    .label
                )

            # =========================
            # 10. Hiển thị trạng thái
            # =========================
            cv2.putText(
                frame,
                f"{hand_label} - {gesture}",
                (20, 60 + i * 40),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.8,
                color,
                2
            )

            cv2.putText(
                frame,
                f"Pinch ratio: {pinch_ratio:.2f}",
                (20, 90 + i * 40),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.6,
                color,
                2
            )

            # =========================
            # 11. Dữ liệu JSON
            # =========================
            hand_data = {
                "hand": hand_label,
                "gesture": gesture,

                "thumb_tip": {
                    "x": round(float(thumb_tip.x), 4),
                    "y": round(float(thumb_tip.y), 4),

                    "pixel_x": thumb_x,
                    "pixel_y": thumb_y
                },

                "index_tip": {
                    "x": round(float(index_tip.x), 4),
                    "y": round(float(index_tip.y), 4),

                    "pixel_x": index_x,
                    "pixel_y": index_y
                },

                "pinch_ratio": round(
                    float(pinch_ratio),
                    4
                )
            }

            hand_data_list.append(hand_data)

    # =========================
    # 12. Hướng dẫn phím
    # =========================
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
        "SmartGeoAR - Hand Tracking",
        frame
    )

    key = cv2.waitKey(1) & 0xFF

    # =========================
    # 13. Nhấn S -> lưu JSON
    # =========================
    if key == ord("s"):

        data = {
            "coordinate_system": "normalized_image",
            "hands": hand_data_list
        }

        json_path = OUTPUT_DIR / "hand_tracking.json"

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
            str(OUTPUT_DIR / "hand_tracking.png"),
            frame
        )

        print("Đã lưu:", json_path)
        print(
            json.dumps(
                data,
                indent=4,
                ensure_ascii=False
            )
        )

    # Nhấn Q -> thoát
    if key == ord("q"):
        break


# =========================
# 14. Dọn tài nguyên
# =========================
cap.release()
hands.close()
cv2.destroyAllWindows()