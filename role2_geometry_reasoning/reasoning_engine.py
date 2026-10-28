"""Bộ máy suy luận hình học: so khớp đồ thị G=(V,E), kiểm tra 3D và sinh phản hồi AR."""

import inspect
import json
import math
from typing import Any, Dict, List, Optional, Set, Tuple

from knowledge_base import (
    KNOWLEDGE_BASE,
    GeometryKnowledge,
    normalize_vertex_id,
)
from schemas import CVDataInput, ErrorDetail, ReasoningResult


class GeometryReasoningEngine:
    """
    Bộ suy luận trung tâm của Thành viên 2 (Hoàn thiện lộ trình Tuần 4 -> Tuần 8):
    - Kiểm tra tô-pô đồ thị G=(V, E): đỉnh, cạnh, bậc đỉnh, mặt.
    - Phát hiện cạnh thiếu (missing_edge), cạnh thừa (extra_edge), nối sai (wrong_connection).
    - Tự động nhận diện loại hình (cube / rectangular_prism / square_pyramid / unknown).
    - Tính toán hình học 3D: độ dài cạnh, góc vuông, diện tích toàn phần, thể tích.
    - Sinh thông báo tiếng Việt chi tiết từng cạnh/đỉnh cho Unity AR.
    """

    def __init__(
        self,
        target_shape: str = "auto",
        auto_detect_shape: bool = False,
        enable_3d_validation: bool = True,
        enable_smoothing: bool = False,
        smoothing_ttl: int = 4,
    ) -> None:
        self.auto_detect_shape = auto_detect_shape or (target_shape.strip().lower() == "auto")
        self.enable_3d_validation = enable_3d_validation
        self.enable_smoothing = enable_smoothing
        self.smoothing_ttl = smoothing_ttl

        # Bộ nhớ đệm hỗ trợ giữ vị trí đỉnh khi camera bị mất tracking ngắn hạn (Tuần 8)
        self._frame_counter: int = 0
        self._vertex_cache: Dict[str, Tuple[Any, int]] = {}

        if not self.auto_detect_shape:
            if target_shape not in KNOWLEDGE_BASE:
                choices = ", ".join(KNOWLEDGE_BASE)
                raise ValueError(f"Hình '{target_shape}' không tồn tại. Hình hỗ trợ: {choices}.")
            self.knowledge: Optional[GeometryKnowledge] = KNOWLEDGE_BASE[target_shape]
        else:
            self.knowledge = None

    # ==========================================================================
    # 1. TIỀN XỬ LÝ DỮ LIỆU ĐẦU VÀO TỪ COMPUTER VISION (TUẦN 4 & TUẦN 8)
    # ==========================================================================

    @staticmethod
    def _format_edge(edge: Tuple[str, str]) -> str:
        """Định dạng cạnh (u, v) thành chuỗi 'u-v' để hiển thị trên UI AR."""
        return f"{edge[0]}-{edge[1]}"

    def _preprocess_cv_input(
        self, cv_input: CVDataInput, active_shape: Optional[str]
    ) -> Tuple[Dict[str, Any], Set[Tuple[str, str]]]:
        """
        Chuẩn hóa danh sách đỉnh và cạnh từ Role 1:
        - Hỗ trợ đổi Marker ID số (0..8) sang chữ cái (A..H, S).
        - Hỗ trợ làm mượt và giữ đỉnh khi mất marker tạm thời nếu bật enable_smoothing.
        """
        self._frame_counter += 1
        raw_vertex_count = len(cv_input.vertices)

        vertex_map: Dict[str, Any] = {}
        for v in cv_input.vertices:
            norm_id = normalize_vertex_id(
                v.id, target_shape=active_shape, total_vertices=raw_vertex_count
            )
            vertex_map[norm_id] = v

        # Nếu bật chống mất marker tạm thời (Tuần 8)
        if self.enable_smoothing:
            for v_id, v_obj in vertex_map.items():
                self._vertex_cache[v_id] = (v_obj, self._frame_counter)

            expired = []
            for cached_id, (cached_v, last_seen) in self._vertex_cache.items():
                if self._frame_counter - last_seen <= self.smoothing_ttl:
                    if cached_id not in vertex_map:
                        vertex_map[cached_id] = cached_v
                else:
                    expired.append(cached_id)
            for exp_id in expired:
                del self._vertex_cache[exp_id]

        normalized_edges: Set[Tuple[str, str]] = set()
        for u, v in cv_input.detected_edges:
            u_norm = normalize_vertex_id(u, target_shape=active_shape, total_vertices=raw_vertex_count)
            v_norm = normalize_vertex_id(v, target_shape=active_shape, total_vertices=raw_vertex_count)
            if u_norm != v_norm:
                normalized_edges.add(GeometryKnowledge.normalize_edge(u_norm, v_norm))

        return vertex_map, normalized_edges

    # ==========================================================================
    # 2. BIỂU DIỄN ĐỒ THỊ & NHẬN DẠNG LOẠI HÌNH (TUẦN 5)
    # ==========================================================================

    @staticmethod
    def _compute_degrees(
        vertices: Set[str], edges: Set[Tuple[str, str]]
    ) -> Dict[str, int]:
        """Tính bậc (số cạnh nối vào) của từng đỉnh trên đồ thị G=(V,E)."""
        degrees: Dict[str, int] = {v: 0 for v in vertices}
        for u, v in edges:
            degrees[u] = degrees.get(u, 0) + 1
            degrees[v] = degrees.get(v, 0) + 1
        return degrees

    def identify_shape(
        self, vertex_map: Dict[str, Any], detected_edges: Set[Tuple[str, str]]
    ) -> Tuple[str, float]:
        """
        [Hạng mục 6 - Tuần 5] Tự động nhận dạng loại hình từ Graph đầu vào và tọa độ 3D:
        Trả về (shape_name, confidence_score).
        """
        detected_v = set(vertex_map.keys())
        if not detected_v and not detected_edges:
            return "unknown", 0.0

        def score_against(kb_item: GeometryKnowledge) -> float:
            exp_v = set(kb_item.expected_vertices)
            exp_e = set(kb_item.expected_edges)
            v_score = len(detected_v & exp_v) / max(len(exp_v), len(detected_v), 1)
            e_score = len(detected_edges & exp_e) / max(len(exp_e), len(detected_edges), 1)
            return round(0.4 * v_score + 0.6 * e_score, 3)

        pyramid_score = score_against(KNOWLEDGE_BASE["square_pyramid"])
        cube_score = score_against(KNOWLEDGE_BASE["cube"])

        # Nếu khớp với hình chóp tứ giác đều (5 đỉnh A, B, C, D, S)
        if ("S" in detected_v or len(detected_v) <= 5) and pyramid_score >= cube_score and pyramid_score >= 0.35:
            return "square_pyramid", pyramid_score

        # Nếu khớp với khối 6 mặt (8 đỉnh A..H), dùng tọa độ 3D để phân biệt Cube và Rectangular Prism
        if cube_score >= 0.35:
            if self._has_valid_3d_coordinates(vertex_map):
                if self._is_rectangular_prism_3d(vertex_map):
                    return "rectangular_prism", cube_score
            return "cube", cube_score

        return "unknown", max(pyramid_score, cube_score)

    def _is_rectangular_prism_3d(self, vertex_map: Dict[str, Any]) -> bool:
        """
        Kiểm tra 3 nhóm cạnh (dài, rộng, cao): nếu chênh lệch giữa nhóm dài nhất
        và ngắn nhất vượt quá 20% thì mô hình là Hình hộp chữ nhật (rectangular_prism).
        """
        cube_kb = KNOWLEDGE_BASE["cube"]
        edge_groups = getattr(cube_kb, "edge_groups", {})
        if not edge_groups:
            return False

        group_averages: List[float] = []
        for group_edges in edge_groups.values():
            lengths = [
                self._distance_3d(vertex_map[u], vertex_map[v])
                for u, v in group_edges
                if u in vertex_map and v in vertex_map
            ]
            if lengths:
                group_averages.append(sum(lengths) / len(lengths))

        if len(group_averages) < 2:
            return False

        min_len, max_len = min(group_averages), max(group_averages)
        if min_len <= 1e-6:
            return False

        return ((max_len - min_len) / min_len) > 0.20

    # ==========================================================================
    # 3. PHÂN LOẠI LỖI NỐI SAI (WRONG CONNECTION) VS CẠNH THỪA (TUẦN 5 & 6)
    # ==========================================================================

    def _analyze_wrong_connections(
        self,
        knowledge: GeometryKnowledge,
        missing_edges: List[Tuple[str, str]],
        extra_edges: List[Tuple[str, str]],
        degrees: Dict[str, int],
    ) -> Tuple[List[Tuple[Optional[Tuple[str, str]], Tuple[str, str]]], List[Tuple[str, str]]]:
        """
        [Hạng mục 5 - Tuần 5 & 6] Phân tích các cạnh nằm ngoài danh sách chuẩn:
        - Một cạnh bị coi là `wrong_connection` (nối sai) nếu:
          1. Nối vào đỉnh lạ không thuộc mô hình.
          2. Là đường chéo mặt hoặc đường chéo không gian bị nối nhầm khi mô hình đang thiếu cạnh.
          3. Có ít nhất 1 đầu mút đang bị thiếu cạnh chuẩn đi qua.
        - Ngược lại, nếu tất cả các cạnh chuẩn đã đủ (12/12 hoặc 8/8) mà gắn dư thêm thanh nối
          thì phân loại là `extra_edge` (cạnh thừa).
        """
        if not extra_edges:
            return [], []

        expected_v_set = set(knowledge.expected_vertices)
        vertices_with_missing_edge = {v for edge in missing_edges for v in edge}
        face_diags = set(getattr(knowledge, "face_diagonals", []))
        space_diags = set(getattr(knowledge, "space_diagonals", []))

        wrong_connections: List[Tuple[Optional[Tuple[str, str]], Tuple[str, str]]] = []
        pure_extra_edges: List[Tuple[str, str]] = []

        for u, v in extra_edges:
            is_invalid_vertex = (u not in expected_v_set) or (v not in expected_v_set)
            is_replacing_missing_edge = bool(missing_edges) and (
                u in vertices_with_missing_edge
                or v in vertices_with_missing_edge
                or (u, v) in face_diags
                or (u, v) in space_diags
                or degrees.get(u, 0) <= knowledge.expected_degrees.get(u, 3)
                or degrees.get(v, 0) <= knowledge.expected_degrees.get(v, 3)
            )

            if is_invalid_vertex or is_replacing_missing_edge:
                # Chỉ ghép được cạnh bị thay thế khi đầu còn lại của cạnh lạ bị thừa bậc.
                # Nếu không có bằng chứng rõ, để cạnh chuẩn là None thay vì đoán bừa.
                replacement = next(
                    (
                        expected
                        for expected in missing_edges
                        if (
                            (u in expected and degrees.get(v, 0) > knowledge.expected_degrees.get(v, 3))
                            or (v in expected and degrees.get(u, 0) > knowledge.expected_degrees.get(u, 3))
                        )
                    ),
                    None,
                )
                wrong_connections.append((replacement, (u, v)))
            else:
                pure_extra_edges.append((u, v))

        return wrong_connections, pure_extra_edges

    # ==========================================================================
    # 4. TÍNH TOÁN HÌNH HỌC 3D: ĐỘ DÀI, GÓC VUÔNG, DIỆN TÍCH, THỂ TÍCH (TUẦN 7)
    # ==========================================================================

    @staticmethod
    def _has_valid_3d_coordinates(vertex_map: Dict[str, Any]) -> bool:
        """Kiểm tra xem dữ liệu từ CV đã có tọa độ 3D thực (khác 0) hay chỉ là dữ liệu mock."""
        if len(vertex_map) < 2:
            return False
        non_zero_count = sum(
            1
            for v in vertex_map.values()
            if abs(getattr(v, "x", 0.0)) > 1e-5
            or abs(getattr(v, "y", 0.0)) > 1e-5
            or abs(getattr(v, "z", 0.0)) > 1e-5
        )
        return non_zero_count >= 2

    @staticmethod
    def _distance_3d(v1: Any, v2: Any) -> float:
        """Tính khoảng cách Euclid trong không gian 3D giữa 2 đỉnh."""
        dx = float(getattr(v1, "x", 0.0)) - float(getattr(v2, "x", 0.0))
        dy = float(getattr(v1, "y", 0.0)) - float(getattr(v2, "y", 0.0))
        dz = float(getattr(v1, "z", 0.0)) - float(getattr(v2, "z", 0.0))
        return math.sqrt(dx * dx + dy * dy + dz * dz)

    @staticmethod
    def _cosine_between_edges(origin: Any, p1: Any, p2: Any) -> float:
        """Tính cosin góc tạo bởi 2 vector (origin -> p1) và (origin -> p2) bằng tích vô hướng."""
        ux = float(getattr(p1, "x", 0.0)) - float(getattr(origin, "x", 0.0))
        uy = float(getattr(p1, "y", 0.0)) - float(getattr(origin, "y", 0.0))
        uz = float(getattr(p1, "z", 0.0)) - float(getattr(origin, "z", 0.0))

        vx = float(getattr(p2, "x", 0.0)) - float(getattr(origin, "x", 0.0))
        vy = float(getattr(p2, "y", 0.0)) - float(getattr(origin, "y", 0.0))
        vz = float(getattr(p2, "z", 0.0)) - float(getattr(origin, "z", 0.0))

        norm_u = math.sqrt(ux * ux + uy * uy + uz * uz)
        norm_v = math.sqrt(vx * vx + vy * vy + vz * vz)
        if norm_u <= 1e-6 or norm_v <= 1e-6:
            return 0.0
        return (ux * vx + uy * vy + uz * vz) / (norm_u * norm_v)

    def compute_geometry_metrics(
        self,
        knowledge: GeometryKnowledge,
        vertex_map: Dict[str, Any],
        detected_edges: Set[Tuple[str, str]],
    ) -> Dict[str, Any]:
        """
        [Hạng mục 7 - Tuần 7] Tính toán thông số hình học 3D bổ sung:
        - Độ dài từng cạnh & độ dài trung bình.
        - Kiểm tra các cạnh bằng nhau theo quy tắc của từng hình.
        - Kiểm tra góc vuông tại các đỉnh.
        - Tính diện tích toàn phần (surface_area) và thể tích (volume).
        """
        if not self._has_valid_3d_coordinates(vertex_map):
            return {}

        edge_lengths: Dict[str, float] = {}
        for u, v in sorted(detected_edges):
            if u in vertex_map and v in vertex_map:
                edge_lengths[f"{u}-{v}"] = round(self._distance_3d(vertex_map[u], vertex_map[v]), 4)

        if not edge_lengths:
            return {}

        mean_length = round(sum(edge_lengths.values()) / len(edge_lengths), 4)
        rules = getattr(knowledge, "geometry_rules", {})
        length_tol = float(rules.get("length_tolerance_ratio", 0.20))
        cos_tol = float(rules.get("angle_cosine_tolerance", 0.25))

        # 1. Kiểm tra độ lệch chiều dài cạnh trong từng nhóm cạnh chuẩn
        length_warnings: List[str] = []
        edge_groups = getattr(knowledge, "edge_groups", {})
        group_means: Dict[str, float] = {}

        for group_name, group_edges in edge_groups.items():
            g_lens = [
                self._distance_3d(vertex_map[u], vertex_map[v])
                for u, v in group_edges
                if u in vertex_map and v in vertex_map and (u, v) in detected_edges
            ]
            if g_lens:
                g_mean = sum(g_lens) / len(g_lens)
                group_means[group_name] = g_mean
                if g_mean > 1e-5:
                    for u, v in group_edges:
                        if u in vertex_map and v in vertex_map and (u, v) in detected_edges:
                            d = self._distance_3d(vertex_map[u], vertex_map[v])
                            if abs(d - g_mean) / g_mean > length_tol:
                                length_warnings.append(f"Cạnh {u}-{v} ({d:.2f}) lệch chuẩn nhóm {group_name} ({g_mean:.2f})")

        # Nếu là Hình lập phương, cả 3 nhóm dài - rộng - cao phải xấp xỉ bằng nhau
        if knowledge.shape_name == "cube" and len(group_means) >= 2:
            min_g, max_g = min(group_means.values()), max(group_means.values())
            if min_g > 1e-5 and (max_g - min_g) / min_g > length_tol:
                length_warnings.append(
                    "Các chiều dài, rộng, cao không bằng nhau (giống Hình hộp chữ nhật hơn Hình lập phương)"
                )

        # 2. Kiểm tra góc vuông tại các góc chuẩn
        non_orthogonal_corners: List[str] = []
        for origin_id, n1_id, n2_id in getattr(knowledge, "orthogonal_corners", []):
            e1 = GeometryKnowledge.normalize_edge(origin_id, n1_id)
            e2 = GeometryKnowledge.normalize_edge(origin_id, n2_id)
            if (
                origin_id in vertex_map
                and n1_id in vertex_map
                and n2_id in vertex_map
                and e1 in detected_edges
                and e2 in detected_edges
            ):
                cos_val = abs(self._cosine_between_edges(vertex_map[origin_id], vertex_map[n1_id], vertex_map[n2_id]))
                if cos_val > cos_tol:
                    deg = round(math.degrees(math.acos(min(1.0, max(-1.0, cos_val)))), 1)
                    non_orthogonal_corners.append(f"Góc {n1_id}-{origin_id}-{n2_id} lệch vuông ({deg}°)")

        # 3. Tính diện tích toàn phần & thể tích
        surface_area = 0.0
        volume = 0.0
        if knowledge.shape_name == "cube":
            surface_area = round(6.0 * (mean_length ** 2), 8)
            volume = round(mean_length ** 3, 8)
        elif knowledge.shape_name == "rectangular_prism":
            a = group_means.get("length", mean_length)
            b = group_means.get("width", mean_length)
            c = group_means.get("height", mean_length)
            surface_area = round(2.0 * (a * b + b * c + c * a), 8)
            volume = round(a * b * c, 8)
        elif knowledge.shape_name == "square_pyramid":
            a = group_means.get("base", mean_length)
            b = group_means.get("lateral", mean_length)
            base_area = a * a
            slant_height = math.sqrt(max(0.0, b * b - (a / 2.0) ** 2))
            lateral_area = 2.0 * a * slant_height
            height = math.sqrt(max(0.0, b * b - (a * a) / 2.0))
            surface_area = round(base_area + lateral_area, 8)
            volume = round((1.0 / 3.0) * base_area * height, 8)

        return {
            "edge_lengths": edge_lengths,
            "mean_edge_length": mean_length,
            "is_edges_equal": len(length_warnings) == 0,
            "length_warnings": length_warnings,
            "is_orthogonal": len(non_orthogonal_corners) == 0,
            "non_orthogonal_corners": non_orthogonal_corners,
            "surface_area": surface_area,
            "volume": volume,
            # Gửi kèm công thức để giao diện có thể giải thích kết quả học tập.
            "formulas": dict(getattr(knowledge, "formulas", {})),
        }

    # ==========================================================================
    # 5. KHỞI TẠO ĐỐI TƯỢNG ĐẦU RA AN TOÀN VỚI MỌI PHIÊN BẢN SCHEMAS.PY
    # ==========================================================================

    @staticmethod
    def _build_error_detail(**kwargs: Any) -> ErrorDetail:
        """Chỉ truyền các trường mà class ErrorDetail trong schemas.py hỗ trợ."""
        sig = inspect.signature(ErrorDetail)
        valid_kwargs = {k: v for k, v in kwargs.items() if k in sig.parameters}
        return ErrorDetail(**valid_kwargs)

    @staticmethod
    def _build_reasoning_result(**kwargs: Any) -> ReasoningResult:
        """Chỉ truyền các trường mà class ReasoningResult trong schemas.py hỗ trợ."""
        sig = inspect.signature(ReasoningResult)
        valid_kwargs = {k: v for k, v in kwargs.items() if k in sig.parameters}
        return ReasoningResult(**valid_kwargs)

    # ==========================================================================
    # 6. HÀM SUY LUẬN CHÍNH (PROCESS) & SINH PHẢN HỒI AR (TUẦN 4 -> TUẦN 8)
    # ==========================================================================

    def process(self, cv_input: CVDataInput) -> ReasoningResult:
        """Phân tích dữ liệu đỉnh/cạnh từ CV và trả về kết quả suy luận cho Unity AR."""
        # Dùng một hàm dựng chung để mọi phản hồi đều mang đúng mã khung hình CV.
        def build_result(**kwargs: Any) -> ReasoningResult:
            kwargs.setdefault("frame_id", getattr(cv_input, "frame_id", 0))
            return self._build_reasoning_result(**kwargs)

        initial_shape_hint = self.knowledge.shape_name if self.knowledge else getattr(cv_input, "target_shape", None)
        vertex_map, detected_edges = self._preprocess_cv_input(cv_input, initial_shape_hint)

        # Nếu chạy ở chế độ tự động nhận diện hình (Tuần 5)
        if self.auto_detect_shape or self.knowledge is None:
            detected_shape, confidence = self.identify_shape(vertex_map, detected_edges)
            if detected_shape == "unknown":
                return build_result(
                    shape="unknown",
                    status="incomplete",
                    is_correct=False,
                    error_type="unknown_shape",
                    confidence=confidence,
                    message="Chưa nhận diện được loại hình học. Hãy đưa camera lại gần để thấy rõ các đỉnh và cạnh.",
                    details=self._build_error_detail(),
                )
            active_knowledge = KNOWLEDGE_BASE[detected_shape]
        else:
            active_knowledge = self.knowledge
            _, confidence = self.identify_shape(vertex_map, detected_edges)

        detected_vertices = set(vertex_map.keys())
        expected_vertices = set(active_knowledge.expected_vertices)
        expected_edges = set(active_knowledge.expected_edges)

        # Tính toán các tập hợp sai khác và sắp xếp để kết quả JSON ổn định
        missing_vertices = sorted(expected_vertices - detected_vertices)
        extra_vertices = sorted(detected_vertices - expected_vertices)
        missing_edges = sorted(expected_edges - detected_edges)
        extra_edges = sorted(detected_edges - expected_edges)

        # Phân tích bậc đỉnh và phân loại cạnh nối sai vs cạnh thừa
        degrees = self._compute_degrees(detected_vertices | expected_vertices, detected_edges)
        degree_errors = {
            v: degrees.get(v, 0)
            for v, exp_deg in active_knowledge.expected_degrees.items()
            if degrees.get(v, 0) != exp_deg
        }
        wrong_connections, pure_extra_edges = self._analyze_wrong_connections(
            active_knowledge, missing_edges, extra_edges, degrees
        )

        # Tập các đỉnh liên quan đến lỗi để Unity highlight trên AR Overlay
        affected_vertices = sorted(
            set(missing_vertices)
            | set(extra_vertices)
            | {v for e in missing_edges for v in e}
            | {v for e in extra_edges for v in e}
        )

        # Tính toán thông số hình học 3D nếu có tọa độ (Tuần 7)
        metrics = (
            self.compute_geometry_metrics(active_knowledge, vertex_map, detected_edges)
            if self.enable_3d_validation
            else {}
        )

        missing_edges_str = ", ".join(self._format_edge(e) for e in missing_edges)
        extra_edges_str = ", ".join(self._format_edge(e) for e in extra_edges)
        wrong_conn_str = ", ".join(
            (
                f"{self._format_edge(expected)} thay bằng {self._format_edge(actual)}"
                if expected is not None
                else f"cạnh nối sai {self._format_edge(actual)}"
            )
            for expected, actual in wrong_connections
        ) or ", ".join(self._format_edge(e) for e in extra_edges)
        shape_label = getattr(active_knowledge, "display_name", active_knowledge.shape_name)

        # ----------------------------------------------------------------------
        # TRƯỜNG HỢP 1: ĐỒ THỊ ĐỈNH & CẠNH ĐÚNG HOÀN TOÀN
        # ----------------------------------------------------------------------
        if not missing_vertices and not extra_vertices and not missing_edges and not extra_edges:
            # Kiểm tra thêm điều kiện hình học 3D (góc vuông / độ dài cạnh) nếu có tọa độ thực
            if metrics and (not metrics.get("is_orthogonal", True) or not metrics.get("is_edges_equal", True)):
                geo_issues = metrics.get("length_warnings", []) + metrics.get("non_orthogonal_corners", [])
                issue_summary = "; ".join(geo_issues[:2])
                return build_result(
                    shape=active_knowledge.shape_name,
                    status="invalid",
                    is_correct=False,
                    error_type="geometry_mismatch",
                    confidence=confidence,
                    message=f"Đã nối đủ cạnh của {shape_label} nhưng kích thước/góc bị lệch: {issue_summary}.",
                    details=self._build_error_detail(
                        degree_errors=degree_errors,
                        affected_vertices=affected_vertices,
                    ),
                    metrics=metrics,
                )

            success_msg = "Chúc mừng! Bạn đã lắp mô hình hoàn toàn chính xác."
            if metrics and metrics.get("volume", 0.0) > 0:
                success_msg = (
                    f"Chúc mừng! Bạn đã lắp {shape_label} hoàn toàn chính xác "
                    f"(Cạnh TB: {metrics['mean_edge_length']}, S_tp: {metrics['surface_area']}, V: {metrics['volume']})."
                )

            return build_result(
                shape=active_knowledge.shape_name,
                status="valid",
                is_correct=True,
                error_type=None,
                confidence=1.0,
                message=success_msg,
                details=self._build_error_detail(),
                metrics=metrics,
            )

        # ----------------------------------------------------------------------
        # TRƯỜNG HỢP 2: THIẾU ĐỈNH (CAMERA CHƯA QUAN SÁT ĐỦ HOẶC CHƯA LẮP ĐỈNH)
        # ----------------------------------------------------------------------
        if missing_vertices:
            msg = f"Mô hình chưa hoàn thành! Thiếu {len(missing_vertices)} đỉnh: {', '.join(missing_vertices)}."
            if missing_edges:
                msg += f" Thiếu cạnh: {missing_edges_str}."
            return build_result(
                shape=active_knowledge.shape_name,
                status="incomplete",
                is_correct=False,
                error_type="missing_vertex",
                confidence=confidence,
                message=msg,
                details=self._build_error_detail(
                    missing_vertices=missing_vertices,
                    extra_vertices=extra_vertices,
                    missing_edges=missing_edges,
                    extra_edges=extra_edges,
                    wrong_connections=wrong_connections,
                    degree_errors=degree_errors,
                    affected_vertices=affected_vertices,
                ),
                metrics=metrics,
            )

        # ----------------------------------------------------------------------
        # TRƯỜNG HỢP 3: NỐI SAI CẠNH (VỪA THIẾU CẠNH CHUẨN VỪA CÓ CẠNH/ĐỈNH LẠ)
        # ----------------------------------------------------------------------
        if missing_edges and (extra_vertices or wrong_connections):
            return build_result(
                shape=active_knowledge.shape_name,
                status="invalid",
                is_correct=False,
                error_type="wrong_connection",
                confidence=confidence,
                message=(
                    f"Mô hình nối sai! Nối sai cạnh: {wrong_conn_str}; "
                    f"Thiếu {len(missing_edges)} cạnh: {missing_edges_str}."
                ),
                details=self._build_error_detail(
                    extra_vertices=extra_vertices,
                    missing_edges=missing_edges,
                    extra_edges=extra_edges,
                    wrong_connections=wrong_connections,
                    degree_errors=degree_errors,
                    affected_vertices=affected_vertices,
                ),
                metrics=metrics,
            )

        # ----------------------------------------------------------------------
        # TRƯỜNG HỢP 4: CHỈ THIẾU CẠNH (MISSING EDGE)
        # ----------------------------------------------------------------------
        if missing_edges:
            return build_result(
                shape=active_knowledge.shape_name,
                status="incomplete",
                is_correct=False,
                error_type="missing_edge",
                confidence=confidence,
                message=(
                    f"Mô hình chưa hoàn thành! Bạn đang thiếu {len(missing_edges)} cạnh: "
                    f"{missing_edges_str}."
                ),
                details=self._build_error_detail(
                    missing_edges=missing_edges,
                    extra_edges=extra_edges,
                    degree_errors=degree_errors,
                    affected_vertices=affected_vertices,
                ),
                metrics=metrics,
            )

        # ----------------------------------------------------------------------
        # TRƯỜNG HỢP 5: THỪA ĐỈNH HOẶC THỪA CẠNH (EXTRA VERTEX / EXTRA EDGE)
        # ----------------------------------------------------------------------
        return build_result(
            shape=active_knowledge.shape_name,
            status="invalid",
            is_correct=False,
            error_type="extra_vertex" if extra_vertices else "extra_edge",
            confidence=confidence,
            message=(
                f"Mô hình bị lỗi! Thừa {len(extra_vertices)} đỉnh ({', '.join(extra_vertices)}) "
                f"và {len(extra_edges)} cạnh không phù hợp: {extra_edges_str}."
                if extra_vertices
                else f"Mô hình bị lỗi! Thừa {len(extra_edges)} cạnh không phù hợp: {extra_edges_str}."
            ),
            details=self._build_error_detail(
                extra_vertices=extra_vertices,
                extra_edges=extra_edges,
                wrong_connections=wrong_connections,
                degree_errors=degree_errors,
                affected_vertices=affected_vertices,
            ),
            metrics=metrics,
        )

    def process_json(self, payload: str, indent: int = 2) -> str:
        """Nhận JSON CV và trả JSON kết quả để dùng ở lớp tích hợp với Unity."""
        try:
            decoded = json.loads(payload)
        except (TypeError, json.JSONDecodeError) as error:
            raise ValueError("Không đọc được JSON đầu vào từ Computer Vision.") from error
        cv_input = CVDataInput.from_dict(decoded)
        return self.process(cv_input).to_json(indent=indent)
