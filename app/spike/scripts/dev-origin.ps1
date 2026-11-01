# Runs `tauri dev` (Vite dev server + debug build) long enough to record the Origin WebView2 sends in dev, then closes it.
$ErrorActionPreference = 'Stop'
$spike = Split-Path $PSScriptRoot -Parent
Set-Location $spike
$port = 51873 # must match build.devUrl in src-tauri/tauri.conf.json
if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) { throw "port $port is busy" }
$log = Join-Path $spike "results\tauri-dev-$(Get-Date -Format yyyyMMddHHmmss).jsonl"
$env:SPIKE_LOG = $log
$vite = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', "npx vite --port $port --strictPort > results\vite-dev.log 2>&1" -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 3
$dev = Start-Process -FilePath 'powershell' -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "$spike\scripts\with-msvc.ps1", 'npx', 'tauri', 'dev' -PassThru -WindowStyle Hidden -RedirectStandardOutput "$spike\results\tauri-dev-build.log" -RedirectStandardError "$spike\results\tauri-dev-build.err.log"
$deadline = (Get-Date).AddMinutes(12)
while ((Get-Date) -lt $deadline) {
    if ((Test-Path $log) -and (Select-String -Path $log -Pattern '"name":"first-rows-painted"' -Quiet)) { break }
    Start-Sleep -Seconds 2
}
Start-Sleep -Seconds 3
$app = Get-Process -Name 'si-spike' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($app) { $app.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 3 }
foreach ($p in @($dev, $vite)) { if (-not $p.HasExited) { & taskkill /PID $p.Id /T /F | Out-Null } }
Get-Process -Name 'si-spike', 'spike-sidecar' -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Output "log: $log"
if (Test-Path $log) {
    Get-Content $log | ForEach-Object { $_ | ConvertFrom-Json } |
        Where-Object { $_.event -eq 'sidecar-log' -and $_.entry.event -eq 'request-shape' } |
        ForEach-Object { "$($_.entry.outcome) $($_.entry.method) $($_.entry.path) origin=$($_.entry.origin) host=$($_.entry.host) acrh=$($_.entry.acrHeaders) pna=$($_.entry.acrPrivateNetwork) site=$($_.entry.secFetchSite)" } |
        Sort-Object -Unique
}
