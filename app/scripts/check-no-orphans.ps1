<#
.SYNOPSIS
Checks that the app leaves no sidecar or WebView2 process behind, whether it is closed or killed.

.DESCRIPTION
Runs the built app with SI_APP_DATA_DIR and SI_APP_STATE_DIR set to fresh temporary folders (never the user's own),
waits for its si-sidecar child to listen, then:

  1. Closes the main window (WM_CLOSE, nothing running): the app, the sidecar and every msedgewebview2.exe started
     with the app's WebView2 user data folder must be gone within 10 s.
  2. Relaunches it and kills the shell (Stop-Process -Force, which is TerminateProcess, as Task Manager does): the
     sidecar must be gone within 5 s.

Prints the timings, exits 1 on any failure, and removes the temporary folders.

.PARAMETER AppExe
The app's executable. Defaults to src-tauri/target/release/shouting-iguana.exe, else the debug build.
#>
param([string]$AppExe)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$CloseTimeout = [TimeSpan]::FromSeconds(10)
$KillTimeout = [TimeSpan]::FromSeconds(5)
$StartTimeout = [TimeSpan]::FromSeconds(30)

function Fail([string]$Message) {
    [Console]::Error.WriteLine("check-no-orphans: $Message")
    exit 1
}

$appRoot = Split-Path -Parent $PSScriptRoot
if (-not $AppExe) {
    foreach ($build in 'release', 'debug') {
        $candidate = Join-Path $appRoot "src-tauri\target\$build\shouting-iguana.exe"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $AppExe = $candidate
            break
        }
    }
    if (-not $AppExe) {
        Fail 'no built app in src-tauri/target/release or debug: run `npm run tauri build` first, or pass -AppExe.'
    }
}
if (-not (Test-Path -LiteralPath $AppExe -PathType Leaf)) {
    Fail "there is no app at $AppExe."
}
$AppExe = (Resolve-Path -LiteralPath $AppExe).Path
$sidecarName = 'si-sidecar.exe'
if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $AppExe) $sidecarName))) {
    Fail "there is no $sidecarName next to ${AppExe}: the app can't start its engine."
}

# Tauri gives WebView2 a user data folder named after the app's identifier.
$identifier = (Get-Content -LiteralPath (Join-Path $appRoot 'src-tauri\tauri.conf.json') -Raw | ConvertFrom-Json).identifier
$webviewData = Join-Path $env:LOCALAPPDATA "$identifier\EBWebView"

$root = Join-Path ([IO.Path]::GetTempPath()) "si-orphans-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
New-Item -ItemType Directory -Path $root | Out-Null
$failures = New-Object System.Collections.Generic.List[string]
$launched = New-Object System.Collections.Generic.List[System.Diagnostics.Process]

function Format-Ms([TimeSpan]$Elapsed) {
    '{0:n0} ms' -f $Elapsed.TotalMilliseconds
}

# Polls until the condition holds (checking at least once); the time it took, or $null after the timeout.
function Wait-Until([scriptblock]$Condition, [TimeSpan]$Timeout) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        if (& $Condition) {
            return $watch.Elapsed
        }
        if ($watch.Elapsed -ge $Timeout) {
            return $null
        }
        Start-Sleep -Milliseconds 20
    }
}

function Get-Children([int]$ParentId, [string]$Name) {
    @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $ParentId AND Name = '$Name'")
}

# WebView2 processes running on the app's user data folder, started since $Since.
function Get-WebViewProcesses([datetime]$Since) {
    @(Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" | Where-Object {
            $_.CommandLine -and $_.CommandLine.IndexOf($webviewData, [StringComparison]::OrdinalIgnoreCase) -ge 0 -and
            $_.CreationDate -ge $Since.AddSeconds(-1)
        })
}

function Test-Running([int[]]$Ids) {
    @($Ids | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue }).Count -gt 0
}

function Start-App([string]$Name) {
    $data = New-Item -ItemType Directory -Path (Join-Path $root "$Name-data")
    $state = New-Item -ItemType Directory -Path (Join-Path $root "$Name-state")
    $info = New-Object Diagnostics.ProcessStartInfo $AppExe
    $info.UseShellExecute = $false
    $info.EnvironmentVariables['SI_APP_DATA_DIR'] = $data.FullName
    $info.EnvironmentVariables['SI_APP_STATE_DIR'] = $state.FullName
    $since = Get-Date
    $app = [Diagnostics.Process]::Start($info)
    $launched.Add($app)
    $script:sidecarId = $null
    $listening = Wait-Until {
        if ($app.HasExited) { return $true }
        if (-not $script:sidecarId) {
            $child = Get-Children $app.Id $sidecarName | Select-Object -First 1
            if ($child) { $script:sidecarId = [int]$child.ProcessId }
        }
        $script:sidecarId -and @(Get-NetTCPConnection -OwningProcess $script:sidecarId -State Listen -ErrorAction SilentlyContinue).Count -gt 0
    } $StartTimeout
    if ($app.HasExited) {
        Fail "the app exited at once (exit code $($app.ExitCode)). Is another Shouting Iguana running? It takes over a second launch."
    }
    if (-not $listening) {
        Fail "the app's sidecar wasn't listening within $($StartTimeout.TotalSeconds) s (sidecar pid: $script:sidecarId)."
    }
    $sidecar = Get-Process -Id $script:sidecarId
    $null = $sidecar.Handle   # held, so its exit code can be read once it has gone
    $windowShown = Wait-Until { $app.Refresh(); $app.MainWindowHandle -ne [IntPtr]::Zero } $StartTimeout
    if (-not $windowShown) {
        Fail "the app's window didn't appear within $($StartTimeout.TotalSeconds) s."
    }
    # The page has loaded once WebView2's renderer is up; give it a moment to settle so the close is an ordinary one.
    $null = Wait-Until { @(Get-WebViewProcesses $since).Count -ge 3 } ([TimeSpan]::FromSeconds(10))
    Start-Sleep -Milliseconds 500
    [pscustomobject]@{ App = $app; Sidecar = $sidecar; Since = $since; WebViews = @(Get-WebViewProcesses $since) }
}

try {
    Write-Host "check-no-orphans: $AppExe"

    # Case 1: closing the window with nothing running quits, and takes everything with it.
    $run = Start-App 'close'
    $webViewIds = @($run.WebViews | ForEach-Object { [int]$_.ProcessId })
    $watch = [Diagnostics.Stopwatch]::StartNew()
    if (-not $run.App.CloseMainWindow()) {
        $failures.Add('case 1: the window refused WM_CLOSE.')
    }
    $appGone = Wait-Until { $run.App.HasExited } $CloseTimeout
    $appAt = if ($appGone) { $watch.Elapsed } else { $null }
    $sidecarGone = Wait-Until { $run.Sidecar.HasExited } ($CloseTimeout - $watch.Elapsed)
    $sidecarAt = if ($sidecarGone) { $watch.Elapsed } else { $null }
    $webViewsGone = Wait-Until { -not (Test-Running $webViewIds) -and @(Get-WebViewProcesses $run.Since).Count -eq 0 } ($CloseTimeout - $watch.Elapsed)
    $webViewsAt = if ($webViewsGone) { $watch.Elapsed } else { $null }
    $line = 'case 1, close the window: '
    $line += if ($appAt) { "app gone in $(Format-Ms $appAt) (exit code $($run.App.ExitCode))" } else { 'app STILL RUNNING' }
    $line += if ($sidecarAt) { ", sidecar in $(Format-Ms $sidecarAt) (exit code $($run.Sidecar.ExitCode))" } else { ', sidecar STILL RUNNING' }
    $line += if ($webViewsAt) { ", $($webViewIds.Count) WebView2 processes in $(Format-Ms $webViewsAt)" } else { ', WebView2 processes STILL RUNNING' }
    Write-Host $line
    if (-not $appAt) { $failures.Add("case 1: the app was still running $($CloseTimeout.TotalSeconds) s after WM_CLOSE.") }
    if (-not $sidecarAt) { $failures.Add("case 1: the sidecar was still running $($CloseTimeout.TotalSeconds) s after WM_CLOSE.") }
    if (-not $webViewsAt) { $failures.Add("case 1: WebView2 processes were still running $($CloseTimeout.TotalSeconds) s after WM_CLOSE.") }

    # Case 2: killing the shell outright still takes the sidecar with it.
    $run = Start-App 'kill'
    $webViewIds = @($run.WebViews | ForEach-Object { [int]$_.ProcessId })
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Stop-Process -Id $run.App.Id -Force
    $sidecarGone = Wait-Until { $run.Sidecar.HasExited } $KillTimeout
    $sidecarAt = if ($sidecarGone) { $watch.Elapsed } else { $null }
    $webViewsGone = Wait-Until { -not (Test-Running $webViewIds) } ($CloseTimeout - $watch.Elapsed)
    $webViewsAt = if ($webViewsGone) { $watch.Elapsed } else { $null }
    $line = 'case 2, kill the shell: '
    $line += if ($sidecarAt) { "sidecar gone in $(Format-Ms $sidecarAt) (exit code $($run.Sidecar.ExitCode))" } else { 'sidecar STILL RUNNING' }
    $line += if ($webViewsAt) { ", $($webViewIds.Count) WebView2 processes in $(Format-Ms $webViewsAt)" } else { ', WebView2 processes still running (not asserted)' }
    Write-Host $line
    if (-not $sidecarAt) { $failures.Add("case 2: the sidecar was still running $($KillTimeout.TotalSeconds) s after the shell was killed.") }
}
finally {
    # Whatever happened, nothing this script started outlives it.
    foreach ($app in $launched) {
        if (-not $app.HasExited) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
        foreach ($child in (Get-Children $app.Id $sidecarName)) {
            Stop-Process -Id $child.ProcessId -Force -ErrorAction SilentlyContinue
        }
    }
    $removed = Wait-Until {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        -not (Test-Path -LiteralPath $root)
    } ([TimeSpan]::FromSeconds(10))
    if (-not $removed) { Write-Warning "couldn't remove $root" }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { [Console]::Error.WriteLine("check-no-orphans: FAIL $_") }
    exit 1
}
Write-Host 'check-no-orphans: PASS'
