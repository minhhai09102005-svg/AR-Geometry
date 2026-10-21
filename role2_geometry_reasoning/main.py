"""Script chạy thử nghiệm độc lập cho Module Geometry Reasoning (Hoàn thiện Tuần 4 -> Tuần 8)."""

import os
import sys
import time
from reasoning_engine import GeometryReasoningEngine
from schemas import CVDataInput, VertexInput

# Thiết lập encoding UTF-8 an toàn cho môi trường Windows Console
if sys.platform == "win32":
    os.system("")  # Bật hỗ trợ ANSI escape sequences trên Windows
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except AttributeError:
        pass

# Danh sách các kịch bản test mở rộng từ Tuần 2 đến Tuần 8
TEST_SCENARIOS = {
    "1": {
        "name": "Kịch bản 1: Mới chỉ lắp 4 cạnh đáy Cube (Thiếu 8 cạnh)",
        "shape": "cube",
        "vertices": list("ABCDEFGH"),
        "edges": [("A", "B"), ("B", "C"), ("C", "D"), ("D", "A")],
    },
    "2": {
        "name": "Kịch bản 2: Lắp 4 cạnh đáy Cube nhưng lỡ tay nối sai đường chéo A-C",
        "shape": "cube",
        "vertices": list("ABCDEFGH"),
        "edges": [
            ("A", "B"),
            ("B", "C"),
            ("C", "D"),
            ("D", "A"),
            ("A", "C"),  # Cạnh thừa / nối sai
        ],
    },
    "3": {
        "name": "Kịch bản 3: Lắp hoàn chỉnh 12 cạnh Hình Lập Phương (Đúng 100%, có đảo thứ tự đỉnh)",
        "shape": "cube",
        "vertices": list("ABCDEFGH"),
        "edges": [
            ("B", "A"),
            ("B", "C"),
            ("C", "D"),
            ("A", "D"),  # Đảo thứ tự đỉnh để test normalize_edge
            ("E", "F"),
            ("G", "F"),
            ("G", "H"),
            ("H", "E"),
            ("A", "E"),
            ("B", "F"),
            ("C", "G"),
            ("D", "H"),
        ],
    },
    "4": {
        "name": "Kịch bản 4 (Validation mức 1): Camera bị che khuất, thiếu đỉnh E, F, G, H",
        "shape": "cube",
        "vertices": ["A", "B", "C", "D"],  # Thiếu các đỉnh E, F, G, H
        "edges": [("A", "B"), ("B", "C"), ("C", "D"), ("D", "A")],
    },
    "5": {
        "name": "Kịch bản 5: Lắp hoàn chỉnh 8 cạnh Hình Chóp Tứ Giác Đều (Square Pyramid)",
        "shape": "square_pyramid",
        "vertices": ["A", "B", "C", "D", "S"],
        "edges": [
            ("A", "B"),
            ("B", "C"),
            ("C", "D"),
            ("D", "A"),  # 4 cạnh đáy
            ("S", "A"),
            ("S", "B"),
            ("S", "C"),
            ("S", "D"),  # 4 cạnh bên
        ],
    },
    "6": {
        "name": "Kịch bản 6 (Tuần 7): Hình Lập Phương hoàn chỉnh CÓ TỌA ĐỘ 3D thực (Tính S_tp và Thể tích V)",
        "shape": "cube",
        "3d_coords": {
            "A": (0.0, 0.0, 0.0), "B": (1.0, 0.0, 0.0), "C": (1.0, 1.0, 0.0), "D": (0.0, 1.0, 0.0),
            "E": (0.0, 0.0, 1.0), "F": (1.0, 0.0, 1.0), "G": (1.0, 1.0, 1.0), "H": (0.0, 1.0, 1.0)
        },
        "edges": [
            ("A", "B"), ("B", "C"), ("C", "D"), ("D", "A"),
            ("E", "F"), ("F", "G"), ("G", "H"), ("E", "H"),
            ("A", "E"), ("B", "F"), ("C", "G"), ("D", "H"),
        ],
    },
    "7": {
        "name": "Kịch bản 7 (Tuần 5): Tự động nhận dạng loại hình (Auto-Detect Shape)",
        "shape": "auto",
        "3d_coords": {
            "A": (0.0, 0.0, 0.0), "B": (2.0, 0.0, 0.0), "C": (2.0, 1.0, 0.0), "D": (0.0, 1.0, 0.0),
            "E": (0.0, 0.0, 1.0), "F": (2.0, 0.0, 1.0), "G": (2.0, 1.0, 1.0), "H": (0.0, 1.0, 1.0)
        },
        "edges": [
            ("A", "B"), ("B", "C"), ("C", "D"), ("D", "A"),
            ("E", "F"), ("F", "G"), ("G", "H"), ("E", "H"),
            ("A", "E"), ("B", "F"), ("C", "G"), ("D", "H"),
        ],
    },
}


def run_demo():
    print("==================================================================", flush=True)
    print("   SMARTGEOAR - GEOMETRY REASONING ENGINE (HOÀN THIỆN TUẦN 4 -> TUẦN 8)   ", flush=True)
    print("==================================================================\n", flush=True)

    for key, scenario in TEST_SCENARIOS.items():
        print(f"👉 {scenario['name']}", flush=True)

        # 1. Khởi tạo Engine theo hình cần test (Hỗ trợ chế độ 'auto' nhận diện hình)
        target_shape = scenario.get("shape", "cube")
        auto_detect = (target_shape == "auto")
        engine = GeometryReasoningEngine(target_shape=target_shape, auto_detect_shape=auto_detect, enable_3d_validation=True)

        # 2. Tạo dữ liệu giả lập (Mock Data) hỗ trợ cả tọa độ 3D (nếu có)
        coords = scenario.get("3d_coords", {})
        vertices_input = []
        for v in scenario["vertices"]:
            x, y, z = coords.get(v, (0.0, 0.0, 0.0))
            vertices_input.append(VertexInput(id=v, x=x, y=y, z=z))

        mock_input = CVDataInput(
            timestamp=time.time(),
            vertices=vertices_input,
            detected_edges=scenario["edges"],
        )

        # 3. Chạy thuật toán suy luận
        result = engine.process(mock_input)

        # 4. In kết quả tóm tắt ra Terminal với flush=True để xả bộ đệm ngay lập tức
        print(f"   - Hình nhận diện: {result.shape.upper()}", flush=True)
        print(f"   - Trạng thái: {result.status.upper()} (Đúng = {result.is_correct})", flush=True)
        print(f"   - Thông báo UI: '{result.message}'", flush=True)
        print(f"   - Mã lỗi: {result.error_type}", flush=True)
        
        if result.metrics:
            print(f"   - Chỉ số 3D: Cạnh TB = {result.metrics.get('mean_edge_length', 0)} | Thể tích V = {result.metrics.get('volume', 0)}", flush=True)

        print(
            f"   - Chi tiết: Thiếu đỉnh={result.details.missing_vertices} | "
            f"Nối sai={result.details.wrong_connections} | "
            f"Số cạnh thiếu={len(result.details.missing_edges)}",
            flush=True,
        )

        # In mẫu JSON xuất sang Unity ở Kịch bản 2 và Kịch bản 6 để kiểm tra định dạng
        if key in ("2", "6"):
            print(
                f"\n   📄 MẪU JSON PHẢN HỒI GỬI UNITY AR (XUẤT TỪ KỊCH BẢN {key}):",
                flush=True,
            )
            print(result.to_json(indent=4), flush=True)

        print("-" * 66, flush=True)


if __name__ == "__main__":
    run_demo()