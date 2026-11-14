# SmartGeoAR — kiểm tra trên điện thoại

## Chuẩn bị thử kính VR Box/Cardboard — 11/11/2026

Người dùng xác nhận kính kẹp điện thoại và camera sau nhìn ra ngoài được;
chưa có tên model hay mã QR. Điện thoại tiếp tục là POCO X7 Pro.

- Thêm chế độ KÍNH VR: render mô hình riêng cho hai mắt, ghép nền camera sau
  đơn mắt, giữ ARCore làm nguồn pose/camera và giữ backend Python qua Wi-Fi.
- Có nút vào/thoát kính; tắt kéo cảm ứng/chuột khi đang nhìn hai mắt. Các mode
  hình học dùng chung mesh/graph/JSON cũ. Thông số tròng kính là cấu hình thử.
- Tắt cả component AROcclusionManager khi không dùng Environment Depth để
  ARBackgroundRendererFeature không yêu cầu prepass độ sâu không cần thiết.
- Thêm thống kê lớp tay và cờ launch `smartgeoar_hand_mask_debug` cho bản
  Development; chỉ cờ debug mới tô xanh mesh để đối chiếu vị trí trên máy.
- Kết quả kiểm tra/build và thử kính thật sẽ cập nhật sau; chưa đánh dấu đạt.
- GPU D3D11: 63/63 kiểm tra đạt (35 kiểm tra tay/scene và 28 kiểm tra kính).
  Có render hai mắt thật với độ lệch ngang, không lệch dọc; kiểm tra alpha,
  ghép camera theo projection, tâm tròng kính, méo ảnh và thoát khôi phục camera.
  Đã lưu PNG và `verification-source.cs.txt`. Kết quả này chưa thay thế thử
  GLES3/ARCore và ghép ảnh qua tròng kính trên điện thoại.
- Log: [Headset-2026-11-11](Logs/Headset-2026-11-11).

## Sửa che khuất tay và cầm vật — 11/11/2026

Ảnh người dùng xác nhận tay thật vẫn sau khung, con trỏ đã nổi trước nhưng
chưa cầm được. Bản liên kết depth trước đó đã cài thành công (SHA-256
`B930FD9538CFFC6BBDB81B35B318667F827EED41A28F4C51C99F1950CA3C73CD`),
nhưng không đạt thử tay thật. Log native ARCore báo lỗi tạo độ sâu.

- Thêm lớp độ sâu bàn tay từ 21 landmark cùng PnP/capture pose với tương tác;
  mặc định tắt Environment Depth gây lỗi trên máy thử. Lớp này xấp xỉ đường
  bao bàn tay, không phân đoạn từng pixel và không bao gồm cẳng tay.
- Chỉ khóa tay sau khi cầm được; nhận HOLD nếu mất START, nhận OPEN nếu mất END;
  kiểm tra vị trí thả hiện tại. Giữ khoảng chạm 3,5 cm và JSON Role 2.
- ADB đã nhận điện thoại. Wi-Fi laptop hiện `192.168.1.9`; cập nhật HOST trong APK.
- Python 5/5 kiểm tra đạt: 21 mốc khớp tia ảnh, độ sâu chung với điểm cầm,
  tương thích webcam, loại dữ liệu lỗi, phát lại ảnh camera sau có bàn tay đầy đủ.
  Ảnh tay bị cắt ở lượt Reach không được detector nhận độc lập; ảnh đầy đủ
  được dùng để kiểm tra đường dữ liệu, không thay thế thử tay trực tiếp.
- Log mới: [HandProxy-2026-11-11](Logs/HandProxy-2026-11-11).
- Unity 32/32 kiểm tra đạt trên GPU Direct3D11: camera URP thật giữ ảnh nền
  ở lòng bàn tay/ngón gần hơn bảng, giữ bảng ở khe ngón và khi bảng gần hơn tay;
  kiểm tra khóa tay, HOLD/OPEN và vị trí thả hiện tại cho hai nhóm mode.
  Ảnh `gpu-hand-before-board.png` dùng nền xám để mô phỏng ảnh camera;
  đây là kiểm tra lớp vẽ, không phải ảnh bàn tay thật trên điện thoại.
- Chưa kết luận tay thật che được khung hoặc cầm/thả đạt cho đến khi thử bản mới.
- Người dùng đã thử APK `9020FE3E...` và báo **tay vẫn bị khung che**.
  Log từng có `Hand depth proxy active`, nhưng đây chỉ là log một lần, chưa
  chứng minh lớp tay còn hiện đúng ở mỗi frame. Bổ sung bộ đếm pose/độ trễ
  và tùy chọn debug màu để đo trên bản tiếp theo; không đánh dấu che khuất đạt.
- Build Android thành công, Unity thoát mã 0; đã cài bằng `adb install -r`
  và mở trên điện thoại. APK SHA-256:
  `9020FE3E97301680F481F006F6F32DBCEE25ABC9040E9522BA568F65501250BD`.
- Log máy xác nhận `Backend CONNECTED`, `Backend ready`, gửi ảnh camera sau
  640×288; TCP từ điện thoại `192.168.1.15` tới laptop `192.168.1.9:5053`.
  ARCore đạt `SessionTracking`. Chưa quan sát lại lỗi native tạo độ sâu ở log đầu.
- Đã gỡ helper Editor và file performance tự sinh, khôi phục đúng ba RID URP
  bị Unity tự bỏ trong lúc build; giữ bản sao harness trong thư mục log.

## Chuyển nguồn nhận tay sang camera sau — 08/11/2026

Backend và launcher hiện mặc định `--camera-source phone`. Ảnh camera sau
ARCore được Unity gửi sang Role 1 trên laptop; tọa độ tay được đưa về không
gian AR theo pose camera lúc chụp. Không mở webcam laptop trong luồng này.
Role 2 giữ logic và các gói kiểm tra cũ. Android bỏ nút CĂN TAY dùng cho webcam.

- Python: 8/8 kiểm tra intrinsics, trục tọa độ, không trộn pose giữa hai ảnh,
  ảnh lỗi, hàng đợi ảnh mới nhất, giới hạn gói và xử lý Role 2 trên cùng TCP.
  Kiểm tra worker khóa `cv2.VideoCapture` để xác nhận không gọi webcam.
- Role 2: 8/8 tests hiện có vẫn đạt.
- Unity: 8/8 kiểm tra projection, pose lúc chụp, đầu ngón trỏ, loại kết quả
  cũ/không có pose và tương thích JSON con trỏ cũ.
- Log tại [Logs/PhoneRearCamera-2026-11-08](Logs/PhoneRearCamera-2026-11-08).
- Build Unity 6000.6.0f1 thành công, cài APK camera sau đầu tiên lên Xiaomi
  2412DPC0AG bằng `adb install -r`. SHA-256 của bản đầu tiên:
  `291815AE6306E8322128136C6E9DB64F040C44AB08431B71B0196C0781BC9797`.
- Menu đã hiển thị nền camera sau thật. Đối chiếu ảnh màn hình
  `01-menu-rear-camera.png` với JPEG `first-phone-camera.jpg`: cùng hướng/crop,
  JPEG không chứa nút hay mô hình ảo. Không có nút CĂN TAY trên Android.
- Điện thoại `192.168.1.15` kết nối Wi-Fi trực tiếp tới `192.168.1.16:5053`.
  Quan sát 403 phản hồi ảnh đầu: khoảng 7,55 ảnh/giây, trung vị thời gian
  xử lý trên laptop 16 ms (không phải tổng độ trễ tới màn hình).
  ARCore đạt `SessionTracking`; chưa có tay trong các ảnh này.
- Chưa xác nhận con trỏ trùng tay thật, chạm/cầm/thả bằng camera sau.
  Các kết quả cảm ứng ở mục lịch sử không thay thế kiểm tra đó.

### Phản hồi về rung và mất con trỏ — cập nhật 09/11/2026

Người dùng đã thấy bàn tay thật và con trỏ từ camera sau, nhưng báo rung mạnh,
bám kém và mất con trỏ liên tục. Đây là lỗi được ghi nhận ở APK camera sau đầu tiên.

- Bổ sung bám pixel đầu ngón, giữ ID theo lòng bàn tay, lọc pose trong hệ AR,
  loại cú nhảy đơn lẻ và giữ con trỏ qua khoảng mất ảnh ngắn 0,3 giây.
  Nhịp gửi tối đa tăng từ 10 lên 15 ảnh/giây; vẫn chỉ một ảnh đang chờ xử lý.
- Đo khoảng cách chụm tay theo pixel để ảnh ngang không làm x/y khác tỉ lệ;
  các ngưỡng và xác nhận nhiều khung của cử chỉ cũ được giữ.
- Python cập nhật: 13/13 kiểm tra luồng camera, pixel đầu ngón, ID tay và
  tỉ lệ chụm tay khi xoay/đổi aspect ảnh;
  8/8 tests Role 2. Native Python exit code xác nhận là 0.
- Unity cập nhật: 8/8 kiểm tra camera và 6/6 kiểm tra bộ lọc. Với nhiễu tổng hợp,
  RMS sau lọc bằng 0,381 lần đầu vào; đây không phải số đo giảm rung trên người thật.
- Chạy lại MediaPipe trên một JPEG camera sau thật đã lưu: sai số chiếu đầu
  ngón trong ảnh 640 pixel giảm từ 18,687 pixel (PnP cũ) xuống 0,0094 pixel
  (ràng buộc tia nhìn). Kết quả này không đo độ chính xác độ sâu hay tổng độ trễ.
- Bản giảm rung đã build thành công và cài bằng `adb install -r` ngày
  09/11/2026. Đường dẫn APK dùng chung giữa các lượt build:
  [Builds/Android/SmartGeoAR-SpatialDeviceTest.apk](Builds/Android/SmartGeoAR-SpatialDeviceTest.apk).
  SHA-256 của bản giảm rung ngày 09/11:
  `C32D7A2BF8D7293B3D326F534678FF9284D27A5EEDCB537B5FDFE13914A7F03E`.
- Người dùng đã thử tay thật qua camera sau trên bản giảm rung và xác nhận:
  **“Ổn hơn rõ, ít rung và ít mất.”** Đây là kết quả quan sát của người dùng,
  chưa phải số đo độ chính xác không gian hoặc tổng độ trễ.
- Log sau lượt thử ghi nhận ít nhất 463 mẫu pose tay hợp lệ từ `phone_rear`.
  Trong 669 ảnh đầu của APK mới chưa có tay; luồng ảnh đạt khoảng 9,56 ảnh/giây.
  Trace cho lượt đối chiếu được lưu riêng tại
  [Stability-2026-11-09](Logs/PhoneRearCamera-2026-11-08/Stability-2026-11-09).
- Còn cần thử cầm khối, thả khớp lỗ và lắp cạnh bằng nguồn camera sau trên bản này.

### Khung, độ sâu và che khuất — 09/11/2026

Người dùng vào THẢ HÌNH, thấy con trỏ nhưng chưa cầm được; xác nhận cả chấm
con trỏ và ảnh tay thật đều bị khối 3D che. Chưa có log `Hand grab` hay phản hồi
thả hình Role 2 cho lượt camera sau này. Không đánh dấu thao tác cầm/thả là đạt.

Log có lần ĐẶT LẠI lúc `SessionInitializing`, nên vị trí cũ có thể bị lệch khi
ARCore khôi phục pose. Mẫu tay gần lượt thử có độ sâu ước lượng khoảng 0,21–0,34 m.

- Chờ 15 frame tracking và đặt lại khung sau khi tracking khôi phục.
- ĐẶT LẠI có tay hợp lệ: đặt khung sau tay nghỉ khoảng 15 cm; bổ sung nút
  GẦN HƠN/XA HƠN điều chỉnh từng 10 cm, giới hạn 0,35–1,2 m.
- Bật môi trường độ sâu ARCore, giữ nút phục hồi và con trỏ bằng camera Overlay.
  Điện thoại trả tên `POCO X7 Pro`; Google liệt kê mẫu này
  [hỗ trợ Depth API](https://developers.google.com/ar/devices).
- Con trỏ đổi màu khi chạm collider. Log Development bổ sung khoảng cách
  tay–khối khi chưa cầm được, giữ nguyên bán kính cầm và JSON Role 2.
- 20/20 kiểm tra Unity: 8 camera, 6 lọc rung, 6 vị trí nghỉ/với tay và collider.
- Đã build/cài bản khung–độ sâu đầu tiên, SHA-256
  `F6353DE33F702B05DF9246CB7F6C6193D09FF6231EA4D28B5B355F1A3B174CDE`.
  Máy trả `ARCore depth ready: 160x90`, ba nút điều chỉnh hiện đúng.
- Người dùng thử và báo **tay vẫn bị khung che**. Video `hand-reach-depth.mp4`
  và các frame trích xác nhận ảnh tay thật vẫn nằm sau mô hình; chấm con trỏ
  đã hiện rõ phía trước. Chưa cầm được khối.
- Log nút XA HƠN tăng khung tới 1,2 m; tay ở khoảng 0,15–0,30 m, khoảng cách
  tới collider khoảng 0,93–1,01 m. Tay ở trước theo tọa độ nhưng lớp hiển thị
  chưa che khuất đúng; đẩy khung ra xa không giải quyết phần vẽ này.
- Phát hiện `ARCameraBackground.Awake` chỉ lấy `AROcclusionManager` một lần.
  Việc thêm manager sau khi scene mở làm background giữ tham chiếu null,
  dù manager mới vẫn sinh được ảnh độ sâu.
- Sửa `Assets/Scenes/ARMain.unity` để manager có sẵn trước Awake; bổ sung
  log xác nhận shader nền thực sự bật độ sâu, ngoài log có ảnh độ sâu.
  ĐẶT LẠI chờ tay xuất hiện lại khi thao tác bấm màn hình làm mất ảnh tay.
- 23/23 kiểm tra Unity đạt, gồm 3 kiểm tra scene và tham chiếu trong Awake.
  Bản sửa liên kết đã build/cài nhưng người dùng vẫn báo tay bị khung che;
  xem lượt sửa 11/11/2026 ở đầu báo cáo.
- Bằng chứng và log build: [Reach-2026-11-09](Logs/PhoneRearCamera-2026-11-08/Reach-2026-11-09).

## Lượt thử lịch sử — 07/11/2026

Các kết quả và SHA-256 bên dưới thuộc bản webcam/cảm ứng đã thử trước đây,
không đại diện cho APK camera sau mới.

## Thiết bị và bản cài

- Điện thoại Xiaomi 2412DPC0AG (rodin), Android 15, ARM64, nối USB để dùng ADB.
- Unity 6000.6.0f1; build Android Development thành công, đã cài bằng `adb install -r`.
- APK: [Builds/Android/SmartGeoAR-SpatialDeviceTest.apk](Builds/Android/SmartGeoAR-SpatialDeviceTest.apk).
- SHA-256: `B6BA0B9EC68597EC733622BF5EFA5A6B5C3D5F0AE660356B95A18415CF3DB19B`.
- Backend Python gộp Role 1 + Role 2: `192.168.1.16:5053`, bind `0.0.0.0`,
  `--no-camera-stream`. Điện thoại `192.168.1.15` kết nối trực tiếp qua Wi-Fi;
  không dùng `adb reverse` hay backend giả để kiểm tra trò chơi.

## Kết quả đã quan sát

Thao tác chạm/kéo dưới đây được ADB gửi vào ứng dụng trên điện thoại thật.
Chúng kiểm tra đường cảm ứng của Unity, gửi JSON qua Wi-Fi, xử lý Python
và cập nhật kết quả trên màn hình; chúng không thay thế thử bàn tay thật.

| Phần kiểm tra | Kết quả |
| --- | --- |
| Menu và hướng dẫn | Hiện đủ 4 mode, nhãn nút đúng vị trí, BẮT ĐẦU mở được ARMain |
| Lập phương | Lắp đủ 12/12; Role 2 trả `status=valid`, `is_correct=true`, `edge_count=12`; UI hiện CHÍNH XÁC |
| Hộp chữ nhật | Lắp đủ 12/12 với ba loại thanh; Role 2 xác nhận hợp lệ, UI hiện CHÍNH XÁC |
| Chọn sai loại thanh | Unity từ chối, giữ nguyên số lượng cạnh, hiện thông báo sai loại thanh |
| Chóp tứ giác đều | Lắp đủ 8/8 với cạnh đáy/cạnh bên; Role 2 xác nhận hợp lệ, UI hiện CHÍNH XÁC |
| Thả sai hình | Vuông vào lỗ tam giác: Role 2 trả `wrong_shape`, tiến độ vẫn 0/4, khối trở về khay |
| Thả đủ bốn hình | Bốn phản hồi đúng lần lượt tăng `completed_count` 1, 2, 3, 4; UI hiện hoàn thành 4/4 |
| LÀM LẠI | Lập phương về 0/12; thả hình về 0/4, hiện lại đủ khối/nhãn/lỗ |
| Phiên mới sau làm lại | `session_id` mới, `request_id=1` được kiểm tra lại; kết quả đầu tiên là 1/4, không dùng kết quả 4/4 của phiên trước |
| Backend khởi động lại | App tự kết nối lại và nhận `Backend ready`; thử thả vuông sau kết nối lại đạt 1/4 |
| Python | 8/8 tests Role 2; 13/13 kiểm tra PnP, tọa độ, JSON cũ, dữ liệu không hợp lệ và mất tay |

## Những lỗi đã sửa khi thử máy

- Đặt workspace sau khi camera AR có pose tracking ổn định, tránh đặt theo
  pose camera lưu trong scene rồi bị lệch khỏi tầm nhìn khi ARCore khởi tạo.
- Giữ nhãn và vùng bấm của nút trong panel đang ẩn: tìm parent Button với
  `includeInactive=true`, bố trí lại panel và nhãn theo Canvas World Space.
- Dùng `EnhancedTouch` khi đọc trong `Update`, cân bằng Enable/Disable và giữ
  touch ID tới khi kết thúc; hủy thao tác khi mất touch, ưu tiên touch trước chuột.
  Các hàm bắt đầu/di chuyển/thả và hợp đồng JSON Role 2 được giữ.
- Đưa nền khay thả hình ra phía sau bảng để tránh hai bề mặt cùng độ sâu.
- Log tương tác chỉ có trong Development Build/Unity Editor để đối chiếu thao tác.

## Còn cần thao tác thực tế

- Lượt cuối camera sau đang cho ảnh rất tối/đen. Đã mở camera gốc để đối
  chiếu: preview gốc cũng tối/đen. Log ARCore báo `NotTracking` và thiếu
  đặc trưng/độ sâu; chưa xác nhận giữ bảng ổn định trong phòng ở APK cuối.
  Cần nhấc máy, hướng camera vào phòng sáng có đồ vật và lia nhẹ để thử tiếp.
- Trong luồng webcam thật đã ghi nhận 56 mẫu pose hợp lệ và 22 mẫu không hợp
  lệ trước khi dừng ghi trace. Có dữ liệu không đồng nghĩa đã xác nhận thao tác:
  chưa hoàn tất kiểm tra CĂN TAY, gắp khối/cạnh và nhả đúng vị trí bằng bàn tay.
- Chưa thử mất Wi-Fi giữa một yêu cầu đang chờ phản hồi, độ chính xác chạm
  trong không gian hoặc nhiều loại điện thoại. Việc tự gửi lại cùng request ID
  vẫn cần thử riêng trong tình huống mất phản hồi.
- Bản này dùng ARCore và không gian 3D; chưa có stereo hai mắt/Cardboard XR.
- Log còn thông báo Unity Services TLS, Play Asset Delivery class không có và
  ARCore không tìm được độ sâu trong cảnh tối. Không quan sát crash hay
  `NullReferenceException`/`IndexOutOfRangeException` của luồng trò chơi đã thử.

## Bằng chứng trên máy

Thư mục: [Logs/SpatialDeviceTest-2026-11-06T16-15-12-567Z](Logs/SpatialDeviceTest-2026-11-06T16-15-12-567Z).

- `Unity-build-fix2.log`: build APK cuối thành công.
- `python-checks.log`: kết quả 8 + 13 kiểm tra Python.
- `backend-trace-fix2.log`: thống kê hand stream và JSON kết quả Role 2 thực
  sự gửi tới điện thoại (`delivered=true`). Wrapper ghi trace chỉ chạy trong
  bộ nhớ khi test; sau đó đã thay bằng backend bình thường.
- `phone-final-unity.log`, `phone-final-errors.log`: log ứng dụng trên điện thoại.
- Ảnh `15-cube-complete-fix2.png`, `19-prism-complete-fix2.png`,
  `21-pyramid-complete-fix2.png`, `24-drop-complete-fix2.png`: bốn mode hoàn thành.
- Ảnh `16-cube-reset-fix2.png`, `18-prism-wrong-rod-fix2.png`,
  `23-drop-wrong-shape-fix2.png`, `25-drop-reset-fix2.png`,
  `28-drop-after-reconnect.png`: reset, từ chối và kết nối lại.
- `27-stock-camera-preview.png`: ảnh tối từ camera gốc để đối chiếu.

Không sửa Scene YAML, không đổi logic Role 2. File Editor phục vụ build tạm
đã gỡ; thay đổi URP do Unity tự ghi lúc build đã được khôi phục. Các thay đổi
có sẵn và những thư mục untracked được yêu cầu giữ nguyên vẫn được giữ.
