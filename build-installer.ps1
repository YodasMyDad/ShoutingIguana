# Build script for Shouting Iguana
# Creates x86 and/or x64 builds and generates installers using Inno Setup

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("x86", "x64", "Both")]
    [string]$Platform = "Both",
    
    [Parameter(Mandatory=$false)]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectPath = "src\ShoutingIguana\ShoutingIguana.csproj"
$InnoSetupCompiler = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

# Check if Inno Setup is installed
if (-not (Test-Path $InnoSetupCompiler)) {
    Write-Error "Inno Setup compiler not found at: $InnoSetupCompiler"
    Write-Host "Please ensure Inno Setup 6 is installed."
    exit 1
}

# Check if project exists
if (-not (Test-Path $ProjectPath)) {
    Write-Error "Project file not found at: $ProjectPath"
    exit 1
}

function Build-Platform {
    param([string]$Arch)
    
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "Building $Arch version..." -ForegroundColor Cyan
    Write-Host "========================================`n" -ForegroundColor Cyan
    
    $runtime = "win-$Arch"
    $outputPath = "publish\$Arch"
    
    # Restore
    Write-Host "Restoring NuGet packages for $Arch..." -ForegroundColor Yellow
    dotnet restore $ProjectPath -r $runtime
    
    # Publish
    Write-Host "Publishing $Arch build..." -ForegroundColor Yellow
    dotnet publish $ProjectPath `
        -c $Configuration `
        -r $runtime `
        -o $outputPath `
        --self-contained true `
        /p:PublishSingleFile=false `
        /p:IncludeNativeLibrariesForSelfExtract=true
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for $Arch"
        exit 1
    }
    
    Write-Host "Build completed successfully for $Arch" -ForegroundColor Green
    
    # Create installer
    Write-Host "`nCreating installer for $Arch..." -ForegroundColor Yellow
    
    $installerArgs = "/DPlatform=$Arch", "Installer.iss"
    & $InnoSetupCompiler $installerArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Installer creation failed for $Arch"
        exit 1
    }
    
    Write-Host "Installer created successfully for $Arch" -ForegroundColor Green
}

# Main execution
Write-Host "`n========================================" -ForegroundColor Magenta
Write-Host "Shouting Iguana Build Script" -ForegroundColor Magenta
Write-Host "Configuration: $Configuration" -ForegroundColor Magenta
Write-Host "Platform: $Platform" -ForegroundColor Magenta
Write-Host "========================================`n" -ForegroundColor Magenta

# Clean solution first
Write-Host "Cleaning solution..." -ForegroundColor Yellow
dotnet clean $ProjectPath -c $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Error "Solution clean failed"
    exit 1
}

# Build solution to ensure it compiles
Write-Host "Building solution to verify..." -ForegroundColor Yellow
dotnet build $ProjectPath -c $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Error "Solution build failed"
    exit 1
}

Write-Host "Solution build successful`n" -ForegroundColor Green

# Clean publish directory
if (Test-Path "publish") {
    Write-Host "Cleaning publish directory..." -ForegroundColor Yellow
    Remove-Item -Path "publish" -Recurse -Force
}

# Create fresh publish directory structure
Write-Host "Creating publish directories..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path "publish" | Out-Null
New-Item -ItemType Directory -Path "publish\installer" | Out-Null

# Build requested platforms
if ($Platform -eq "Both") {
    Build-Platform "x64"
    Build-Platform "x86"
} else {
    Build-Platform $Platform
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "BUILD COMPLETED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "`nInstallers are located in: publish\installer\" -ForegroundColor Cyan

if ($Platform -eq "Both") {
    Write-Host "  - ShoutingIguana-x64.exe" -ForegroundColor White
    Write-Host "  - ShoutingIguana-x86.exe" -ForegroundColor White
} else {
    Write-Host "  - ShoutingIguana-$Platform.exe" -ForegroundColor White
}

Write-Host ""

