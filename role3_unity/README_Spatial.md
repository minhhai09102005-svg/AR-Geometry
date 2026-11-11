# SmartGeoAR — giao diện và thao tác trong không gian

Phần này dùng lại MainMenu và ARMain, giữ TCP/JSON cổng 5053 và logic kiểm tra
của Role 2. Các đối tượng được dựng bằng code theo cách dự án đang dùng;
không cần gắn thêm component hay prefab bằng tay vào scene.

## Những phần đã đổi

- Menu và bảng trạng thái dùng Canvas World Space, có thể chạm bằng tay.
- Cả ba chế độ ghép cạnh dùng lại graph, loại cạnh, số lượng, hàm lắp và
  kết quả kiểm tra hiện có. Khay chứa cạnh 3D và cạnh đang cầm có độ dày.
- Thả hình có bảng gồm bốn ô khoét xuyên mesh, có thành và mặt sau tối;
  bốn khối màu là mesh 3D, thay cho sprite kéo trên màn hình.
- Mô hình, khay và bảng được đặt cùng một khu vực trước camera. Nếu đã thấy
  tay khi ĐẶT LẠI, khung được đặt sau vị trí tay nghỉ khoảng 15 cm để với tới;
  khi chưa có tay, khoảng cách mặc định là 0,75 m. Chúng giữ vị trí khi camera
  di chuyển và được đặt lại sau khi ARCore mất rồi khôi phục tracking.
  Đây là một Transform trong
  hệ tọa độ AR hiện tại, chưa phải ARAnchor lưu hoặc khôi phục qua phiên.
- Không thêm nút hay tính năng Gợi ý. Thông báo thao tác, kết quả và tiến độ
  cũ vẫn được giữ.
- Chuột/cảm ứng tiếp tục dùng được. Khi mất nhận diện tay hoặc Wi-Fi,
  vật còn đang cầm trở về khay; yêu cầu đã gửi Role 2 vẫn chờ kết quả như cũ.

## Cách dùng khi thử bản mới

1. Laptop chạy duy nhất `Start_SmartGeoAR_Laptop.bat`. Launcher dùng
   `--camera-source phone`; backend mặc định cũng chọn nguồn này và không mở webcam.
   Khởi động lại backend cũ sau khi cập nhật code.
2. Laptop và điện thoại cùng Wi-Fi. HOST trong `HandDataReceiver.cs` vẫn
   là `192.168.1.9` (Wi-Fi laptop ngày 11/11/2026); không đổi cổng 5053.
   Nếu IP laptop đổi, cập nhật HOST rồi build lại APK.
3. Hướng **camera sau điện thoại** vào phòng có ánh sáng và lia nhẹ để ARCore
   tracking. Đưa bàn tay ra phía trước camera sau, trong cùng góc nhìn với
   mô hình. Android không cần nút **CĂN TAY** hay webcam laptop.
4. Chạm nút bằng vị trí đầu ngón trỏ. Chụm ngón cái và trỏ khi ở gần
   cạnh/khối để cầm; di chuyển và mở tay để thả. Giữ tay trong ảnh camera sau.
   Có thể dùng cảm ứng màn hình để vào mode trong lúc thử nhận diện tay.
5. Giữ tay mở ở vị trí nghỉ trong ảnh camera sau rồi bấm **ĐẶT LẠI** bằng
   cảm ứng: khung sẽ ở sau tay khoảng 15 cm để với tới. **GẦN HƠN / XA HƠN**
   dịch khu vực từng 10 cm; khoảng điều chỉnh 0,35–1,2 m.
   Nếu tay tạm ra ngoài ảnh lúc bấm, khung chờ tay xuất hiện lại để đặt.
   **LÀM LẠI** bắt đầu lại bài đang chơi, không di chuyển cả khu vực.
   Ba nút điều chỉnh đi theo hướng nhìn của camera để vẫn bấm được khi bảng
   ra ngoài tầm nhìn; mô hình, khay và bảng vẫn giữ trong phòng.
6. Con trỏ vàng khi chưa chạm vật, xanh lam khi đã chạm vật có thể cầm,
   xanh ngọc khi chụm tay. Con trỏ luôn được vẽ rõ, nhưng việc cầm vẫn phải
   đạt khoảng cách 3D tới collider như logic cũ.

## Độ sâu và che khuất

`HandOcclusionProxy` dựng lớp chỉ ghi độ sâu từ 21 mốc tay do Role 1 trả về.
Lớp này giữ phần ảnh camera ở vị trí bàn tay gần hơn mô hình; không tô màu
hay vẽ bàn tay giả. Pose camera lấy đúng lúc chụp, không phụ thuộc tỉ lệ khung.
Chỉ dùng kết quả ảnh dưới 0,25 giây; mất cập nhật 0,2 giây thì ẩn lớp tay.
Đây là đường bao xấp xỉ theo khớp, chưa phải phân đoạn chính xác từng pixel;
không bao gồm cẳng tay. Độ sâu tay vẫn là ước lượng PnP dùng chung với thao tác.

Environment Depth của ARCore mặc định tắt trong scene và `PhoneDepthOcclusion`:
log máy thử có lỗi native khi tạo độ sâu, dù đã có texture và bật shader.
Không dùng dấu hiệu có texture để kết luận che khuất đã hoạt động đúng.

Các nút phục hồi và chấm chỉ tay dùng camera Overlay riêng, layer 30,
để vẫn thấy khi mô hình hoặc vật thật che vùng thao tác. Hình học và thao tác
cầm/thả vẫn ở không gian AR; không đổi gói kiểm tra Role 2.

Khung chờ 15 frame tracking ổn định trước khi cố định vị trí. Khi chỉnh khoảng
cách theo tay nghỉ, kích thước bố cục cũng đổi để vừa màn hình; hai nút
GẦN HƠN/XA HƠN sau đó chỉ dịch khung, giữ kích thước đang dùng.

## Luồng camera sau

`ARCore camera sau → Unity → TCP 5053 → Role 1 MediaPipe trên laptop → Unity`.
Role 2 tiếp tục kiểm tra graph và thao tác thả hình trên cùng kết nối.

`PhoneCameraSender` lấy riêng nền ARCore qua material của `ARCameraBackground`,
không chụp mô hình hay giao diện vào ảnh nhận diện. Ảnh JPEG có chiều ngang
640 pixel, cùng hướng/crop với màn hình. Nhịp gửi tối đa 15 ảnh/giây, chờ
kết quả ảnh đang xử lý để hạn chế hàng đợi. Backend cũng chỉ giữ ảnh mới nhất.
Cách đọc nền GPU dựa trên [mẫu của Unity AR Foundation](https://docs.unity.cn/Packages/com.unity.xr.arfoundation@6.6/manual/features/camera/image-capture.html).

Gói mới từ điện thoại là `type=phone_camera_frame`, kèm `frame_id`, `width`,
`height`, `jpeg` base64 và `intrinsics` (`fx`, `fy`, `cx`, `cy`). Intrinsics
được tính từ projection của camera AR theo kích thước ảnh đã gửi.
`PhoneMenuCamera` dùng lại ARMain dưới dạng additive để menu Android cũng
nhìn thấy camera sau; khi vào bài học, luồng tải ARMain hiện có vẫn được dùng.

## Dữ liệu Role 1

Các trường `interaction.x/y`, `gesture`, `pinch_ratio`, `coordinate_system`
và các gói Role 2 giữ nguyên. Mỗi tay có thể có thêm trường tùy chọn:

```json
{
  "spatial": {
    "valid": true,
    "coordinate_system": "phone_camera_m",
    "camera_frame_id": 42,
    "estimated": true,
    "point": {"x": 0.02, "y": 0.01, "z": 0.55},
    "index_tip": {"x": 0.03, "y": 0.04, "z": 0.54}
  }
}
```

`point` là giữa đầu ngón cái và trỏ; `index_tip` là đầu ngón trỏ. Trục x
hướng phải, y lên trên, z ra trước camera sau; ảnh điện thoại không lật gương.
`hand_pose.py` dùng MediaPipe world landmarks và OpenCV solvePnP để ước lượng
vị trí so với camera; không dùng trực tiếp z của landmark làm khoảng cách.
Gói tay từ nguồn điện thoại có `camera_source=phone_rear`. Unity dùng pose
camera AR lưu tại thời điểm chụp `camera_frame_id` để đưa tọa độ tay vào
phòng, không dùng pose lúc Wi-Fi trả kết quả và không ánh xạ qua mốc webcam.
Kết quả quá 0,65 giây hoặc không còn pose camera tương ứng bị loại bỏ.

Riêng nguồn điện thoại có thêm `spatial.landmarks`: 21 điểm `{x,y,z}` cùng
hệ tọa độ và độ sâu với `point/index_tip`, dùng cho lớp che khuất bàn tay.
Nhánh webcam không thêm mảng này; các trường cũ và JSON Role 2 giữ nguyên.
Quyền giữ vật chỉ khóa sau khi cầm được; `PINCH_HOLD` có thể phục hồi khi
mất gói bắt đầu, `OPEN` có thể phục hồi khi mất gói kết thúc. Gói thả dùng
vị trí hiện tại để kiểm tra, không dùng lại vị trí của gói giữ trước đó.

Với camera sau, độ sâu vẫn do PnP ước lượng nhưng x/y của đầu ngón được
đưa về đúng tia nhìn của landmark trong ảnh. Role 1 ghép vị trí lòng bàn tay
giữa các ảnh để nhãn Left/Right thay đổi không làm đổi ID thao tác ngay lập tức.
Unity lọc rung sau khi chuyển sang tọa độ AR: tăng độ đáp ứng khi tay di chuyển,
đòi thêm một mẫu xác nhận với cú nhảy lớn và nội suy con trỏ giữa các gói.
Khoảng mất tay ngắn dưới 0,3 giây giữ hình con trỏ tại vị trí cuối, không phát
lại cử chỉ thả. Mất lâu hơn hủy thao tác và ẩn con trỏ.

Khi pose không hợp lệ hoặc backend đang giữ gói giả do mất tay, `valid` là
false và thao tác cầm bị hủy. Backend cũ không có trường `spatial` vẫn có
thể điều khiển con trỏ trên mặt phẳng làm việc ở Editor; chế độ này không có
độ sâu. Android chỉ nhận luồng tay có nguồn `phone_rear`.

Nhánh webcam cũ được giữ cho Unity Editor/Windows qua `--camera-source webcam`;
tọa độ nhánh này vẫn là `mirrored_camera_m`, có hiệu chỉnh mốc riêng.

Gói `shape_drop_request` giữ các trường cũ. Unity tính `offset_x/y` trong
hệ tọa độ của ô và kiểm tra khoảng cách tới mặt bảng trước khi gửi thao tác
thả có độ sâu. Role 2 tiếp tục quyết định hình, góc và sai lệch có hợp lệ.

## Thử kính kẹp điện thoại

Kính thử hiện là loại VR Box/Cardboard, camera sau nhìn ra ngoài; chưa có
tên model hay mã QR. `PhoneHeadsetDisplay` giữ ARCore làm nguồn camera/pose,
render mô hình riêng cho hai mắt và ghép nền camera đơn mắt. Không đổi XR
loader, không đổi luồng MediaPipe laptop hoặc JSON Role 2.

1. Chọn mode bằng cảm ứng trước khi lắp điện thoại vào kính. Giữ camera sau
   hướng vào phòng, bấm ĐẶT LẠI để đưa khung về trước mặt.
2. Bấm **KÍNH VR** ở góc trên phải; kiểm tra màn hình đã chia hai ảnh rồi
   lắp điện thoại nằm ngang vào kính, để hở camera sau.
3. Thao tác bằng tay trong vùng camera nhìn thấy. Nút KÍNH VR có thể dùng
   lại để thoát; phím Back/Escape cũng thoát. Kéo bằng cảm ứng/chuột bị tắt
   trong chế độ kính để không dùng nhầm tọa độ màn hình của một mắt.
4. Thông số tròng kính hiện là cấu hình thử, chưa hiệu chỉnh theo QR/model.
   Cần kiểm tra ghép ảnh, độ rõ và vị trí khung với kính thật.

Hai mắt có độ lệch của **mô hình ảo**; nền thật vẫn là một ảnh camera, chưa
có độ sâu hai mắt. Lớp che tay dùng cùng tia ảnh nền ở hai mắt, còn kiểm tra
cầm vật vẫn dùng vị trí 3D. Nguồn ảnh gửi laptop giữ projection gốc, không
gửi hình chia đôi hoặc hình đã uốn cho tròng kính.

## Giới hạn cần hiệu chỉnh trên thiết bị

- Chế độ kính là bản thử stereo dùng tư thế ARCore, chưa tích hợp Cardboard
  XR hay cấu hình QR tròng kính. Nền camera đơn mắt trên Android vẫn được giữ.
- Camera đơn mắt và MediaPipe cho độ sâu ước lượng theo bàn tay. Việc dùng
  intrinsics ARCore và cùng góc nhìn loại bỏ bước ghép hai camera, nhưng
  không biến MediaPipe thành cảm biến đo khoảng cách chính xác.
- Độ trễ Wi-Fi, ánh sáng, che khuất các ngón và kích thước bàn tay có thể
  làm con trỏ lệch/rung. Cần thử trực tiếp việc chạm, cầm và thả ở độ sâu thật.
- ARCore Depth phụ thuộc cảnh và thiết bị; biên ngón tay gần camera cần được
  đối chiếu trên máy. Google ghi độ sâu chính xác nhất khoảng 0,5–5 m trong
  [tài liệu Depth API](https://developers.google.com/ar/develop/depth).
- `movementScale`, `invertDepth` và mốc **CĂN TAY** chỉ còn dùng cho nhánh
  webcam Editor/Windows, không áp dụng lên tọa độ camera sau điện thoại.

## Thử nguồn camera sau — 09/11/2026

Đã build/cài APK camera sau lên điện thoại Xiaomi 2412DPC0AG, Android 15.
Người dùng xác nhận thấy bàn tay thật và con trỏ; sau bản lọc mới,
con trỏ ổn hơn rõ, ít rung và ít mất nhận diện. Còn cần kiểm tra cầm/thả khối
và lắp cạnh bằng tay thật trên nguồn camera sau. Kết quả cảm ứng/webcam cũ
bên dưới không thay thế kiểm tra này.

## Kết quả thử trước khi đổi nguồn tay — 07/11/2026

- Đã build bằng Unity 6000.6.0f1 và cài APK lên Xiaomi 2412DPC0AG, Android 15.
- Qua thao tác cảm ứng do ADB gửi trên máy thật: lập phương 12/12 cạnh,
  hộp chữ nhật 12/12, chóp 8/8, thả hình 4/4; backend Wi-Fi xác nhận đúng.
  Thả sai hình, chọn sai loại thanh, làm lại và kết nối lại cũng đã kiểm tra.
- Đọc cảm ứng qua `EnhancedTouch`, giữ một touch ID tới lúc nhả/hủy.
  Cách này tránh mất chuyển trạng thái khi đọc `Touchscreen` trong `Update`;
  xem [tài liệu Unity Input System](https://github.com/Unity-Technologies/InputSystem/blob/develop/Packages/com.unity.inputsystem/Documentation~/touch-polling.md).
- Python đạt 8/8 tests Role 2 và 13/13 kiểm tra pose/JSON. Có nhận pose hợp lệ
  từ webcam thật, nhưng chưa xác nhận hoàn tất thao tác gắp/thả bằng tay.
- Trong lượt cuối, cả camera gốc điện thoại cũng cho ảnh rất tối/đen và
  ARCore chưa tracking. Cần hướng máy vào phòng sáng và thử lại việc giữ
  bảng trong phòng, hiệu chỉnh tay và chạm khối theo độ sâu.

Các kết quả trên thuộc bản dùng webcam/cảm ứng trước đây, chưa xác nhận
luồng nhận tay camera sau mới. Chi tiết và kết quả cập nhật nằm trong
[DEVICE_TEST_REPORT.md](DEVICE_TEST_REPORT.md).
