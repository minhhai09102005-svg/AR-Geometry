# SmartGeoAR - Ban goc PC (Windows, webcam)

Bo ma nguon nay duoc khoi phuc tu `AR-Geometry-main.zip` do nguoi dung da gui. Dung webcam **cua laptop/PC**, MediaPipe Hands, Unity 3D, va backend Python xu ly hinh hoc. Khong can dien thoai, kinh AR/VR, ARCore hoac Wi-Fi noi bo.

## Can gi?

- Windows 10/11, webcam, chuot/ban phim
- Python 3.11/3.12 (de cai MediaPipe 0.10.21)
- **Unity 6.6 (6000.6.0f1)** va Windows Build Support (IL2CPP hoac Mono theo build settings)

## Chay nhanh

1. Giai nen toan bo ZIP, tranh duong dan co ky tu dac biet.
2. Chay `Setup_SmartGeoAR_PC.bat` de tao `.venv-cv` va cai MediaPipe/OpenCV.
3. Mo `role3_unity` trong Unity Hub, doi build target sang **Windows** (File > Build Profiles).
4. Trong Scene List chon `Assets/Scenes/MainMenu.unity` truoc `Assets/Scenes/ARMain.unity`.
5. Build Windows, luu theo **duong dan bat buoc** `role3_unity/Builds/Windows/SmartGeoAR.exe`.
6. Double-click `Start_SmartGeoAR.bat`. Backend Python se khoi dong tu dong, sau do Windows Unity app mo.
7. Chon bai hoc bang chuot. Sau khi vao man hinh 3D, chum ngon tro & cai mot lan de **can tay** cho webcam, sau do gap/tha canh hoac hinh.

**Quan trong:** ZIP la **source code**, khong co exe Windows biên dich san. Unity Editor khong co trong moi truong tao goi nen can build exe tren may ban mot lan.

## Thu trong Unity Editor (khong build)

1. Chay `Start_Backend_PC.bat` va de cua so mo.
2. Trong Unity, mo `Assets/Scenes/MainMenu.unity` va bam Play.
3. Backend TCP chay tai `127.0.0.1:5053`; chuot chon menu; webcam nhan dien tay.
4. Neu webcam khong mo duoc, dong Zoom/Teams/Camera dang chiem webcam va kiem tra `CAMERA_INDEX = 0` trong `role1_hand_tracking/role1_week2_stream.py`.

## Module bao gom

- `role1_hand_tracking/`: nhan dien ban tay, MediaPipe, pinch, 3D hand pose.
- `role2_geometry_reasoning/`: logic kiem tra hinh lap phuong/hop chu nhat/chop tu giac va tha hinh.
- `role3_unity/`: Unity UI, scene, vat the 3D, tuong tac chuot/tay.
- `smartgeoar_backend.py`: backend TCP thong nhat (Role 1 + Role 2), gui webcam JPEG va JSON toi Unity.

## Thay doi so voi ma nguon ZIP goc

- `HandDataReceiver.cs`: Windows ket noi `127.0.0.1:5053`, khong khoi dong trung Python backend.
- `SpatialWorkspace.cs`: desktop khong doi ARCore tracking; phone branch giu nguyen ma goc.
- `MainMenuFlow.cs`: desktop giu nguyen Canvas UI goc de dung chuot, khong chuyen sang world-space phone menu.
- `ProjectSettings.asset`: Windowed 1600 x 900, cho phep resize.
- `Start_SmartGeoAR.bat`: chay **1 backend thong nhat** bang webcam, dong Unity la dung backend.
- Phan lon gameplay Python/Unity duoc giu nguyen **tu ZIP goc**; cac ban sua mobile/occlusion/scale trong chat khong duoc dua vao.

## Xu ly loi

- `Connection refused` -> chua chay backend, dung `Start_Backend_PC.bat` khi thu Editor.
- `Port 5053 in use` -> dong backend Python cu, khong chay Start_Backend_PC.bat va Start_SmartGeoAR.bat cung luc.
- `Camera error` -> webcam dang bi chiem; doi `CAMERA_INDEX` neu PC co nhieu camera.
- Loi compile Unity -> kiem tra dung phien ban Unity 6000.6.0f1 va package import xong.
- Log backend cua launcher: `_launcher_logs/backend_pc.err.log`.
- Ve quat tay bi sai huong -> hay chum ngon can tay tai tu the ban dau; desktop su dung `mirrored_camera_m` trong code goc.

## Gioi han

Chua co Windows `SmartGeoAR.exe` trong ZIP nguon goc, va chua the kiem tra chay GUI Unity tren Windows tu moi truong tao goi. Da kiem tra cau truc, scripts Python va duong dan.
