import time

import cv2
import mediapipe as mp
import numpy as np

from hand_pose import HandPoseEstimator
from role1_week2_stream import (
    MAX_HANDS,
    get_gesture,
    get_state,
    hand_states,
    handle_missing_hands,
    make_hand_data,
    smooth_point,
)


class PhoneHandTracking:

    def __init__(self):
        # A TCP connection owns its gesture state, as with the webcam stream.
        hand_states.clear()
        self.tracker = mp.solutions.hands.Hands(
            static_image_mode=False,
            max_num_hands=MAX_HANDS,
            min_detection_confidence=0.6,
            min_tracking_confidence=0.6
        )
        self.pose = HandPoseEstimator()
        self.previous_centers = {}

    @staticmethod
    def pinch_ratio(points, width, height):
        def distance(a, b):
            return float(np.hypot((a.x - b.x) * width, (a.y - b.y) * height))

        # Normalized x/y have different scales in a wide phone image.
        # Keep the existing pinch thresholds/hysteresis in square pixels.
        palm = distance(points[0], points[9])
        return distance(points[4], points[8]) / palm if palm > 1e-6 else 999.0

    def hand_name(self, label, center, visible, now):
        # Handedness can flip when a hand turns. Keep its gesture/owner ID
        # by matching palm position instead of changing Left/Right each frame.
        candidates = [
            (float(np.linalg.norm(center - previous)), name)
            for name, (previous, seen_at) in self.previous_centers.items()
            if name not in visible and now - seen_at < 0.4
        ]
        distance, name = min(candidates, default=(float("inf"), label))
        if distance > 0.25:
            name = label
            suffix = 2
            while name in visible:
                name = label + "_" + str(suffix)
                suffix += 1
        self.previous_centers[name] = (center, now)
        return name

    def process(self, frame, frame_id, intrinsics):
        height, width = frame.shape[:2]
        result = self.tracker.process(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
        detected = []
        visible = set()
        now = time.monotonic()
        self.previous_centers = {
            name: previous
            for name, previous in self.previous_centers.items()
            if now - previous[1] < 0.4
        }

        for index, landmarks in enumerate(result.multi_hand_landmarks or []):
            name = "Unknown"

            if result.multi_handedness and index < len(result.multi_handedness):
                name = result.multi_handedness[index].classification[0].label
                # MediaPipe's handedness assumes a selfie/mirrored image.
                name = {"Left": "Right", "Right": "Left"}.get(name, name)

            points = landmarks.landmark
            center = np.mean([
                (points[index].x, points[index].y)
                for index in (0, 5, 9, 13, 17)
            ], axis=0)
            name = self.hand_name(name, center, visible, now)

            visible.add(name)
            state = get_state(name)
            state["lost"] = 0
            thumb, finger = points[4], points[8]
            ratio = self.pinch_ratio(points, width, height)
            gesture = get_gesture(state, ratio)
            x, y = smooth_point(
                state,
                float(np.clip((thumb.x + finger.x) * 0.5, 0, 1)),
                float(np.clip((thumb.y + finger.y) * 0.5, 0, 1))
            )
            state["last_pos"] = (x, y)
            state["last_ratio"] = ratio

            world = None

            if result.multi_hand_world_landmarks and index < len(result.multi_hand_world_landmarks):
                world = result.multi_hand_world_landmarks[index]

            spatial = self.pose.estimate(
                name, landmarks, world, width, height,
                intrinsics=intrinsics,
                coordinate_system="phone_camera_m",
                # Each phone frame has a different AR camera pose. Mixing
                # coordinates from older camera frames would shift the hand.
                smooth=False
            )
            spatial["camera_frame_id"] = frame_id
            detected.append(make_hand_data(name, gesture, x, y, ratio, spatial))

        self.pose.forget_missing(visible)
        handle_missing_hands(visible, detected, frame_id)

        for hand in detected:
            if "spatial" not in hand:
                hand["spatial"] = {"valid": False, "camera_frame_id": frame_id}

        return detected

    def close(self):
        self.tracker.close()
