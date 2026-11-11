import math

import cv2
import numpy as np


# =========================================================
# CAMERA-RELATIVE HAND POSE
#
# MediaPipe world landmarks are relative to the hand, not to
# the room. PnP estimates camera-relative translation as well.
# The default FOV is approximate; Unity still needs calibration.
# =========================================================

class HandPoseEstimator:

    def __init__(self, horizontal_fov=60.0):
        self.horizontal_fov = horizontal_fov
        self.previous = {}

    def estimate(
        self,
        hand_name,
        image_landmarks,
        world_landmarks,
        width,
        height,
        intrinsics=None,
        coordinate_system="mirrored_camera_m",
        smooth=True
    ):

        invalid = {"valid": False}

        if world_landmarks is None or width <= 0 or height <= 0:
            return invalid

        objects = np.array([
            [point.x, point.y, point.z]
            for point in world_landmarks.landmark
        ], dtype=np.float64)

        image = np.array([
            [point.x * width, point.y * height]
            for point in image_landmarks.landmark
        ], dtype=np.float64)

        if (
            objects.shape != (21, 3)
            or image.shape != (21, 2)
            or not np.isfinite(objects).all()
            or not np.isfinite(image).all()
        ):
            return invalid

        focal = width / (
            2.0 * math.tan(math.radians(self.horizontal_fov) * 0.5)
        )

        camera = np.array([
            [focal, 0.0, width * 0.5],
            [0.0, focal, height * 0.5],
            [0.0, 0.0, 1.0]
        ], dtype=np.float64)

        if intrinsics is not None:
            try:
                fx, fy, cx, cy = [
                    float(intrinsics[key])
                    for key in ("fx", "fy", "cx", "cy")
                ]
            except (KeyError, TypeError, ValueError):
                return invalid

            if not all(math.isfinite(value) for value in (fx, fy, cx, cy)):
                return invalid

            if fx <= 0 or fy <= 0:
                return invalid

            camera = np.array([
                [fx, 0.0, cx],
                [0.0, fy, cy],
                [0.0, 0.0, 1.0]
            ], dtype=np.float64)

        try:
            ok, rotation, translation = cv2.solvePnP(
                objects,
                image,
                camera,
                None,
                flags=cv2.SOLVEPNP_SQPNP
            )

            if not ok:
                return invalid

            projected, _ = cv2.projectPoints(
                objects, rotation, translation, camera, None
            )

            error = float(np.mean(np.linalg.norm(
                projected.reshape(-1, 2) - image,
                axis=1
            )))

            matrix, _ = cv2.Rodrigues(rotation)
            points = objects @ matrix.T + translation.reshape(1, 3)

        except cv2.error:
            return invalid

        if (
            not np.isfinite(points).all()
            or not math.isfinite(error)
            or error > width * 0.035
            or np.min(points[:, 2]) < 0.12
            or np.max(points[:, 2]) > 2.5
        ):
            return invalid

        # Keep phone fingertips on the measured image rays. PnP supplies
        # depth; its reprojection residual must not move the cursor away
        # from the real finger in the AR camera image.
        if coordinate_system == "phone_camera_m":
            points[:, 0] = (image[:, 0] - camera[0, 2]) * points[:, 2] / camera[0, 0]
            points[:, 1] = (camera[1, 2] - image[:, 1]) * points[:, 2] / camera[1, 1]
        else:
            # x right, y up, z forward in the supplied camera image.
            points[:, 1] *= -1.0

        tips = np.array([
            (points[4] + points[8]) * 0.5,
            points[8]
        ])

        previous = self.previous.get(hand_name)

        if previous is not None and smooth:
            # A large jump is not a valid release/placement position.
            if np.linalg.norm(tips[0] - previous[0]) > 0.30:
                self.previous.pop(hand_name, None)
                return invalid

            tips = previous * 0.55 + tips * 0.45

        self.previous[hand_name] = tips

        def point_json(point):
            return {
                "x": round(float(point[0]), 5),
                "y": round(float(point[1]), 5),
                "z": round(float(point[2]), 5)
            }

        result = {
            "valid": True,
            "coordinate_system": coordinate_system,
            "estimated": True,
            "point": point_json(tips[0]),
            "index_tip": point_json(tips[1])
        }

        # The phone uses the same estimated hand depth for interaction and
        # its depth-only hand silhouette. Legacy webcam packets stay small.
        if coordinate_system == "phone_camera_m":
            result["landmarks"] = [point_json(point) for point in points]

        return result

    def forget_missing(self, visible_hands):

        for hand_name in list(self.previous):
            if hand_name not in visible_hands:
                self.previous.pop(hand_name, None)
