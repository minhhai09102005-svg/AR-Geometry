# Geometry Reasoning

Module Role 2 nhận dữ liệu đỉnh/cạnh từ Computer Vision, so khớp với tri thức ba mô hình MVP và trả kết quả JSON cho Unity. Module hỗ trợ hình lập phương, hình hộp chữ nhật và hình chóp tứ giác đều.

## Chạy demo

Từ thư mục này:

```powershell
python main.py
```

Demo gồm tình huống thiếu đỉnh/cạnh, nối cạnh sai, dữ liệu hợp lệ của ba hình và JSON phản hồi mẫu.

## Hợp đồng dữ liệu

```python
CVDataInput(
    timestamp=1720000000.0,
    frame_id=120,
    vertices=[VertexInput(id="A", x=0.0, y=0.0, z=0.0)],
    detected_edges=[("A", "B")],
)
```

`GeometryReasoningEngine("cube").process(data)` kiểm tra theo hình được chọn. Có thể bỏ đối số hình để suy ra từ graph đầy đủ; hình lập phương và hộp chữ nhật có cùng topology nên cần tọa độ để phân biệt. Cạnh được chuẩn hóa không phân biệt chiều và ID marker số 0–8 được đổi sang đỉnh chữ; marker 4 ánh xạ thành `S` khi target là hình chóp.

Khi nhận dữ liệu dạng JSON, gọi `GeometryReasoningEngine().process_json(payload)`. Input phải có `vertices` và `detected_edges` (hoặc `edges`), cùng `timestamp`, `frame_id`/`frameId` và `target_shape` tùy chọn. Các packet chỉ có `hands`/gesture từ hand tracking không có graph đỉnh/cạnh nên bị từ chối với thông báo rõ ràng; cần Role 1 xuất thêm cấu trúc mô hình trước khi nối trực tiếp vào reasoning.

JSON phản hồi giữ `shape`, `status`, `is_correct`, `message`, `error_type` và `details` để tương thích cũ; đồng thời có `correct`, confidence, `frame_id`, các đỉnh liên quan, độ dài cạnh, góc, diện tích và thể tích khi tọa độ đủ dùng. `error_type` có thể là `missing_vertex`, `missing_edge`, `wrong_connection`, `extra_edge`, `extra_vertex`, `geometry_mismatch` hoặc `unknown_shape`.

Với lỗi nối sai, `details.wrong_connections` chứa từng cặp `[cạnh_chuẩn_bị_thiếu_hoặc_null, cạnh_đang_nối_nhầm]` để Unity biết nên đánh dấu và hướng dẫn sửa. Cạnh chuẩn là `null` nếu không đủ dữ liệu để suy ra cạnh nào đã bị thay thế. Các phép đo 3D nằm trong `metrics`; công thức của hình cũng được gửi trong `metrics.formulas`. Khi bật `enable_smoothing`, engine giữ lại vị trí marker tối đa `smoothing_ttl` frame để giảm nhấp nháy lúc tracking mất ngắn hạn.

## Kịch bản tự động

```powershell
python -m unittest -v
```

Các kịch bản bao phủ ba mô hình, cạnh thiếu/nối sai, chuẩn hóa ID và chiều cạnh, nhận dạng graph, số đo hình học và JSON Unity.
