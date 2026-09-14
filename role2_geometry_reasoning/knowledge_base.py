"""Tri thức chuẩn cho các mô hình hình học được hỗ trợ."""

from dataclasses import dataclass
from typing import Dict, List, Tuple


@dataclass(frozen=True)
class GeometryKnowledge:
    """Mô tả tô-pô chuẩn của một hình học không hướng."""

    shape_name: str
    expected_vertices: List[str]
    expected_edges: List[Tuple[str, str]]
    expected_degrees: Dict[str, int]

    @staticmethod
    def normalize_edge(u: str, v: str) -> Tuple[str, str]:
        """Chuẩn hóa cạnh vô hướng để ``(A, B)`` và ``(B, A)`` là một."""
        return (u, v) if u < v else (v, u)


# ==============================================================================
# 1. HÌNH LẬP PHƯƠNG (CUBE)
# ==============================================================================

# Danh sách 12 cạnh thô của Hình lập phương:
# - Đáy dưới: A-B, B-C, C-D, D-A
# - Đáy trên: E-F, F-G, G-H, H-E
# - Các cạnh bên: A-E, B-F, C-G, D-H
CUBE_EDGES_RAW = [
    ("A", "B"), ("B", "C"), ("C", "D"), ("D", "A"),
    ("E", "F"), ("F", "G"), ("G", "H"), ("H", "E"),
    ("A", "E"), ("B", "F"), ("C", "G"), ("D", "H"),
]

# Tri thức chuẩn cho Hình lập phương:
# - Số đỉnh: 8 đỉnh (A, B, C, D, E, F, G, H)
# - Số cạnh: 12 cạnh (đã qua chuẩn hóa)
# - Bậc đỉnh: Tất cả 8 đỉnh đều có đúng 3 cạnh nối vào (bậc = 3)
CUBE_KNOWLEDGE = GeometryKnowledge(
    shape_name="cube",
    expected_vertices=list("ABCDEFGH"),
    expected_edges=[GeometryKnowledge.normalize_edge(u, v) for u, v in CUBE_EDGES_RAW],
    expected_degrees={vertex: 3 for vertex in "ABCDEFGH"},
)


# ==============================================================================
# 2. HÌNH HỘP CHỮ NHẬT (RECTANGULAR PRISM)
# ==============================================================================

# Tri thức chuẩn cho Hình hộp chữ nhật:
# Lưu ý: Về mặt đồ thị (topology/cấu trúc kết nối), hình hộp chữ nhật có cùng
# số đỉnh, số cạnh và bậc đỉnh hoàn toàn giống với hình lập phương.
PRISM_KNOWLEDGE = GeometryKnowledge(
    shape_name="rectangular_prism",
    expected_vertices=list("ABCDEFGH"),
    expected_edges=[GeometryKnowledge.normalize_edge(u, v) for u, v in CUBE_EDGES_RAW],
    expected_degrees={vertex: 3 for vertex in "ABCDEFGH"},
)


# ==============================================================================
# 3. HÌNH CHÓP TỨ GIÁC ĐỀU (SQUARE PYRAMID)
# ==============================================================================

# Danh sách 8 cạnh thô của Hình chóp tứ giác đều:
# - 4 cạnh đáy: A-B, B-C, C-D, D-A
# - 4 cạnh bên nối từ đỉnh chóp S xuống đáy: S-A, S-B, S-C, S-D
PYRAMID_EDGES_RAW = [
    ("A", "B"), ("B", "C"), ("C", "D"), ("D", "A"),
    ("S", "A"), ("S", "B"), ("S", "C"), ("S", "D"),
]

# Tri thức chuẩn cho Hình chóp tứ giác đều:
# - Số đỉnh: 5 đỉnh (A, B, C, D và đỉnh chóp S)
# - Số cạnh: 8 cạnh (đã qua chuẩn hóa)
# - Bậc đỉnh: 4 đỉnh đáy (A, B, C, D) có bậc = 3; Đỉnh chóp S nối với 4 đỉnh đáy nên có bậc = 4
PYRAMID_KNOWLEDGE = GeometryKnowledge(
    shape_name="square_pyramid",
    expected_vertices=["A", "B", "C", "D", "S"],
    expected_edges=[GeometryKnowledge.normalize_edge(u, v) for u, v in PYRAMID_EDGES_RAW],
    expected_degrees={"A": 3, "B": 3, "C": 3, "D": 3, "S": 4},
)


# ==============================================================================
# TỪ ĐIỂN TRI THỨC CHUNG (KNOWLEDGE BASE DICTIONARY)
# ==============================================================================
# Tập hợp tất cả các mô hình hình học chuẩn để bộ suy luận tra cứu nhanh theo tên hình
KNOWLEDGE_BASE: Dict[str, GeometryKnowledge] = {
    "cube": CUBE_KNOWLEDGE,                  # Tra cứu Hình lập phương
    "rectangular_prism": PRISM_KNOWLEDGE,    # Tra cứu Hình hộp chữ nhật
    "square_pyramid": PYRAMID_KNOWLEDGE,     # Tra cứu Hình chóp tứ giác đều
}