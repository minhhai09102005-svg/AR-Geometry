@echo off
setlocal
cd /d "%~dp0"
if not exist ".venv-cv\Scripts\python.exe" (
  echo Please run Setup_SmartGeoAR_PC.bat first.
  pause
  exit /b 1
)
echo Starting SmartGeoAR backend with PC webcam on 127.0.0.1:5053
echo Leave this terminal running while Unity is in Play Mode.
".venv-cv\Scripts\python.exe" -u "smartgeoar_backend.py" --host 127.0.0.1 --port 5053 --camera-source webcam
if errorlevel 1 pause
