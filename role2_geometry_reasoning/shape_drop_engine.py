
"""Role 2 - Geometry reasoning cho trò chơi THẢ HÌNH."""

import math
from typing import Any, Dict


class ShapeDropEngine:
    """
    Chỉ Role 2 quyết định đúng/sai.

    SPECS:
      ID hình: (loại hình, kích thước khối,
                kích thước lỗ, chu kỳ đối xứng)

    Các kích thước là đơn vị mô phỏng, không phải cm.
    """

    SPECS = {
        "square": (
            "square", (1.0, 1.0), (1.15, 1.15), 90
        ),
        "triangle": (
            "triangle", (1.0, 1.0), (1.15, 1.15), 360
        ),
        "rectangle": (
            "rectangle", (1.4, 0.75), (1.56, 0.86), 180
        ),
        "circle": (
            "circle", (1.0, 1.0), (1.15, 1.15), 0
        ),
    }

    def __init__(self):
        self.session_id = None
        self.completed = set()
        self.results = {}

    @staticmethod
    def _number(value: Any) -> float:
        number = float(value)

        if not math.isfinite(number):
            raise ValueError("Giá trị không hợp lệ")

        return number

    def evaluate(
        self,
        packet: Dict[str, Any]
    ) -> Dict[str, Any]:

        request_id = int(packet.get("request_id", -1))
        session_id = str(packet.get("session_id", ""))
        piece_id = str(packet.get("piece_id", ""))
        hole_id = str(packet.get("hole_id", ""))

        def response(ok, error_type, message):
            return {
                "source": "role2_geometry_reasoning",
                "type": "shape_drop_result",
                "session_id": session_id,
                "request_id": request_id,
                "piece_id": piece_id,
                "hole_id": hole_id,
                "is_correct": ok,
                "error_type": error_type,
                "message": message,
                "completed_count": len(self.completed),
                "total_count": len(self.SPECS)
            }

        if (
            not session_id
            or len(session_id) > 100
            or request_id < 0
        ):
            return response(
                False,
                "invalid_request",
                "Yêu cầu không hợp lệ"
            )

        # Khi bắt đầu ván mới, xóa tiến độ ván cũ.
        if self.session_id != session_id:
            self.session_id = session_id
            self.completed.clear()
            self.results.clear()

        # Tránh một yêu cầu được xử lý hai lần.
        if request_id in self.results:
            return dict(self.results[request_id])

        if (
            piece_id not in self.SPECS
            or hole_id not in self.SPECS
        ):
            result = response(
                False,
                "unknown_shape",
                "Không tìm thấy khối hoặc lỗ"
            )

        elif piece_id in self.completed:
            result = response(
                False,
                "already_placed",
                "Khối này đã được đặt rồi"
            )

        elif hole_id in self.completed:
            result = response(
                False,
                "occupied",
                "Lỗ này đã có khối"
            )

        elif piece_id != hole_id:
            result = response(
                False,
                "wrong_shape",
                "Sai hình dạng, hãy thử lỗ khác"
            )

        else:
            try:
                spec = self.SPECS[piece_id]

                pw, ph = spec[1]
                hw, hh = spec[2]

                rotation = (
                    self._number(
                        packet.get("rotation_deg", 0)
                    ) % 360
                )

                offset_x = self._number(
                    packet["offset_x"]
                )

                offset_y = self._number(
                    packet["offset_y"]
                )

                # Kiểm tra góc xoay theo tính đối xứng.
                symmetry = spec[3]

                if symmetry:
                    deviation = abs(
                        (rotation + symmetry / 2)
                        % symmetry
                        - symmetry / 2
                    )
                else:
                    deviation = 0.0

                if deviation > 12.0:
                    result = response(
                        False,
                        "wrong_rotation",
                        "Cần xoay khối cho đúng hướng"
                    )

                # Tọa độ đã chuẩn hóa theo kích thước lỗ.
                elif (
                    abs(offset_x) > 0.22
                    or abs(offset_y) > 0.22
                ):
                    result = response(
                        False,
                        "wrong_position",
                        "Cần đưa khối tới gần tâm lỗ"
                    )

                else:
                    theta = math.radians(rotation)

                    projected_width = (
                        abs(pw * math.cos(theta))
                        + abs(ph * math.sin(theta))
                    )

                    projected_height = (
                        abs(pw * math.sin(theta))
                        + abs(ph * math.cos(theta))
                    )

                    if (
                        projected_width > hw + 1e-8
                        or projected_height > hh + 1e-8
                    ):
                        result = response(
                            False,
                            "size_mismatch",
                            "Kích thước khối không vừa lỗ"
                        )
                    else:
                        self.completed.add(piece_id)

                        result = response(
                            True,
                            None,
                            "Chính xác! Khối đã vào đúng lỗ"
                        )

            except (
                KeyError,
                TypeError,
                ValueError,
                OverflowError
            ):
                result = response(
                    False,
                    "invalid_geometry",
                    "Dữ liệu hình học không hợp lệ"
                )

        self.results[request_id] = result

        # Giới hạn bộ nhớ cache.
        if len(self.results) > 256:
            first_key = next(iter(self.results))
            self.results.pop(first_key)

        return dict(result)
