<#
.SYNOPSIS
Runs a built or installed shell and proves it started its engine, without needing its window (Windows).

.DESCRIPTION
What smoke-installer.sh checks on Linux and macOS, for a machine where smoke-tauri.mjs can't attach to the window (a
GitHub-hosted runner). It starts the shell on temporary data, state and profile folders, then checks:
1. The shell's log says its engine is ready on a port.
2. That port answers 401 without the token: it is the engine, and it is guarded.
3. A si-sidecar.exe runs on the test's state folder.
4. Once the shell has gone (its window closed, or the process ended when it has none), no such si-sidecar.exe is left.

It also says whether the window's page called the engine (the engine's own log), without failing on it: that needs a
desktop the runner may not have. Exits 1 on any failure.

.PARAMETER Exe
The shell: shouting-iguana.exe, with si-sidecar.exe beside it.
#>
param([Parameter(Mandatory = $true)][string]$Exe)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ReadyTimeout = [TimeSpan]::FromSeconds(90)
$PageTimeout = [TimeSpan]::FromSeconds(20)
$StopTimeout = [TimeSpan]::FromSeconds(45)
$script:failed = $false

function Fail([string]$Message) {
    [Console]::Error.WriteLine("smoke-engine: FAIL $Message")
    $script:failed = $true
}

function Wait-Until([scriptblock]$Condition, [TimeSpan]$Timeout) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while (-not (& $Condition)) {
        if ($watch.Elapsed -ge $Timeout) { return $false }
        Start-Sleep -Milliseconds 250
    }
    return $true
}

$Exe = (Resolve-Path -LiteralPath $Exe).Path
$temp = Join-Path ([IO.Path]::GetTempPath()) "si-smoke-engine-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$state = Join-Path $temp 'state'
$log = Join-Path $temp 'profile\logs\shell.log'

function Get-Sidecars {
    @(Get-CimInstance Win32_Process -Filter "Name='si-sidecar.exe'" | Where-Object { $_.CommandLine -like "*$temp*" })
}

function Get-EnginePort {
    if (-not (Test-Path -LiteralPath $log)) { return $null }
    $line = Select-String -LiteralPath $log -Pattern 'The engine is ready on port (\d+)' | Select-Object -First 1
    if ($line) { return $line.Matches[0].Groups[1].Value }
    return $null
}

# The shell inherits these from this process; they are taken away again once it has started.
$env:SI_APP_DATA_DIR = Join-Path $temp 'data'
$env:SI_APP_STATE_DIR = $state
$env:SI_APP_PROFILE_DIR = Join-Path $temp 'profile'
$shell = $null
try {
    $shell = Start-Process -FilePath $Exe -PassThru
    Write-Host "smoke-engine: started $Exe (pid $($shell.Id))"
}
finally {
    Remove-Item Env:SI_APP_DATA_DIR, Env:SI_APP_STATE_DIR, Env:SI_APP_PROFILE_DIR -ErrorAction SilentlyContinue
}

try {
    Wait-Until { (Get-EnginePort) -or $shell.HasExited } $ReadyTimeout | Out-Null
    $port = Get-EnginePort
    if (-not $port) {
        if ($shell.HasExited) { Fail "the shell stopped (exit code $($shell.ExitCode)) before its engine was ready." }
        else { Fail "the shell's log never said its engine was ready ($($ReadyTimeout.TotalSeconds) s)." }
        if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 30 | ForEach-Object { [Console]::Error.WriteLine($_) } }
    }
    else {
        Write-Host "smoke-engine: the shell's log: the engine is ready on port $port"
        $status = 0
        try { $status = [int](Invoke-WebRequest -Uri "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 10).StatusCode }
        catch { if ($_.Exception.PSObject.Properties['Response'] -and $_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode } }
        if ($status -eq 401) { Write-Host "smoke-engine: port $port answers 401 without the token: the engine, guarded" }
        else { Fail "GET /api/health on port $port without the token answered $status, not 401." }

        $sidecars = @(Get-Sidecars)
        if ($sidecars.Count -eq 0) { Fail "no si-sidecar.exe runs on $state." }
        else { Write-Host "smoke-engine: si-sidecar.exe runs (pid $($sidecars.ProcessId -join ', '))" }

        $called = Wait-Until {
            $logs = Join-Path $state 'logs\*.log'
            (Test-Path $logs) -and (Select-String -Path $logs -Pattern "Request finished HTTP/1.1 GET http://127.0.0.1:$port/api/crawls - 200" -Quiet)
        } $PageTimeout
        if ($called) { Write-Host 'smoke-engine: the window''s page called the engine with the token (GET /api/crawls, 200)' }
        else { Write-Host 'smoke-engine: the window''s page did not call the engine: no window here (not a failure of this check).' }
        if ($shell.HasExited) { Fail "the shell has stopped by itself (exit code $($shell.ExitCode))." }
    }
}
finally {
    if ($shell -and -not $shell.HasExited) {
        # As a user closes it; a shell with no window (no desktop) is ended, which the engine must not outlive either.
        $closed = $false
        try { $shell.Refresh(); $closed = $shell.CloseMainWindow() } catch { $closed = $false }
        if (-not $closed -or -not $shell.WaitForExit(15000)) {
            Write-Host 'smoke-engine: ending the shell (it has no window to close, or did not quit)'
            Stop-Process -Id $shell.Id -Force -ErrorAction SilentlyContinue
            $shell.WaitForExit(15000) | Out-Null
        }
    }
    if (Wait-Until { @(Get-Sidecars).Count -eq 0 } $StopTimeout) {
        Write-Host 'smoke-engine: the shell has stopped, and no si-sidecar.exe is left'
    }
    else {
        Fail "a si-sidecar.exe was still running $($StopTimeout.TotalSeconds) s after the shell stopped."
        Get-Sidecars | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    }
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

if ($script:failed) {
    [Console]::Error.WriteLine('smoke-engine: FAIL')
    exit 1
}
Write-Host 'smoke-engine: PASS'
