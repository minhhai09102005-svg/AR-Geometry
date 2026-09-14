import time
import sys
from schemas import CVDataInput, VertexInput
from reasoning_engine import GeometryReasoningEngine

# Danh sách các kịch bản test thực tế
TEST_SCENARIOS = {
    "1": {
        "name": "Kịch bản 1: Học sinh mới chỉ lắp 4 cạnh đáy (Thiếu cạnh)",
        "edges": [("A", "B"), ("B", "C"), ("C", "D"), ("D", "A")]
    },
    "2": {
        "name": "Kịch bản 2: Lắp 4 cạnh đáy nhưng lỡ tay nối sai cạnh chéo A-C (Nối sai)",
        "edges": [("A", "B"), ("B", "C"), ("C", "D"), ("D", "A"), ("A", "C")]
    },
    "3": {
        "name": "Kịch bản 3: Lắp hoàn chỉnh 12 cạnh Hình Lập Phương (Đúng 100%)",
        "edges": [
            ("A", "B"), ("B", "C"), ("C", "D"), ("D", "A"),
            ("E", "F"), ("F", "G"), ("G", "H"), ("H", "E"),
            ("A", "E"), ("B", "F"), ("C", "G"), ("D", "H")
        ]
    }
}

def run_demo():
    sys.stdout.reconfigure(encoding="utf-8")
    engine = GeometryReasoningEngine(target_shape="cube")
    
    print("==================================================")
    print("  SUY LUẬN HÌNH HỌC  ")
    print("==================================================\n")
    
    for key, scenario in TEST_SCENARIOS.items():
        print(f"👉 {scenario['name']}")
        
        # Tạo dữ liệu giả lập cho từng kịch bản
        mock_input = CVDataInput(
            timestamp=time.time(),
            vertices=[VertexInput(id=v, x=0.0, y=0.0, z=0.0) for v in "ABCDEFGH"],
            detected_edges=scenario["edges"]
        )
        
        # Chạy suy luận
        result = engine.process(mock_input)
        
        # In kết quả tóm tắt
        print(f"   - Trạng thái: {result.status.upper()}")
        print(f"   - Thông báo UI: '{result.message}'")
        print(f"   - Mã lỗi: {result.error_type}")
        print(f"   - Chi tiết cạnh lỗi: Extra={result.details.extra_edges} | Missing count={len(result.details.missing_edges)}")
        print("-" * 50)

if __name__ == "__main__":
    run_demo()
