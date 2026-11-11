@echo off
setlocal

cd /d "%~dp0"

set "SG_PYTHON="

if exist ".venv-cv\Scripts\python.exe" set "SG_PYTHON=.venv-cv\Scripts\python.exe"
if not defined SG_PYTHON if exist ".venv\Scripts\python.exe" set "SG_PYTHON=.venv\Scripts\python.exe"
if not defined SG_PYTHON if exist "venv\Scripts\python.exe" set "SG_PYTHON=venv\Scripts\python.exe"

if not defined SG_PYTHON (
    where python.exe >nul 2>nul
    if not errorlevel 1 set "SG_PYTHON=python.exe"
)

if not defined SG_PYTHON (
    echo Python not found. Check .venv-cv or PATH.
    pause
    exit /b 1
)

echo Starting laptop backend on TCP 0.0.0.0:5053...
echo Hand tracking uses frames from the phone rear camera.
echo Set HandDataReceiver.HOST to this laptop's Wi-Fi IPv4 address.
echo Allow inbound TCP port 5053 through Windows Firewall if prompted.
echo Press Ctrl+C to stop.

"%SG_PYTHON%" -u "smartgeoar_backend.py" --host 0.0.0.0 --camera-source phone --no-camera-stream

if errorlevel 1 pause
