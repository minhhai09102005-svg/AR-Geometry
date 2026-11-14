@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start_SmartGeoAR_PC.ps1"
set "SG_EXIT=%errorlevel%"
if not "%SG_EXIT%"=="0" pause
exit /b %SG_EXIT%
