"""Bộ máy so sánh đồ thị cạnh phát hiện được với tri thức chuẩn."""

from knowledge_base import GeometryKnowledge, KNOWLEDGE_BASE
from schemas import CVDataInput, ErrorDetail, ReasoningResult


class GeometryReasoningEngine:
    def __init__(self, target_shape: str) -> None:
        if target_shape not in KNOWLEDGE_BASE:
            choices = ", ".join(KNOWLEDGE_BASE)
            raise ValueError(f"Hình '{target_shape}' không tồn tại. Hình hỗ trợ: {choices}.")
        self.knowledge = KNOWLEDGE_BASE[target_shape]

    def process(self, cv_input: CVDataInput) -> ReasoningResult:
        """Phân tích các cạnh CV phát hiện và trả kết quả có thể gửi Unity."""
        detected_vertices = {vertex.id for vertex in cv_input.vertices}
        expected_vertices = set(self.knowledge.expected_vertices)
        detected_edges = {
            GeometryKnowledge.normalize_edge(u, v)
            for u, v in cv_input.detected_edges
        }
        expected_edges = set(self.knowledge.expected_edges)

        # Sắp xếp để JSON đầu ra ổn định, thuận tiện khi kiểm thử/tích hợp.
        missing_vertices = sorted(expected_vertices - detected_vertices)
        extra_vertices = sorted(detected_vertices - expected_vertices)
        missing_edges = sorted(expected_edges - detected_edges)
        extra_edges = sorted(detected_edges - expected_edges)

        if not missing_vertices and not extra_vertices and not missing_edges and not extra_edges:
            return ReasoningResult(
                shape=self.knowledge.shape_name,
                status="valid",
                is_correct=True,
                message="Chúc mừng! Bạn đã lắp mô hình hoàn toàn chính xác.",
                details=ErrorDetail(),
            )

        # Ưu tiên báo thiếu đỉnh: camera chưa quan sát đủ mô hình.
        if missing_vertices:
            return ReasoningResult(
                shape=self.knowledge.shape_name,
                status="incomplete",
                is_correct=False,
                error_type="missing_vertex",
                message=(
                    f"Mô hình chưa hoàn thành! Thiếu {len(missing_vertices)} đỉnh: "
                    f"{', '.join(missing_vertices)}."
                ),
                details=ErrorDetail(
                    missing_vertices=missing_vertices,
                    extra_vertices=extra_vertices,
                    missing_edges=missing_edges,
                    extra_edges=extra_edges,
                    wrong_connections=extra_edges,
                ),
            )

        # Có đồng thời cạnh thiếu và cạnh thừa nghĩa là một hay nhiều cạnh nối sai.
        if missing_edges and (extra_vertices or extra_edges):
            return ReasoningResult(
                shape=self.knowledge.shape_name,
                status="invalid",
                is_correct=False,
                error_type="wrong_connection",
                message=(
                    f"Mô hình nối sai! Thiếu {len(missing_edges)} cạnh và "
                    f"thừa {len(extra_edges)} cạnh."
                ),
                details=ErrorDetail(
                    extra_vertices=extra_vertices,
                    missing_edges=missing_edges,
                    extra_edges=extra_edges,
                    wrong_connections=extra_edges,
                ),
            )

        if missing_edges:
            return ReasoningResult(
                shape=self.knowledge.shape_name,
                status="incomplete",
                is_correct=False,
                error_type="missing_edge",
                message=f"Mô hình chưa hoàn thành! Bạn đang thiếu {len(missing_edges)} cạnh.",
                details=ErrorDetail(missing_edges=missing_edges),
            )

        if extra_vertices or extra_edges:
            return ReasoningResult(
                shape=self.knowledge.shape_name,
                status="invalid",
                is_correct=False,
                error_type="extra_vertex" if extra_vertices else "extra_edge",
                message=(
                    f"Mô hình bị lỗi! Thừa {len(extra_vertices)} đỉnh và "
                    f"{len(extra_edges)} cạnh không phù hợp."
                ),
                details=ErrorDetail(
                    extra_vertices=extra_vertices,
                    extra_edges=extra_edges,
                    wrong_connections=extra_edges,
                ),
            )
