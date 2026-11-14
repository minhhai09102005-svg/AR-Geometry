# Báo cáo dọn dẹp SmartGeoAR — Windows PC

Ngày thực hiện: 14/11/2026 (Asia/Saigon).

Đã xóa **24 file, 51.443 byte**, gồm 2 demo Python, 9 tài nguyên/script Unity và 13 file `.meta` (bao gồm meta thư mục). Không sửa mã backend hoặc logic gameplay, giao diện, Scene, Prefab, cấu hình render hay package Unity. Build đang dùng và dữ liệu thực nghiệm được giữ nguyên theo SHA-256.

## 1. Phạm vi kiểm tra

- Kiểm kê 303 file nguồn/cấu hình/tài liệu/tài nguyên trước khi dọn; 15 Python, 25 C#, 2 Scene, 3 Prefab, 96 file tài nguyên Unity và 125 `.meta`. Sau dọn: 13 Python, 22 C#, 87 tài nguyên và 112 `.meta`.
- Đối chiếu GUID trong Assets/ProjectSettings với `.meta` của dự án và 32.442 GUID trong PackageCache, Packages và BuiltInPackages của Unity 6000.6.0f1.
- Phân tích AST import Python, tên lớp C#, khởi tạo qua `RuntimeInitializeOnLoadMethod`, `AddComponent`, `GetComponent`, `Resources.Load`, `Shader.Find`, `SceneManager.LoadScene`, tìm đường dẫn backend và khởi động tiến trình.
- Kiểm tra riêng build hiện hữu: 220 file, tổng 146.387.078 byte. Các thư mục Library/Temp/Logs/UserSettings và môi trường Python được xem là dữ liệu sinh tự động; không xóa chúng. Chỉ đọc phần cần thiết để kiểm tra dependency/compile.
- Không tạo branch, commit, push hoặc xóa `.git`. Thư mục dự án không có `.git` riêng; Git đang nhận repository tổ tiên ở `C:/Users/admin`. Không thực hiện thao tác Git ghi dữ liệu.

## 2. File đã xóa và lý do

| Nhóm | Lý do xác minh an toàn cho PC |
| --- | --- |
| `role1_hand_tracking/hand_tracking.py` | Demo webcam chạy độc lập, xuất ảnh/JSON; không có import hoặc launcher nào gọi. Backend dùng `Tracking` và các hàm từ `role1_week2_stream.py`. Giữ lại ảnh/JSON thực nghiệm ở `output/`. |
| `role1_hand_tracking/hand_gesture.py` | Demo cử chỉ độc lập, không có dependency đi vào từ mã còn giữ; không chứa bộ đo benchmark hay assertion kiểm thử. Gesture/hysteresis thực tế của backend nằm trong module được giữ. |
| `PhoneMenuCamera.cs` và `.meta` | Lớp static không được C# hay GUID Scene/Prefab khác tham chiếu. Có auto-initializer nhưng đăng ký sự kiện chỉ ở `UNITY_ANDROID && !UNITY_EDITOR`; Windows không thực hiện bootstrap này. Việc xóa dành riêng cho bản PC; không bảo đảm menu Android sau dọn. |
| `TutorialInfo/`, `TutorialInfo.meta`, `Readme.asset` và `.meta` | Bộ chào mừng URP mẫu. Các tham chiếu style/icon/script chỉ nằm trong chính nhóm này. Xóa cả ScriptableObject và editor của nó cùng lúc; không để Readme.asset trỏ đến script bị xóa. Không thuộc tài nguyên nạp bằng Resources của gameplay. |

Danh sách đầy đủ:

```text
role1_hand_tracking/hand_gesture.py
role1_hand_tracking/hand_tracking.py
role3_unity/Assets/Readme.asset
role3_unity/Assets/Readme.asset.meta
role3_unity/Assets/Scripts/Spatial/PhoneMenuCamera.cs
role3_unity/Assets/Scripts/Spatial/PhoneMenuCamera.cs.meta
role3_unity/Assets/TutorialInfo.meta
role3_unity/Assets/TutorialInfo/Editor.meta
role3_unity/Assets/TutorialInfo/Editor/ReadmeEditor.cs
role3_unity/Assets/TutorialInfo/Editor/ReadmeEditor.cs.meta
role3_unity/Assets/TutorialInfo/Icons.meta
role3_unity/Assets/TutorialInfo/Icons/URP.png
role3_unity/Assets/TutorialInfo/Icons/URP.png.meta
role3_unity/Assets/TutorialInfo/Layout.wlt
role3_unity/Assets/TutorialInfo/Layout.wlt.meta
role3_unity/Assets/TutorialInfo/Readme.cs
role3_unity/Assets/TutorialInfo/Readme.cs.meta
role3_unity/Assets/TutorialInfo/StyleSheets.meta
role3_unity/Assets/TutorialInfo/StyleSheets/ReadmeEditor.uss
role3_unity/Assets/TutorialInfo/StyleSheets/ReadmeEditor.uss.meta
role3_unity/Assets/TutorialInfo/StyleSheets/ReadmeEditorDark.uss
role3_unity/Assets/TutorialInfo/StyleSheets/ReadmeEditorDark.uss.meta
role3_unity/Assets/TutorialInfo/StyleSheets/ReadmeEditorLight.uss
role3_unity/Assets/TutorialInfo/StyleSheets/ReadmeEditorLight.uss.meta
```

Các thư mục TutorialInfo/Editor, Icons, StyleSheets và TutorialInfo được bỏ sau khi đã rỗng. Tất cả file xóa đã được sao lưu trước khi sửa tại:

`C:\Users\admin\AppData\Local\Temp\smartgeoar_pc_cleanup_20261114\removed_files_backup.zip`

Đây là bản sao ngoài dự án trong thư mục tạm Windows; không phải artifact runtime.

## 3. File được giữ dù có vẻ không dùng

| File/nhóm | Nguyên nhân giữ |
| --- | --- |
| `output/` (4 file) | Ảnh và JSON nhận diện bàn tay/marker có giá trị làm minh chứng báo cáo. Không có runtime PC đọc chúng, nhưng không coi dữ liệu thực nghiệm là rác. |
| `role1_hand_tracking/role1_week6_benchmark.py` và `test_results/` (5 CSV) | Bộ benchmark và số đo học thuật; giữ nguyên byte. Không chạy lại benchmark dài hoặc ghi đè số đo cũ. |
| `virtual_edge_test.py` | Thử nghiệm thủ công gắp/thả cạnh, khoảng cách điểm–đoạn thẳng và smoothing; còn giá trị kiểm thử thực hành, không phải dependency backend. Không import/chạy tự động vì script mở camera và vòng lặp GUI ở cấp module. |
| `udp_test_receiver.py` | Công cụ xem gói UDP 5052; cả benchmark và `role1_week2_stream.py` vẫn có luồng UDP độc lập. Có giá trị chẩn đoán dù bản PC dùng backend TCP 5053. Không chạy vòng chờ vô hạn. |
| `role1_week2_stream.py` | Backend import `CAMERA_INDEX`, `get_gesture`, `get_state`, `hand_states`, `make_hand_data`; cần cho trạng thái hai tay và pinch. |
| `phone_hand_tracking.py`, `hand_pose.py` | Backend import PhoneHandTracking ở cấp module ngay cả khi chọn webcam; module phone tiếp tục import HandPoseEstimator. Xóa sẽ làm import backend lỗi. |
| Toàn bộ Role 2, `test_reasoning_engine.py` | Kiểm tra hình học, schema, knowledge base và kiểm thử cần cho hai chế độ học. `main.py` là công cụ/entry point suy luận độc lập còn hữu ích. |
| `ARObjectPlacement.cs`, `CameraPermission.cs` | Cả hai gắn trong ARMain; ARObjectPlacement còn được ScanScreenController/ShapeDropFeature truy vấn để vô hiệu hóa. Không xóa chỉ vì chức năng AR không cần trên PC. |
| `Detection/Mock*.cs` | Cả 3 script được GUID trong ARMain tham chiếu; ScanScreenController có trường kiểu tương ứng. Xóa sẽ tạo Missing Script hoặc lỗi C#. |
| `PhoneCameraSender.cs` | SpatialWorkspace/HandDataReceiver gọi Ensure; các lớp mapping/occlusion/headset còn tham chiếu. Ensure giới hạn Android, nhưng kiểu và API vẫn cần để compile. |
| `PhoneDepthOcclusion.cs` | SpatialWorkspace và SpatialHandInteractor gọi GetComponent và các phương thức cấu hình UI/con trỏ. Không xóa lớp khi còn dependency C#. |
| `HandOcclusionProxy.cs`, `PhoneHeadsetDisplay.cs` | Còn được các script phone và harness Editor dùng. Các lớp UI, SpatialTouchInput còn đọc `PhoneHeadsetDisplay.IsActive`. Không thể xóa riêng lẻ mà giữ nguyên logic. |
| `HandPoseSmoother.cs` | Mapper còn nhánh dùng lớp này; có giá trị lọc pose và kiểm thử. |
| `Assets/Editor/SmartGeoARHeadsetBuild.cs` | Harness có kiểm tra mesh, shader, tương tác và hình học; giữ giá trị kiểm thử dù không thuộc đường chạy PC và chú thích cũ gọi nó là tạm. Compile được; không chạy Verify/Build Android trong đợt này. |
| `Assets/Resources/*` | SmartGeoAR_Unlit.mat được backend receiver/workspace/scan nạp bằng tên; 2 shader HandDepth/HeadsetComposite được các lớp còn giữ/harness nạp bằng Resources.Load. Không dùng thiếu tham chiếu GUID để suy ra có thể xóa. |
| `Assets/Prelabs/*`, UI PNG, InputSystem_Actions, TextMesh Pro | Giữ UI, prefab, input, font và shader, kể cả shader có chữ Mobile trong tên. Không đổi hình thức giao diện hoặc cơ chế tương tác. |
| `Assets/Settings/SampleSceneProfile.asset` | Tên có vẻ là mẫu nhưng cả PC_RPAsset và Mobile_RPAsset tham chiếu GUID của profile này. |
| XR assets/Resources, ARCore settings, Android profile, Mobile renderer/RP asset và package AR/XR | Có tham chiếu trong Scene/ProjectSettings, script C# và QualitySettings; muốn gỡ cần một đợt chuyển đổi cấu hình riêng. Không gỡ package, kiểu C# hay asset liên kết trong đợt dọn bảo thủ này. |
| Design/VR_Concepts, DEVICE_TEST_REPORT.md, README_Spatial.md | Tư liệu thiết kế/lịch sử kiểm thử; không đưa vào runtime PC. Giữ làm dữ liệu báo cáo. Nội dung nhắc PhoneMenuCamera trong README_Spatial là lịch sử Android, không phải dependency còn hoạt động. |
| Builds/, .venv-cv/, Library/, Temp/, Logs/, UserSettings/ | Giữ build đang dùng, môi trường chạy và cache Editor đang mở. |

## 4. Thay đổi phụ trợ

Đã sửa `.gitignore`: tách dòng lỗi `*.pyc**/Library/` thành quy tắc hợp lệ, bỏ quy tắc trùng và chuẩn hóa các thư mục Unity sinh tự động. Đã kiểm tra `.pyc`, Library, Builds và .venv-cv được ignore; benchmark và CSV vẫn không bị các quy tắc mới ignore.

Các file còn tồn tại có nội dung đổi so với snapshot đầu:

- `.gitignore`
- `role3_unity/Assembly-CSharp-Editor.csproj`
- `role3_unity/Assembly-CSharp.csproj`

Chỉ `.gitignore` được chỉnh tay. Các `.csproj`/`.slnx` nếu xuất hiện trong danh sách là Unity tự tái sinh sau refresh; không chỉnh project C# sinh tự động bằng tay.

## 5. Lỗi/tham chiếu có sẵn còn tồn tại

### Gameplay PC cần kiểm tra/sửa riêng

- **Hai tay ở backend độc lập, Unity còn sở hữu thao tác bằng một tay.** `SpatialHandInteractor.ReceiveHands` chọn một candidate và dùng một `activeHand`/cursor; ScanScreenController và ShapeDropFeature cũng có một đối tượng đang kéo. Vì vậy chưa thể khẳng định hai tay cùng điều khiển hai vật độc lập chỉ dựa trên backend có `max_num_hands=2` và mapper không khóa tay.
- **Điều kiện hủy giữ có nguy cơ ảnh hưởng webcam.** `SpatialHandInteractor.cs:129` hủy khi `activeHand != null && hand.spatial == null && mapper.IsCalibrated`. Backend webcam hiện gửi hand không có trường spatial; MapDesktop đặt IsCalibrated=true. Phân tích mã cho thấy gói giữ kế tiếp có thể bị hủy. Chưa kiểm chứng thao tác người thật trong GUI; không tự sửa logic ngoài phạm vi dọn dẹp.
- **Launcher BAT/PowerShell cũ chưa đồng bộ.** Start_SmartGeoAR_PC.ps1 tìm `Builds/Windows/SmartGeoAR.exe`, còn build hiện có ở `Builds/SmartGeoAR.exe`. Mẫu log chờ `SmartGeoAR backend:` cũng khác chuỗi backend hiện in `SmartGeoAR:`. Syntax launcher hợp lệ, nhưng các điều kiện này có thể khiến launcher thất bại. Mã tự khởi động backend từ MainMenuFlow vẫn còn nguyên; không đổi launcher hoặc di chuyển build trong đợt này.
- README_PC.md còn mô tả chưa có exe và luồng khởi động cũ; bản build thực tế đã tồn tại. Chưa sửa tài liệu hướng dẫn chạy để tránh ngầm xác nhận luồng GUI chưa thử.

### Tham chiếu Unity chưa ánh xạ được

14 GUID dưới đây đã tồn tại từ trước; không có GUID mới bị thiếu do dọn dẹp. “Chưa ánh xạ” nghĩa là không tìm thấy `.meta` tương ứng trong Assets, Packages, PackageCache và BuiltInPackages hiện có; chưa coi mọi tham chiếu debug/package cũ là lỗi runtime chắc chắn.

| Vị trí | GUID |
| --- | --- |
| `role3_unity/Assets/Scenes/ARMain.unity`:1825 | `bac9ade69347f684e8b3a14147db42b9` |
| `role3_unity/Assets/Settings/DefaultVolumeProfile.asset`:78 | `60f3b30c03e6ba64d9a27dc9dba8f28d` |
| `role3_unity/Assets/Settings/DefaultVolumeProfile.asset`:119 | `0fd9ee276a1023e439cf7a9c393195fa` |
| `role3_unity/Assets/Settings/DefaultVolumeProfile.asset`:264 | `5a00a63fdd6bd2a45ab1f2d869305ffd` |
| `role3_unity/Assets/Settings/DefaultVolumeProfile.asset`:836 | `74955a4b0b4243bc87231e8b59ed9140` |
| `role3_unity/Assets/Settings/PC_Renderer.asset`:22 | `e5c6678ed2aaa91408dd3df699057aae` |
| `role3_unity/Assets/Settings/PC_Renderer.asset`:24 | `03cfc4915c15d504a9ed85ecc404e607` |
| `role3_unity/Assets/Settings/PC_Renderer.asset`:26 | `53a11f4ebaebf4049b3638ef78dc9664` |
| `role3_unity/Assets/Settings/PC_Renderer.asset`:28 | `8f96cd657dc40064aa21efcc7e50a2e7` |
| `role3_unity/Assets/Settings/PC_Renderer.asset`:30 | `57d7c4c16e2765b47a4d2069b311bffe` |
| `role3_unity/Assets/Settings/PC_Renderer.asset`:32 | `24ec0e140fb444a44ab96ee80844e18e` |
| `role3_unity/Assets/Settings/PC_Renderer.asset`:34 | `b9a23f869c4fd45f19c5ada54dd82176` |
| `role3_unity/Assets/TextMesh Pro/Resources/TMP Settings.asset`:39 | `c41005c129ba4d66911b75229fd70b45` |
| `role3_unity/ProjectSettings/EditorBuildSettings.asset`:10 | `99c9720ab356a0642a771bea13969a05` |

- ARMain:1825 là `ARObjectPlacement.objectPrefab`, không phải m_Script. Giữ component và Scene; không xóa/null hóa tham chiếu để che lỗi. Các controller hiện có vô hiệu hóa placement cũ khi khởi tạo.
- DefaultVolumeProfile có 4 GUID script chưa giải được (OutlineVolumeComponent, TestAnimationCurveVolumeComponent, OasisFogVolumeComponent, TestVolume) và 5 subasset test có `m_Script: {fileID: 0}`. Profile vẫn được UniversalRenderPipelineGlobalSettings tham chiếu nên không xóa hoặc viết lại; làm sạch profile có thể thay đổi hiển thị.
- PC_Renderer có 7 tham chiếu probe/debug cũ; giữ cấu hình render.
- TMP Settings trỏ tới defaultSpriteAsset không tìm thấy. Chưa xác nhận ảnh hưởng emoji/sprite thực tế; font chữ thường vẫn giữ đầy đủ.
- SampleScene trong EditorBuildSettings không tồn tại nhưng entry đang disabled; MainMenu và ARMain đang enabled và tồn tại. Giữ cấu hình build nguyên trạng.
- `ProjectAuditorSettings` dùng fileID 0 cùng `m_EditorClassIdentifier` của module Editor; các GUID built-in `000...e000...` cũng là tham chiếu nội bộ Unity. Không gộp các trường hợp này vào Missing Script của Scene/Prefab.

### Cảnh báo compile/môi trường

- C# sau dọn: 0 lỗi; 12 warning cho mỗi cấu hình runtime/Windows Player, so với 14 trước dọn. Các warning còn lại là CS0618 (API FindObjectsByType cũ), CS0414 (invertDepth chưa dùng) và UAC0009 (DEVELOPMENT_BUILD). Editor tools: 0 lỗi, 0 warning. Không sửa gameplay chỉ để bỏ warning.
- Log Unity còn có cảnh báo entitlement/license, TLS/certificate và một số thông báo Editor/plugin. Chúng không ngăn đợt compile/domain reload đã xác nhận; không đổi cấu hình bảo mật hay tài khoản.

## 6. Kết quả xác minh sau dọn

| Kiểm tra | Kết quả |
| --- | --- |
| Python AST + compile trong bộ nhớ, không chạy demo ở cấp module | 13/13 file đạt |
| Tìm module import bằng importlib + import backend thực tế | 33 module tìm thấy; 0 import thiếu |
| pip check | No broken requirements found |
| Phiên bản môi trường | Python 3.12.10, MediaPipe 0.10.21, OpenCV 4.11.0, NumPy 1.26.4 |
| unittest hình học có sẵn | 8/8 đạt trước và sau dọn |
| Smoke backend bổ sung, chạy bằng script tạm ngoài dự án | 18/18 đạt |
| CLAHE và 3 tùy chọn denoise | Đạt trên ảnh tổng hợp (none/Gaussian/bilateral) |
| Kalman | Đạt kiểm tra nhiễu vị trí tĩnh tổng hợp; không suy diễn thành benchmark người thật |
| ID hai tay + pinch/release độc lập | Đạt trên dữ liệu tổng hợp backend |
| Lucas–Kanade | Hai track theo được dịch chuyển tổng hợp; loại track quá hạn |
| MediaPipe native | Suy luận khung đen thành công; không suy diễn thành nhận diện đồng thời hai bàn tay người thật |
| JSON transport | Ping/pong; hình lập phương, hộp chữ nhật, chóp tứ giác; thả hình đúng/sai hình/sai góc đều đạt |
| Backend webcam thật | Khởi động tiến trình riêng trên cổng localhost tạm; nhận hello, JPEG giải mã 640×480×3, và gói hand đúng schema; tiến trình kiểm tra đã dừng, không chiếm cổng 5053 |
| Unity compiler đi kèm 6000.6.0f1, dùng response file Bee của dự án | Runtime Editor, Windows Player và Editor tools đều exit code 0; output đặt ngoài dự án, không đè build |
| Unity Editor refresh thực tế | AssetDatabase đã nhận phần xóa, Csc + ILPostProcess hoàn tất, log ghi `Scripting: successfully reloaded assembly`, không có error CS; domain reload thành công |
| GUID và meta | 0 GUID trùng, 0 meta mồ côi, 0 asset/thư mục thiếu meta; không còn tham chiếu tới GUID đã xóa trong Assets/ProjectSettings, kể cả meta |
| Scene/Prefab | 2 Scene + 3 Prefab giữ nguyên hash; quét m_Script không thấy Missing Script; không tạo Missing Script mới |
| Các đường nạp động | Giữ 3 asset Resources; giữ MainMenu/ARMain, backend và mọi module import gián tiếp |
| Build cũ | 220/220 file cùng SHA-256 như trước |
| Dữ liệu báo cáo | output 4 file, test_results 5 CSV, benchmark và test hiện có giữ nguyên SHA-256 |

Chưa chạy lại Windows BuildPlayer, chưa thử toàn bộ GUI bằng SmartGeoAR.exe, chưa thử kéo/thả/xoay bằng hai tay người thật. Không tuyên bố các bước này đã đạt. Thử điều khiển UI tự động gặp lỗi `FrameArrived timed out`, `coordinate input geometry is unavailable` và `activate_window timed out`; kết quả compile Editor được xác nhận độc lập từ log và assembly sinh mới, không suy từ trạng thái cửa sổ.

Các lệnh có thể lặp lại trong dự án:

```powershell
& .venv-cv/Scripts/python.exe -B -m unittest discover -s role2_geometry_reasoning -p 'test_*.py' -v
& .venv-cv/Scripts/python.exe -B -m pip check
& .venv-cv/Scripts/python.exe -B -c "import smartgeoar_backend; print('Backend import OK')"
```

Snapshot trước/sau, kết quả smoke, manifest xóa, log compiler và script kiểm tra tạm được lưu tại `C:\Users\admin\AppData\Local\Temp\smartgeoar_pc_cleanup_20261114`. Không chép ảnh webcam, không ghi đè số đo benchmark và không thêm script kiểm thử vào Assets của dự án.
