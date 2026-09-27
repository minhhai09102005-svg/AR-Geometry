"""Script chạy thử nghiệm độc lập cho Module Geometry Reasoning (Tuần 2)."""

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

# Danh sách các kịch bản test mở rộng cho Tuần 2
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
}


def run_demo():
    print("==================================================", flush=True)
    print("   SMARTGEOAR - GEOMETRY REASONING (DEMO TUẦN 2)   ", flush=True)
    print("==================================================\n", flush=True)

    for key, scenario in TEST_SCENARIOS.items():
        print(f"👉 {scenario['name']}", flush=True)

        # 1. Khởi tạo Engine theo hình cần test
        engine = GeometryReasoningEngine(target_shape=scenario["shape"])

        # 2. Tạo dữ liệu giả lập (Mock Data)
        mock_input = CVDataInput(
            timestamp=time.time(),
            vertices=[
                VertexInput(id=v, x=0.0, y=0.0, z=0.0)
                for v in scenario["vertices"]
            ],
            detected_edges=scenario["edges"],
        )

        # 3. Chạy thuật toán suy luận
        result = engine.process(mock_input)

        # 4. In kết quả tóm tắt ra Terminal với flush=True để xả bộ đệm ngay lập tức
        print(f"   - Trạng thái: {result.status.upper()}", flush=True)
        print(f"   - Thông báo UI: '{result.message}'", flush=True)
        print(f"   - Mã lỗi: {result.error_type}", flush=True)
        print(
            f"   - Chi tiết: Thiếu đỉnh={result.details.missing_vertices} | "
            f"Thừa cạnh={result.details.extra_edges} | "
            f"Số cạnh thiếu={len(result.details.missing_edges)}",
            flush=True,
        )

        # In mẫu JSON xuất sang Unity ở Kịch bản 2 để kiểm tra định dạng
        if key == "2":
            print(
                "\n   📄 MẪU JSON PHẢN HỒI GỬI UNITY AR (XUẤT TỪ KỊCH BẢN 2):",
                flush=True,
            )
            print(result.to_json(indent=4), flush=True)

        print("-" * 60, flush=True)


if __name__ == "__main__":
    run_demo()