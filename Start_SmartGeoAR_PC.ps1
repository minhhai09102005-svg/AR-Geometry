$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe = Join-Path $root 'role3_unity\Builds\Windows\SmartGeoAR.exe'
$script = Join-Path $root 'smartgeoar_backend.py'
$logDir = Join-Path $root '_launcher_logs'
$python = Join-Path $root '.venv-cv\Scripts\python.exe'

Write-Host '======================================' -ForegroundColor Cyan
Write-Host 'SmartGeoAR - PC / Webcam (original)' -ForegroundColor Cyan
Write-Host '======================================' -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $python)) {
    throw 'Python virtual environment not found. Run Setup_SmartGeoAR_PC.bat first.'
}
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Windows Unity build missing: $exe`nOpen role3_unity in Unity 6.6, switch Build Profiles to Windows, and build to role3_unity\Builds\Windows\SmartGeoAR.exe"
}
if (-not (Test-Path -LiteralPath $script)) {
    throw "Python backend missing: $script"
}

# Refuse to silently attach to a stale mobile/phone backend on the same port.
$portCheck = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 5053)
try { $portCheck.Start() }
catch { throw 'Port 5053 is already in use. Close the previous SmartGeoAR backend first.' }
finally { $portCheck.Stop() }

New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$outLog = Join-Path $logDir 'backend_pc.out.log'
$errLog = Join-Path $logDir 'backend_pc.err.log'
$backend = $null
$unity = $null

try {
    Write-Host '[1/2] Starting webcam + hand tracking + geometry reasoning...' -ForegroundColor Yellow
    $arguments = '-u "' + $script + '" --host 127.0.0.1 --port 5053 --camera-source webcam'
    $backend = Start-Process -FilePath $python -ArgumentList $arguments -WorkingDirectory $root -RedirectStandardOutput $outLog -RedirectStandardError $errLog -WindowStyle Hidden -PassThru

    # Wait for startup output. Never probe the TCP port with a client socket:
    # the backend serves one session at a time, and a probe would consume it.
    $ready = $false
    for ($i = 0; $i -lt 120; $i++) {
        Start-Sleep -Milliseconds 150
        $backend.Refresh()
        if ($backend.HasExited) { break }
        if (Test-Path -LiteralPath $outLog) {
            $status = Get-Content -LiteralPath $outLog -ErrorAction SilentlyContinue
            if ($status -match 'SmartGeoAR backend: 127\.0\.0\.1:5053') {
                $ready = $true
                break
            }
        }
    }
    if (-not $ready) {
        Write-Host 'Backend stdout:' -ForegroundColor Red
        if (Test-Path $outLog) { Get-Content $outLog -Tail 20 }
        Write-Host 'Backend stderr:' -ForegroundColor Red
        if (Test-Path $errLog) { Get-Content $errLog -Tail 20 }
        throw 'Python backend did not start on 127.0.0.1:5053.'
    }

    Write-Host '[2/2] Launching Unity (Windows)...' -ForegroundColor Green
    $unity = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe -Parent) -PassThru
    Write-Host 'Use mouse to choose a lesson. Pinch once to calibrate a hand.'
    Write-Host 'Close Unity to stop the Python backend.'
    $unity.WaitForExit()
}
finally {
    if ($null -ne $backend) {
        try { Stop-Process -Id $backend.Id -Force -ErrorAction SilentlyContinue } catch {}
    }
    Write-Host 'SmartGeoAR PC stopped.'
}
