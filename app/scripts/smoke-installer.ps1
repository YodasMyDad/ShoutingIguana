<#
.SYNOPSIS
Installs the NSIS bundle silently for the current user, checks it, runs it, and uninstalls it (Windows).

.DESCRIPTION
1. Runs the installer with /S into a fresh temporary folder (/D=), as a per-user install.
2. Checks the install: the shell and the sidecar are there, the uninstaller is registered under HKCU, and Windows
   opens .sicrawl files with the installed app (the merged HKEY_CLASSES_ROOT view, as Explorer reads it).
3. Runs the installed app through smoke-tauri.mjs (--exe), on temporary data, state and profile folders: it shows the
   library, closes, and leaves no sidecar behind.
4. Runs the uninstaller with /S and checks the folder, the registration, the association and the shortcuts are gone.

Exits 1 on any failure. Whatever happens, an install it made is uninstalled.

.PARAMETER Installer
The setup .exe. Defaults to the newest in src-tauri/target/release/bundle/nsis.
#>
param([string]$Installer)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$InstallTimeout = [TimeSpan]::FromSeconds(120)
$UninstallTimeout = [TimeSpan]::FromSeconds(60)
$FileClass = 'Shouting Iguana crawl'

function Fail([string]$Message) {
    [Console]::Error.WriteLine("smoke-installer: FAIL $Message")
    $script:failed = $true
}

$appRoot = Split-Path -Parent $PSScriptRoot
if (-not $Installer) {
    $bundles = Join-Path $appRoot 'src-tauri\target\release\bundle\nsis'
    $Installer = Get-ChildItem -LiteralPath $bundles -Filter '*-setup.exe' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $Installer) {
        [Console]::Error.WriteLine("smoke-installer: no installer in ${bundles}: run npm run tauri build first, or pass -Installer.")
        exit 1
    }
}
$Installer = (Resolve-Path -LiteralPath $Installer).Path
# The product's name, as the installer names its folder, uninstall key and shortcuts: the setup file's name before
# "_<version>".
$product = ([IO.Path]::GetFileNameWithoutExtension($Installer)) -replace '_[^_]+_[^_]+-setup$', ''
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$product"
# Where the installer remembers its folder (HKCU\Software\<publisher>\<product>): the next install goes there unless
# told otherwise, and the uninstaller leaves it. Left pointing at this test's deleted folder, it sent the next real
# install into the temp folder, so the test puts it back as it found it.
$publisher = (Get-Content -Raw -LiteralPath (Join-Path $appRoot 'src-tauri\tauri.conf.json') | ConvertFrom-Json).bundle.publisher
$publisherKey = "HKCU:\Software\$publisher"
$rememberedKey = "$publisherKey\$product"
$hadPublisherKey = Test-Path -LiteralPath $publisherKey
$hadRememberedKey = Test-Path -LiteralPath $rememberedKey
$rememberedFolder = if ($hadRememberedKey) { (Get-Item -LiteralPath $rememberedKey).GetValue('') } else { $null }
$installDir = Join-Path ([IO.Path]::GetTempPath()) "si-install-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$script:failed = $false
$installed = $false

# A real install here shares this test's registration, .sicrawl association and shortcuts: the test's install would
# take them over and its uninstall would remove them, so it doesn't run beside one.
if (Test-Path -LiteralPath $uninstallKey) {
    $existing = (Get-Item -LiteralPath $uninstallKey).GetValue('UninstallString')
    [Console]::Error.WriteLine("smoke-installer: $product is installed for this user ($existing): uninstall it first, or run this on another account or machine.")
    exit 1
}

function Wait-Until([scriptblock]$Condition, [TimeSpan]$Timeout) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while (-not (& $Condition)) {
        if ($watch.Elapsed -ge $Timeout) { return $false }
        Start-Sleep -Milliseconds 250
    }
    return $true
}

function Get-Default([string]$Key) {
    if (-not (Test-Path -LiteralPath $Key)) { return $null }
    (Get-Item -LiteralPath $Key).GetValue('')
}

try {
    Write-Host "smoke-installer: $Installer ($product) into $installDir"

    # 1. A silent per-user install. NSIS reads /D= raw: last, and unquoted even with spaces.
    $setup = Start-Process -FilePath $Installer -ArgumentList '/S', "/D=$installDir" -PassThru -Wait
    $installed = $true
    if ($setup.ExitCode -ne 0) { Fail "the installer exited with code $($setup.ExitCode)." }
    $shell = Join-Path $installDir 'shouting-iguana.exe'
    $sidecar = Join-Path $installDir 'si-sidecar.exe'
    if (-not (Wait-Until { (Test-Path -LiteralPath $shell) -and (Test-Path -LiteralPath $sidecar) } $InstallTimeout)) {
        Fail "the install has no shouting-iguana.exe and si-sidecar.exe in $installDir."
    }
    else {
        # 2. Registered for this user, and .sicrawl opens with the installed app.
        $uninstallString = if (Test-Path -LiteralPath $uninstallKey) { (Get-Item -LiteralPath $uninstallKey).GetValue('UninstallString') } else { $null }
        if (-not $uninstallString -or $uninstallString -notlike "*$installDir*") {
            Fail "the uninstaller isn't registered at $uninstallKey (UninstallString: $uninstallString)."
        }
        $class = Get-Default 'Registry::HKEY_CLASSES_ROOT\.sicrawl'
        $command = Get-Default "Registry::HKEY_CLASSES_ROOT\$FileClass\shell\open\command"
        if ($class -ne $FileClass -or -not $command -or $command -notlike "*$shell*%1*") {
            Fail ".sicrawl doesn't open with the installed app (class: $class; command: $command)."
        }
        else {
            Write-Host "smoke-installer: installed; .sicrawl opens with $command"
        }

        # 3. The installed app runs: it shows the library, and closing it leaves no sidecar.
        # A GitHub-hosted Windows job has no interactive desktop, so WebView2 never opens a debugging port.
        # The install, the file association and the uninstall are still checked. A desktop run still fails here.
        & node (Join-Path $PSScriptRoot 'smoke-tauri.mjs') --exe $shell
        if ($LASTEXITCODE -ne 0) {
            if ($env:GITHUB_ACTIONS -eq 'true') {
                Write-Host 'smoke-installer: the installed app did not open a window on this runner. GitHub-hosted Windows jobs have no interactive desktop, so this check is the one that runs on a real machine.'
            }
            else {
                Fail 'the installed app failed its smoke test.'
            }
        }
    }
}
finally {
    if ($installed) {
        # 4. A silent uninstall. The uninstaller copies itself to the temp folder and runs from there, so the wait is
        # for its work, not its process.
        $uninstaller = Join-Path $installDir 'uninstall.exe'
        if (Test-Path -LiteralPath $uninstaller) {
            Start-Process -FilePath $uninstaller -ArgumentList '/S' -Wait | Out-Null
        }
        else {
            Fail "there's no uninstaller in $installDir."
        }
        $gone = Wait-Until { -not (Test-Path -LiteralPath $installDir) -and -not (Test-Path -LiteralPath $uninstallKey) } $UninstallTimeout
        if (-not $gone) { Fail "the uninstall left $installDir or $uninstallKey behind." }
        if (Test-Path -LiteralPath "Registry::HKEY_CURRENT_USER\Software\Classes\$FileClass") {
            Fail '.sicrawl still opens with the uninstalled app.'
        }
        foreach ($shortcut in (Join-Path ([Environment]::GetFolderPath('Desktop')) "$product.lnk"),
            (Join-Path ([Environment]::GetFolderPath('Programs')) "$product.lnk")) {
            if (Test-Path -LiteralPath $shortcut) { Fail "the uninstall left the shortcut $shortcut." }
        }
        if ($gone) { Write-Host 'smoke-installer: uninstalled' }
    }
    # The installer's remembered folder, as it was before the test.
    if ($hadRememberedKey) {
        Set-Item -LiteralPath $rememberedKey -Value $rememberedFolder
    }
    elseif (Test-Path -LiteralPath $rememberedKey) {
        Remove-Item -LiteralPath $rememberedKey -Recurse
        if (-not $hadPublisherKey -and -not (Get-ChildItem -LiteralPath $publisherKey) -and
            -not (Get-Item -LiteralPath $publisherKey).ValueCount) {
            Remove-Item -LiteralPath $publisherKey
        }
    }
    if ((Test-Path -LiteralPath $rememberedKey) -and (Get-Item -LiteralPath $rememberedKey).GetValue('') -like "*$installDir*") {
        Fail "the installer's remembered folder ($rememberedKey) still points at the test's $installDir."
    }
}

if ($script:failed) {
    [Console]::Error.WriteLine('smoke-installer: FAIL')
    exit 1
}
Write-Host 'smoke-installer: PASS'
