"""Tri thức chuẩn cho các mô hình hình học được hỗ trợ"""

from dataclasses import dataclass, field
from typing import Dict, List, Set, Tuple


@dataclass(frozen=True)
class GeometryKnowledge:
    """Mô tả tô-pô và đặc trưng hình học 3D chuẩn của một mô hình không hướng."""

    # --- Các thuộc tính gốc (giữ nguyên tương thích 100%) ---
    shape_name: str
    expected_vertices: List[str]
    expected_edges: List[Tuple[str, str]]
    expected_degrees: Dict[str, int]

    # --- Các thuộc tính mở rộng cho Tuần 4 -> Tuần 8 ---
    display_name: str = ""
    expected_faces: List[Tuple[str, ...]] = field(default_factory=list)
    edge_groups: Dict[str, List[Tuple[str, str]]] = field(default_factory=dict)
    orthogonal_corners: List[Tuple[str, str, str]] = field(default_factory=list)
    face_diagonals: List[Tuple[str, str]] = field(default_factory=list)
    space_diagonals: List[Tuple[str, str]] = field(default_factory=list)
    geometry_rules: Dict[str, float | bool] = field(default_factory=dict)
    formulas: Dict[str, str] = field(default_factory=dict)

    @staticmethod
    def normalize_edge(u: str, v: str) -> Tuple[str, str]:
        """Chuẩn hóa cạnh vô hướng để ``(A, B)`` và ``(B, A)`` là một."""
        u_clean, v_clean = str(u).strip().upper(), str(v).strip().upper()
        return (u_clean, v_clean) if u_clean < v_clean else (v_clean, u_clean)

    @classmethod
    def normalize_edge_list(cls, edges: List[Tuple[str, str]]) -> List[Tuple[str, str]]:
        """Chuẩn hóa và loại bỏ cạnh trùng lặp, giữ thứ tự ổn định."""
        seen: Set[Tuple[str, str]] = set()
        result: List[Tuple[str, str]] = []
        for u, v in edges:
            u_c, v_c = str(u).strip().upper(), str(v).strip().upper()
            if u_c == v_c:
                continue
            norm = cls.normalize_edge(u_c, v_c)
            if norm not in seen:
                seen.add(norm)
                result.append(norm)
        return result

    @property
    def num_vertices(self) -> int:
        """Số lượng đỉnh chuẩn của hình."""
        return len(self.expected_vertices)

    @property
    def num_edges(self) -> int:
        """Số lượng cạnh chuẩn của hình."""
        return len(self.expected_edges)

    @property
    def num_faces(self) -> int:
        """Số lượng mặt chuẩn của hình."""
        return len(self.expected_faces)

    @property
    def expected_vertex_set(self) -> Set[str]:
        """Tập hợp các đỉnh chuẩn phục vụ phép toán tập hợp nhanh."""
        return set(self.expected_vertices)

    @property
    def expected_edge_set(self) -> Set[Tuple[str, str]]:
        """Tập hợp các cạnh chuẩn (đã normalize) phục vụ tính missing/extra edges."""
        return set(self.expected_edges)

    def get_adjacency_map(self) -> Dict[str, Set[str]]:
        """Trả về danh sách kề chuẩn G=(V,E) của mô hình."""
        adj: Dict[str, Set[str]] = {v: set() for v in self.expected_vertices}
        for u, v in self.expected_edges:
            adj.setdefault(u, set()).add(v)
            adj.setdefault(v, set()).add(u)
        return adj


# ==============================================================================
# DỮ LIỆU CẤU TRÚC DÙNG CHUNG CHO KHỐI 6 MẶT (LẬP PHƯƠNG & HỘP CHỮ NHẬT)
# ==============================================================================

# Danh sách 12 cạnh thô của Hình lập phương / Hình hộp chữ nhật:
# - Đáy dưới: A-B, B-C, C-D, D-A
# - Đáy trên: E-F, F-G, G-H, H-E
# - Các cạnh bên: A-E, B-F, C-G, D-H
CUBE_EDGES_RAW = [
    ("A", "B"), ("B", "C"), ("C", "D"), ("D", "A"),
    ("E", "F"), ("F", "G"), ("G", "H"), ("H", "E"),
    ("A", "E"), ("B", "F"), ("C", "G"), ("D", "H"),
]

# 6 mặt chuẩn của khối lập phương / hộp chữ nhật
HEXAHEDRON_FACES: List[Tuple[str, ...]] = [
    ("A", "B", "C", "D"),  # Mặt đáy dưới
    ("E", "F", "G", "H"),  # Mặt đáy trên
    ("A", "B", "F", "E"),  # Mặt bên trước
    ("B", "C", "G", "F"),  # Mặt bên phải
    ("C", "D", "H", "G"),  # Mặt bên sau
    ("D", "A", "E", "H"),  # Mặt bên trái
]

# Phân nhóm 12 cạnh theo 3 phương (Dài - Rộng - Cao) để kiểm tra tỷ lệ độ dài 3D
HEXAHEDRON_EDGE_GROUPS: Dict[str, List[Tuple[str, str]]] = {
    "length": [GeometryKnowledge.normalize_edge(u, v) for u, v in [("A", "B"), ("C", "D"), ("E", "F"), ("G", "H")]],
    "width":  [GeometryKnowledge.normalize_edge(u, v) for u, v in [("B", "C"), ("D", "A"), ("F", "G"), ("H", "E")]],
    "height": [GeometryKnowledge.normalize_edge(u, v) for u, v in [("A", "E"), ("B", "F"), ("C", "G"), ("D", "H")]],
}

# Các bộ 3 đỉnh (đỉnh góc, láng giềng 1, láng giềng 2) phải tạo góc vuông 90 độ
HEXAHEDRON_ORTHOGONAL_CORNERS: List[Tuple[str, str, str]] = [
    # Góc vuông trên mặt đáy dưới
    ("A", "B", "D"), ("B", "A", "C"), ("C", "B", "D"), ("D", "A", "C"),
    # Góc vuông trên mặt đáy trên
    ("E", "F", "H"), ("F", "E", "G"), ("G", "F", "H"), ("H", "E", "G"),
    # Góc vuông giữa cạnh bên đứng và mặt đáy
    ("A", "B", "E"), ("A", "D", "E"),
    ("B", "A", "F"), ("B", "C", "F"),
    ("C", "B", "G"), ("C", "D", "G"),
    ("D", "A", "H"), ("D", "C", "H"),
]

# 12 đường chéo trên 6 mặt (hay bị học sinh nối nhầm nhất)
HEXAHEDRON_FACE_DIAGONALS = GeometryKnowledge.normalize_edge_list([
    ("A", "C"), ("B", "D"),  # Đáy dưới
    ("E", "G"), ("F", "H"),  # Đáy trên
    ("A", "F"), ("B", "E"),  # Mặt trước
    ("B", "G"), ("C", "F"),  # Mặt phải
    ("C", "H"), ("D", "G"),  # Mặt sau
    ("D", "E"), ("A", "H"),  # Mặt trái
])

# 4 đường chéo xuyên tâm không gian
HEXAHEDRON_SPACE_DIAGONALS = GeometryKnowledge.normalize_edge_list([
    ("A", "G"), ("B", "H"), ("C", "E"), ("D", "F"),
])


# ==============================================================================
# 1. HÌNH LẬP PHƯƠNG (CUBE)
# ==============================================================================

CUBE_KNOWLEDGE = GeometryKnowledge(
    shape_name="cube",
    expected_vertices=list("ABCDEFGH"),
    expected_edges=GeometryKnowledge.normalize_edge_list(CUBE_EDGES_RAW),
    expected_degrees={vertex: 3 for vertex in "ABCDEFGH"},
    display_name="Hình lập phương",
    expected_faces=HEXAHEDRON_FACES,
    edge_groups=HEXAHEDRON_EDGE_GROUPS,
    orthogonal_corners=HEXAHEDRON_ORTHOGONAL_CORNERS,
    face_diagonals=HEXAHEDRON_FACE_DIAGONALS,
    space_diagonals=HEXAHEDRON_SPACE_DIAGONALS,
    geometry_rules={
        "all_edges_equal": True,
        "opposite_edges_equal": True,
        "orthogonal_faces": True,
        "length_tolerance_ratio": 0.18,  # Cho phép sai số lắp ghép vật lý 18%
        "angle_cosine_tolerance": 0.25,  # |cos(theta)| <= 0.25 (~75° đến 105°)
    },
    formulas={
        "surface_area": "S_tp = 6 * a²",
        "volume": "V = a³",
        "description": "Hình lập phương có 6 mặt đều là hình vuông, 8 đỉnh và 12 cạnh bằng nhau.",
    },
)


# ==============================================================================
# 2. HÌNH HỘP CHỮ NHẬT (RECTANGULAR PRISM)
# ==============================================================================

PRISM_KNOWLEDGE = GeometryKnowledge(
    shape_name="rectangular_prism",
    expected_vertices=list("ABCDEFGH"),
    expected_edges=GeometryKnowledge.normalize_edge_list(CUBE_EDGES_RAW),
    expected_degrees={vertex: 3 for vertex in "ABCDEFGH"},
    display_name="Hình hộp chữ nhật",
    expected_faces=HEXAHEDRON_FACES,
    edge_groups=HEXAHEDRON_EDGE_GROUPS,
    orthogonal_corners=HEXAHEDRON_ORTHOGONAL_CORNERS,
    face_diagonals=HEXAHEDRON_FACE_DIAGONALS,
    space_diagonals=HEXAHEDRON_SPACE_DIAGONALS,
    geometry_rules={
        "all_edges_equal": False,        # Các nhóm cạnh dài/rộng/cao có độ dài khác nhau
        "opposite_edges_equal": True,    # 4 cạnh song song cùng nhóm phải bằng nhau
        "orthogonal_faces": True,
        "length_tolerance_ratio": 0.20,
        "min_aspect_ratio_diff": 0.20,   # Chênh lệch tối thiểu giữa các chiều để tách khỏi Cube
        "angle_cosine_tolerance": 0.25,
    },
    formulas={
        "surface_area": "S_tp = 2 * (a*b + b*c + c*a)",
        "volume": "V = a * b * c",
        "description": "Hình hộp chữ nhật có 6 mặt là hình chữ nhật, 8 đỉnh và 3 nhóm cạnh (dài, rộng, cao).",
    },
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

# 5 mặt chuẩn của Hình chóp tứ giác đều (1 mặt đáy tứ giác + 4 mặt bên tam giác)
PYRAMID_FACES: List[Tuple[str, ...]] = [
    ("A", "B", "C", "D"),  # Mặt đáy
    ("S", "A", "B"),       # Mặt bên 1
    ("S", "B", "C"),       # Mặt bên 2
    ("S", "C", "D"),       # Mặt bên 3
    ("S", "D", "A"),       # Mặt bên 4
]

PYRAMID_EDGE_GROUPS: Dict[str, List[Tuple[str, str]]] = {
    "base":    [GeometryKnowledge.normalize_edge(u, v) for u, v in [("A", "B"), ("B", "C"), ("C", "D"), ("D", "A")]],
    "lateral": [GeometryKnowledge.normalize_edge(u, v) for u, v in [("S", "A"), ("S", "B"), ("S", "C"), ("S", "D")]],
}

# Góc vuông tại 4 đỉnh mặt đáy của hình chóp tứ giác đều
PYRAMID_ORTHOGONAL_CORNERS: List[Tuple[str, str, str]] = [
    ("A", "B", "D"), ("B", "A", "C"), ("C", "B", "D"), ("D", "A", "C"),
]

# 2 đường chéo mặt đáy của hình chóp
PYRAMID_FACE_DIAGONALS = GeometryKnowledge.normalize_edge_list([
    ("A", "C"), ("B", "D"),
])

PYRAMID_KNOWLEDGE = GeometryKnowledge(
    shape_name="square_pyramid",
    expected_vertices=["A", "B", "C", "D", "S"],
    expected_edges=GeometryKnowledge.normalize_edge_list(PYRAMID_EDGES_RAW),
    expected_degrees={"A": 3, "B": 3, "C": 3, "D": 3, "S": 4},
    display_name="Hình chóp tứ giác đều",
    expected_faces=PYRAMID_FACES,
    edge_groups=PYRAMID_EDGE_GROUPS,
    orthogonal_corners=PYRAMID_ORTHOGONAL_CORNERS,
    face_diagonals=PYRAMID_FACE_DIAGONALS,
    space_diagonals=[],
    geometry_rules={
        "base_edges_equal": True,        # 4 cạnh đáy bằng nhau
        "lateral_edges_equal": True,     # 4 cạnh bên bằng nhau
        "orthogonal_base": True,         # Đáy là hình vuông
        "length_tolerance_ratio": 0.20,
        "angle_cosine_tolerance": 0.25,
    },
    formulas={
        "surface_area": "S_tp = a² + 2 * a * d (d: trung đoạn)",
        "volume": "V = (1/3) * a² * h",
        "description": "Hình chóp tứ giác đều có đáy là hình vuông ABCD và 4 mặt bên là các tam giác cân bằng nhau chung đỉnh S.",
    },
)


# ==============================================================================
# TỪ ĐIỂN TRI THỨC CHUNG & HÀM TIỆN ÍCH TRA CỨU (TUẦN 4 -> TUẦN 8)
# ==============================================================================

KNOWLEDGE_BASE: Dict[str, GeometryKnowledge] = {
    "cube": CUBE_KNOWLEDGE,                  # Tra cứu Hình lập phương
    "rectangular_prism": PRISM_KNOWLEDGE,    # Tra cứu Hình hộp chữ nhật
    "square_pyramid": PYRAMID_KNOWLEDGE,     # Tra cứu Hình chóp tứ giác đều
}

# Bảng ánh xạ ID Marker số từ OpenCV/ArUco (Role 1) sang tên Đỉnh chuẩn
DEFAULT_MARKER_ID_TO_VERTEX: Dict[str, str] = {
    "0": "A", "1": "B", "2": "C", "3": "D",
    "4": "E", "5": "F", "6": "G", "7": "H",
    "8": "S",
}


def normalize_vertex_id(raw_id: str | int, target_shape: str | None = None, total_vertices: int | None = None) -> str:
    """
    Chuẩn hóa ID đỉnh nhận từ Role 1 (Computer Vision):
    - Đổi ID số (0..8) sang chữ cái (A..H, S).
    - Nếu đang xét Hình chóp tứ giác đều (hoặc chỉ có <= 5 đỉnh) và Role 1 dùng marker 'E' (ID=4)
      làm đỉnh chóp thì tự động ánh xạ 'E' -> 'S'.
    """
    cleaned = str(raw_id).strip().upper()
    mapped = DEFAULT_MARKER_ID_TO_VERTEX.get(cleaned, cleaned)

    if mapped == "E" and target_shape == "square_pyramid":
        return "S"
    return mapped


def get_knowledge(shape_name: str) -> GeometryKnowledge | None:
    """Tra cứu an toàn tri thức hình học theo tên."""
    if not shape_name:
        return None
    return KNOWLEDGE_BASE.get(shape_name.strip().lower())
