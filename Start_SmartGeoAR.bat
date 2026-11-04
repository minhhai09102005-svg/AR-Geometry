
@echo off
setlocal

set "SG_ROOT=%~dp0"
set "SG_THIS=%~f0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$source=[IO.File]::ReadAllText($env:SG_THIS); $parts=[regex]::Split($source, '(?m)^# --- POWERSHELL ---\r?\n', 2); if($parts.Length -ne 2){throw 'Launcher code not found'}; Invoke-Expression $parts[1]"

if errorlevel 1 pause
exit /b %errorlevel%

# --- POWERSHELL ---

$ErrorActionPreference = "Stop"

$root = $env:SG_ROOT

$role1Dir = Join-Path $root "role1_hand_tracking"
$role2Dir = Join-Path $root "role2_geometry_reasoning"

$role1File = Join-Path $role1Dir "role1_week2_stream.py"
$role2File = Join-Path $role2Dir "main.py"

$unityExe = Join-Path $root "role3_unity\Builds\Windows\SmartGeoAR.exe"

$logDir = Join-Path $root "_launcher_logs"

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

$role1Out = Join-Path $logDir "role1.out.log"
$role1Err = Join-Path $logDir "role1.err.log"
$role2Out = Join-Path $logDir "role2.out.log"
$role2Err = Join-Path $logDir "role2.err.log"

# Tim moi truong Python
$python = $null

$candidates = @(
    (Join-Path $root ".venv-cv\Scripts\python.exe"),
    (Join-Path $root ".venv\Scripts\python.exe"),
    (Join-Path $root "venv\Scripts\python.exe")
)

foreach ($candidate in $candidates) {
    if (Test-Path -LiteralPath $candidate) {
        $python = $candidate
        break
    }
}

if (-not $python) {
    $command = Get-Command python.exe -ErrorAction SilentlyContinue

    if ($command) {
        $python = $command.Source
    }
}

if (-not $python) {
    throw "Khong tim thay Python. Kiem tra .venv-cv hoac PATH."
}

if (-not (Test-Path -LiteralPath $role1File)) {
    throw "Khong tim thay Role 1: $role1File"
}

if (-not (Test-Path -LiteralPath $role2File)) {
    throw "Khong tim thay Role 2: $role2File"
}

if (-not (Test-Path -LiteralPath $unityExe)) {
    throw "Chua build Unity Windows. Can file: $unityExe"
}

$p1 = $null
$p2 = $null
$unity = $null
$failed = $false

$env:PYTHONUNBUFFERED = "1"

try {
    Write-Host "[1/3] Starting Role 2..." -ForegroundColor Cyan

    $p2 = Start-Process `
        -FilePath $python `
        -ArgumentList @("-u", "main.py", "--serve") `
        -WorkingDirectory $role2Dir `
        -RedirectStandardOutput $role2Out `
        -RedirectStandardError $role2Err `
        -WindowStyle Hidden `
        -PassThru

    Start-Sleep -Seconds 1

    $p2.Refresh()

    if ($p2.HasExited) {
        throw "Role 2 exited. Check role2.err.log"
    }

    Write-Host "[2/3] Starting Role 1..." -ForegroundColor Cyan

    $p1 = Start-Process `
        -FilePath $python `
        -ArgumentList @("-u", "role1_week2_stream.py") `
        -WorkingDirectory $role1Dir `
        -RedirectStandardOutput $role1Out `
        -RedirectStandardError $role1Err `
        -WindowStyle Hidden `
        -PassThru

    Start-Sleep -Seconds 1

    $p1.Refresh()

    if ($p1.HasExited) {
        throw "Role 1 exited. Check role1.err.log"
    }

    Write-Host "[3/3] Starting Unity SmartGeoAR..." -ForegroundColor Green

    $unity = Start-Process `
        -FilePath $unityExe `
        -WorkingDirectory (Split-Path $unityExe -Parent) `
        -PassThru

    Write-Host ""
    Write-Host "SmartGeoAR is running!" -ForegroundColor Green
    Write-Host "Close the Unity app to stop Role 1 and Role 2."

    # Doi cho toi khi ung dung Unity dong
    $unity.WaitForExit()
}
catch {
    $failed = $true

    Write-Host ""
    Write-Host "SMARTGEOAR STARTUP FAILED" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red

    foreach ($log in @($role2Err, $role1Err)) {
        if (Test-Path $log) {
            Write-Host ""
            Write-Host "LOG: $log"
            Get-Content $log -Tail 15
        }
    }
}
finally {
    Write-Host "Stopping SmartGeoAR services..."

    foreach ($process in @($p1, $p2)) {
        if ($null -ne $process) {
            Stop-Process `
                -Id $process.Id `
                -Force `
                -ErrorAction SilentlyContinue
        }
    }

    Write-Host "Services stopped."
}

if ($failed) {
    Read-Host "Press Enter to close"
    exit 1
}
