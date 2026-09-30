import cv2
import mediapipe as mp
import math
import json
import socket


# ============================================================
# SMARTGEOAR - ROLE 1 - WEEK 2
#
# Camera
# -> MediaPipe Hands
# -> Gesture
# -> Interaction Point
# -> Smoothing
# -> JSON
# -> UDP
# -> Unity
# ============================================================


# ============================================================
# 1. CAMERA CONFIG
# ============================================================

CAMERA_INDEX = 0

MAX_HANDS = 2


# ============================================================
# 2. GESTURE CONFIG
# ============================================================

# Khi ratio nhỏ hơn mức này
# -> bắt đầu pinch
PINCH_START_THRESHOLD = 0.40


# Khi đang pinch mà ratio lớn hơn mức này
# -> kết thúc pinch
PINCH_END_THRESHOLD = 0.55


# ============================================================
# 3. SMOOTHING CONFIG
# ============================================================

# 0.15 -> rất mượt nhưng hơi chậm
# 0.30 -> cân bằng
# 0.50 -> nhanh nhưng rung hơn

SMOOTHING_ALPHA = 0.30


# ============================================================
# 4. UDP CONFIG
# ============================================================

# Python + Unity chạy cùng laptop
UDP_HOST = "172.11.203.7"

# Unity sẽ nghe port này
UDP_PORT = 5052


# ============================================================
# 5. DEBUG CONFIG
# ============================================================

# In JSON sau mỗi bao nhiêu frame
# Camera khoảng 30 FPS
# 30 = khoảng 1 giây in 1 lần

PRINT_JSON_EVERY_N_FRAMES = 30


# ============================================================
# 6. MEDIAPIPE
# ============================================================

mp_hands = mp.solutions.hands

mp_drawing = mp.solutions.drawing_utils

mp_drawing_styles = mp.solutions.drawing_styles


hands = mp_hands.Hands(

    static_image_mode=False,

    max_num_hands=MAX_HANDS,

    min_detection_confidence=0.6,

    min_tracking_confidence=0.6
)


# ============================================================
# 7. STATE
# ============================================================

# trạng thái pinch từng tay

pinch_state = {

    "Left": False,

    "Right": False
}


# lưu vị trí đã smoothing

smooth_position = {

    "Left": None,

    "Right": None
}


frame_id = 0


# ============================================================
# 8. HÀM TÍNH KHOẢNG CÁCH
# ============================================================

def distance_2d(point1, point2):

    return math.sqrt(

        (point1.x - point2.x) ** 2

        +

        (point1.y - point2.y) ** 2
    )


# ============================================================
# 9. HÀM XÁC ĐỊNH GESTURE
# ============================================================

def get_gesture(hand_name, pinch_ratio):

    # Lấy trạng thái trước đó

    was_pinching = pinch_state.get(

        hand_name,

        False
    )


    # ========================================================
    # TRƯỜNG HỢP 1
    # Trước đó chưa pinch
    # ========================================================

    if not was_pinching:


        # Hai ngón đủ gần nhau
        # -> bắt đầu pinch

        if pinch_ratio < PINCH_START_THRESHOLD:

            pinch_state[hand_name] = True

            return "PINCH_START"


        # Chưa pinch

        return "OPEN"


    # ========================================================
    # TRƯỜNG HỢP 2
    # Trước đó đang pinch
    # ========================================================

    else:


        # Hai ngón đã tách ra đủ xa
        # -> kết thúc pinch

        if pinch_ratio > PINCH_END_THRESHOLD:

            pinch_state[hand_name] = False

            return "PINCH_END"


        # Vẫn đang giữ pinch

        return "PINCH_HOLD"


# ============================================================
# 10. HÀM SMOOTH TỌA ĐỘ
# ============================================================

def smooth_point(hand_name, current_x, current_y):

    previous = smooth_position.get(

        hand_name
    )


    # ========================================================
    # Frame đầu tiên
    # ========================================================

    if previous is None:

        smooth_x = current_x

        smooth_y = current_y


    # ========================================================
    # Các frame tiếp theo
    # ========================================================

    else:

        smooth_x = (

            previous["x"]

            *

            (1 - SMOOTHING_ALPHA)

            +

            current_x

            *

            SMOOTHING_ALPHA
        )


        smooth_y = (

            previous["y"]

            *

            (1 - SMOOTHING_ALPHA)

            +

            current_y

            *

            SMOOTHING_ALPHA
        )


    # Lưu lại

    smooth_position[hand_name] = {

        "x": smooth_x,

        "y": smooth_y
    }


    return smooth_x, smooth_y


# ============================================================
# 11. TẠO UDP SOCKET
# ============================================================

udp_socket = socket.socket(

    socket.AF_INET,

    socket.SOCK_DGRAM
)


# ============================================================
# 12. MỞ CAMERA
# ============================================================

cap = cv2.VideoCapture(

    CAMERA_INDEX,

    cv2.CAP_DSHOW
)


if not cap.isOpened():

    print("ERROR: Không mở được camera")

    raise SystemExit


print()

print("============================================")

print("SMARTGEOAR - ROLE 1 - WEEK 2")

print("============================================")

print()

print(
    f"UDP destination: {UDP_HOST}:{UDP_PORT}"
)

print()

print(
    "Q = Quit"
)

print()

print("============================================")

print()


# ============================================================
# 13. MAIN LOOP
# ============================================================

while True:


    # ========================================================
    # 14. ĐỌC CAMERA
    # ========================================================

    ret, frame = cap.read()


    if not ret:

        print("ERROR: Không đọc được frame")

        break


    # Flip camera dạng gương

    frame = cv2.flip(

        frame,

        1
    )


    height, width, _ = frame.shape


    # ========================================================
    # 15. BGR -> RGB
    # ========================================================

    rgb = cv2.cvtColor(

        frame,

        cv2.COLOR_BGR2RGB
    )


    # ========================================================
    # 16. HAND TRACKING
    # ========================================================

    results = hands.process(

        rgb
    )


    # Danh sách tay trong frame này

    detected_hands = []


    # ========================================================
    # 17. NẾU CÓ TAY
    # ========================================================

    if results.multi_hand_landmarks:


        for i, hand_landmarks in enumerate(

            results.multi_hand_landmarks
        ):


            # =================================================
            # 18. XÁC ĐỊNH LEFT / RIGHT
            # =================================================

            if results.multi_handedness:


                classification = (

                    results

                    .multi_handedness[i]

                    .classification[0]
                )


                hand_name = classification.label


            else:

                hand_name = "Unknown"


            # =================================================
            # 19. LẤY LANDMARK
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
            # 20. TÍNH KHOẢNG CÁCH NGÓN CÁI - NGÓN TRỎ
            # =================================================

            finger_distance = distance_2d(

                thumb_tip,

                index_tip
            )


            # =================================================
            # 21. TÍNH KÍCH THƯỚC BÀN TAY
            # =================================================

            palm_size = distance_2d(

                wrist,

                middle_mcp
            )


            # =================================================
            # 22. PINCH RATIO
            # =================================================

            if palm_size > 0:

                pinch_ratio = (

                    finger_distance

                    /

                    palm_size
                )


            else:

                pinch_ratio = 999


            # =================================================
            # 23. GESTURE
            # =================================================

            gesture = get_gesture(

                hand_name,

                pinch_ratio
            )


            # =================================================
            # 24. INTERACTION POINT
            #
            # trung điểm:
            #
            # thumb_tip + index_tip
            # =================================================

            raw_x = (

                thumb_tip.x

                +

                index_tip.x

            ) / 2


            raw_y = (

                thumb_tip.y

                +

                index_tip.y

            ) / 2


            # =================================================
            # 25. GIỚI HẠN TỌA ĐỘ 0 -> 1
            # =================================================

            raw_x = max(

                0.0,

                min(

                    1.0,

                    raw_x
                )
            )


            raw_y = max(

                0.0,

                min(

                    1.0,

                    raw_y
                )
            )


            # =================================================
            # 26. SMOOTH TỌA ĐỘ
            # =================================================

            interaction_x, interaction_y = smooth_point(

                hand_name,

                raw_x,

                raw_y
            )


            # =================================================
            # 27. CHUYỂN TỌA ĐỘ NORMALIZED -> PIXEL
            #
            # chỉ dùng để vẽ debug
            # =================================================

            pixel_x = int(

                interaction_x

                *

                width
            )


            pixel_y = int(

                interaction_y

                *

                height
            )


            # =================================================
            # 28. TẠO DATA CỦA 1 TAY
            # =================================================

            hand_data = {


                "hand":

                    hand_name,


                "gesture":

                    gesture,


                "interaction": {


                    "x":

                        round(

                            float(interaction_x),

                            4
                        ),


                    "y":

                        round(

                            float(interaction_y),

                            4
                        )
                },


                "pinch_ratio":

                    round(

                        float(pinch_ratio),

                        4
                    )
            }


            # Thêm vào danh sách

            detected_hands.append(

                hand_data
            )


            # =================================================
            # 29. VẼ LANDMARK
            # =================================================

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
            # 30. CHỌN MÀU GESTURE
            # =================================================

            if gesture == "PINCH_START":

                color = (

                    0,

                    255,

                    0
                )


            elif gesture == "PINCH_HOLD":

                color = (

                    0,

                    255,

                    255
                )


            elif gesture == "PINCH_END":

                color = (

                    255,

                    0,

                    255
                )


            else:

                color = (

                    0,

                    0,

                    255
                )


            # =================================================
            # 31. VẼ INTERACTION POINT
            # =================================================

            cv2.circle(

                frame,

                (
                    pixel_x,

                    pixel_y
                ),

                12,

                color,

                3
            )


            # =================================================
            # 32. HIỂN THỊ GESTURE
            # =================================================

            cv2.putText(

                frame,

                f"{hand_name}: {gesture}",

                (
                    20,

                    40 + i * 40
                ),

                cv2.FONT_HERSHEY_SIMPLEX,

                0.7,

                color,

                2
            )


            # =================================================
            # 33. HIỂN THỊ PINCH RATIO
            # =================================================

            cv2.putText(

                frame,

                f"ratio: {pinch_ratio:.2f}",

                (
                    20,

                    65 + i * 40
                ),

                cv2.FONT_HERSHEY_SIMPLEX,

                0.45,

                color,

                1
            )


    # ========================================================
    # 34. TẠO PACKET JSON
    # ========================================================

    packet = {


        "version":

            "1.0",


        "source":

            "role1_hand_tracking",


        "frame_id":

            frame_id,


        "coordinate_system":

            "normalized_image",


        "hands":

            detected_hands
    }


    # ========================================================
    # 35. IN JSON RA TERMINAL
    #
    # khoảng 1 giây / lần
    # ========================================================

    if frame_id % PRINT_JSON_EVERY_N_FRAMES == 0:


        print(

            json.dumps(

                packet,

                indent=4,

                ensure_ascii=False
            )
        )


        print(

            "--------------------------------------------"
        )


    # ========================================================
    # 36. JSON -> BYTES
    # ========================================================

    message = json.dumps(

        packet,

        ensure_ascii=False

    ).encode(

        "utf-8"
    )


    # ========================================================
    # 37. SEND UDP
    # ========================================================

    try:


        udp_socket.sendto(

            message,

            (

                UDP_HOST,

                UDP_PORT
            )
        )


    except OSError as error:


        print(

            "UDP ERROR:",

            error
        )


    # ========================================================
    # 38. HIỂN THỊ UDP STATUS
    # ========================================================

    cv2.putText(

        frame,

        f"UDP -> {UDP_HOST}:{UDP_PORT}",

        (

            20,

            height - 45
        ),

        cv2.FONT_HERSHEY_SIMPLEX,

        0.5,

        (

            255,

            255,

            255
        ),

        1
    )


    # ========================================================
    # 39. HƯỚNG DẪN QUIT
    # ========================================================

    cv2.putText(

        frame,

        "Q: Quit",

        (

            20,

            height - 20
        ),

        cv2.FONT_HERSHEY_SIMPLEX,

        0.5,

        (

            255,

            255,

            255
        ),

        1
    )


    # ========================================================
    # 40. SHOW CAMERA
    # ========================================================

    cv2.imshow(

        "SmartGeoAR - Role 1 Week 2",

        frame
    )


    # ========================================================
    # 41. FRAME ID
    # ========================================================

    frame_id += 1


    # ========================================================
    # 42. KEYBOARD
    # ========================================================

    key = cv2.waitKey(1) & 0xFF


    if key == ord("q"):

        break


# ============================================================
# 43. CLEANUP
# ============================================================

cap.release()

hands.close()

udp_socket.close()

cv2.destroyAllWindows()