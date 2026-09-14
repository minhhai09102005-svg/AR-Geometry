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
        detected_edges = {
            GeometryKnowledge.normalize_edge(u, v)
            for u, v in cv_input.detected_edges
        }
        expected_edges = set(self.knowledge.expected_edges)

        # Sắp xếp để JSON đầu ra ổn định, thuận tiện khi kiểm thử/tích hợp.
        missing_edges = sorted(expected_edges - detected_edges)
        extra_edges = sorted(detected_edges - expected_edges)

        if not missing_edges and not extra_edges:
            return ReasoningResult(
                shape=self.knowledge.shape_name,
                status="valid",
                is_correct=True,
                message="Chúc mừng! Bạn đã lắp mô hình hoàn toàn chính xác.",
                details=ErrorDetail(),
            )

        if missing_edges and not extra_edges:
            return ReasoningResult(
                shape=self.knowledge.shape_name,
                status="incomplete",
                is_correct=False,
                error_type="missing_edge",
                message=f"Mô hình chưa hoàn thành! Bạn đang thiếu {len(missing_edges)} cạnh.",
                details=ErrorDetail(missing_edges=missing_edges),
            )

        if extra_edges and not missing_edges:
            return ReasoningResult(
                shape=self.knowledge.shape_name,
                status="invalid",
                is_correct=False,
                error_type="extra_edge",
                message=f"Mô hình bị lỗi! Bạn đang lắp thừa {len(extra_edges)} cạnh không phù hợp.",
                details=ErrorDetail(extra_edges=extra_edges),
            )

        return ReasoningResult(
            shape=self.knowledge.shape_name,
            status="invalid",
            is_correct=False,
            error_type="wrong_connection",
            message=(
                f"Mô hình nối sai! Thiếu {len(missing_edges)} cạnh "
                f"và thừa {len(extra_edges)} cạnh."
            ),
            details=ErrorDetail(
                missing_edges=missing_edges,
                extra_edges=extra_edges,
                wrong_connections=extra_edges,
            ),
        )
