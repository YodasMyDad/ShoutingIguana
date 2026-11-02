<#
.SYNOPSIS
Builds the engine's sidecar for the Tauri shell. Twin of build-sidecar.sh; `npm run build:sidecar` runs whichever
fits the platform.

.DESCRIPTION
Publishes engine/src/ShoutingIguana.Sidecar as one self-contained file for the target's .NET runtime, so users need
no .NET installed, into app/.sidecar-build (its own artefacts folder, so it never collides with other builds of the
engine). Then copies si-sidecar to src-tauri/binaries/si-sidecar-<target triple>(.exe), where Tauri's externalBin
expects it, and signs a Windows build there with scripts/sign.mjs (which skips, saying so, without a certificate).
Tauri copies it next to the app, without the triple, in dev, build and bundles. IIS's in-process module, which a
self-hosted Kestrel never loads, is left out.

.PARAMETER EngineRoot
The engine folder. Defaults to the repo's engine/, found from this script's location.

.PARAMETER Configuration
The .NET build configuration. Release by default.

.PARAMETER Target
A Rust target triple. Defaults to the host's, from `rustc -vV`.

.PARAMETER NoReadyToRun
Leaves out ahead-of-time compilation (PublishReadyToRun), which is on by default. Measured on 2026-11-01 (win-x64,
spawn to ready line, 15 warm runs): 116 ms with it, 152 ms without, and 125 against 157 ms on a first run. It costs
25 MB more on disk (128 MB against 104 MB) and a slower very first run while antivirus scans the new file.
#>
param(
    [string]$EngineRoot,
    [string]$Configuration = 'Release',
    [string]$Target,
    [switch]$NoReadyToRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Fail([string]$Message) {
    [Console]::Error.WriteLine("build-sidecar: $Message")
    exit 1
}

# Rust target triples and the .NET runtime identifiers that build for them.
$runtimes = @{
    'x86_64-pc-windows-msvc'    = 'win-x64'
    'aarch64-pc-windows-msvc'   = 'win-arm64'
    'x86_64-apple-darwin'       = 'osx-x64'
    'aarch64-apple-darwin'      = 'osx-arm64'
    'x86_64-unknown-linux-gnu'  = 'linux-x64'
    'aarch64-unknown-linux-gnu' = 'linux-arm64'
}

$appRoot = Split-Path -Parent $PSScriptRoot
if (-not $EngineRoot) {
    $EngineRoot = Join-Path (Split-Path -Parent $appRoot) 'engine'
}
$EngineRoot = [IO.Path]::GetFullPath($EngineRoot)
$project = Join-Path $EngineRoot 'src\ShoutingIguana.Sidecar\ShoutingIguana.Sidecar.csproj'
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    Fail "there is no sidecar project at $project. Pass the engine folder with -EngineRoot (--engine-root through npm)."
}

if (-not $Target) {
    $rustc = Get-Command rustc -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
    if (-not $rustc) {
        $cargoHome = if ($env:CARGO_HOME) { $env:CARGO_HOME } else { Join-Path $env:USERPROFILE '.cargo' }
        $candidate = Join-Path $cargoHome 'bin\rustc.exe'
        if (Test-Path -LiteralPath $candidate) { $rustc = $candidate }
    }
    if (-not $rustc) {
        Fail 'rustc wasn''t found, so the host''s target triple is unknown: install Rust, or pass -Target.'
    }
    $hostLine = & $rustc -vV | Where-Object { $_ -like 'host: *' } | Select-Object -First 1
    if (-not $hostLine) {
        Fail "``$rustc -vV`` didn't name the host's target triple: pass -Target."
    }
    $Target = $hostLine.Substring('host: '.Length).Trim()
}
$runtime = $runtimes[$Target]
if (-not $runtime) {
    Fail "there is no .NET runtime for the target $Target. Known targets: $(($runtimes.Keys | Sort-Object) -join ', ')."
}
$extension = if ($Target -like '*-windows-*') { '.exe' } else { '' }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail 'dotnet wasn''t found: install the .NET SDK the engine targets (see engine/README.md).'
}

$artifacts = Join-Path $appRoot '.sidecar-build'
$readyToRunValue = if ($NoReadyToRun) { 'false' } else { 'true' }
$arguments = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', $runtime,
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $PSScriptRoot 'sidecar-publish.targets')",
    '-p:DebugType=none',
    "-p:PublishReadyToRun=$readyToRunValue",
    '--artifacts-path', $artifacts,
    '--nologo'
)
Write-Host "build-sidecar: publishing $runtime ($Configuration) from $EngineRoot"
& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    Fail "dotnet publish failed with exit code $LASTEXITCODE."
}

# The artefacts layout puts a publish in publish/<project>/<configuration>_<runtime>.
$published = Join-Path $artifacts "publish\ShoutingIguana.Sidecar\$($Configuration.ToLowerInvariant())_$runtime\si-sidecar$extension"
if (-not (Test-Path -LiteralPath $published -PathType Leaf)) {
    Fail "dotnet publish succeeded but $published isn't there."
}
$binaries = Join-Path $appRoot 'src-tauri\binaries'
New-Item -ItemType Directory -Force -Path $binaries | Out-Null
$destination = Join-Path $binaries "si-sidecar-$Target$extension"
Copy-Item -LiteralPath $published -Destination $destination -Force
# Signed as soon as it exists, so the release never depends on the bundler covering externalBin.
if ($extension -eq '.exe') {
    if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
        Fail 'node wasn''t found, which signs the sidecar (scripts/sign.mjs).'
    }
    & node (Join-Path $PSScriptRoot 'sign.mjs') $destination
    if ($LASTEXITCODE -ne 0) {
        Fail 'signing the sidecar failed.'
    }
}
$megabytes = [math]::Round((Get-Item -LiteralPath $destination).Length / 1MB, 1)
$aheadOfTime = if ($NoReadyToRun) { '' } else { ', ReadyToRun' }
Write-Host "build-sidecar: $destination ($megabytes MB, $runtime, $Configuration$aheadOfTime)"
