@echo off
setlocal
cd /d "%~dp0"

echo SmartGeoAR PC - Python setup
if not exist ".venv-cv\Scripts\python.exe" (
  where py.exe >nul 2>nul
  if not errorlevel 1 (
    py -3.12 -m venv .venv-cv 2>nul
    if not exist ".venv-cv\Scripts\python.exe" py -3.11 -m venv .venv-cv 2>nul
  )
  if not exist ".venv-cv\Scripts\python.exe" (
    where python.exe >nul 2>nul
    if not errorlevel 1 python -m venv .venv-cv
  )
)
if not exist ".venv-cv\Scripts\python.exe" (
  echo Could not create a virtual environment. Install Python 3.11 or 3.12 with PATH enabled.
  pause
  exit /b 1
)
".venv-cv\Scripts\python.exe" -m pip install --upgrade pip
if errorlevel 1 goto fail
".venv-cv\Scripts\python.exe" -m pip install -r requirements_PC.txt
if errorlevel 1 goto fail
".venv-cv\Scripts\python.exe" -c "import cv2, mediapipe, numpy; print('Python dependencies OK')"
if errorlevel 1 goto fail
echo.
echo DONE. Next: Build role3_unity for Windows, then run Start_SmartGeoAR.bat.
pause
exit /b 0
:fail
echo.
echo Dependency installation failed. Check the error above.
pause
exit /b 1
