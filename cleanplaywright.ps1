Write-Host "Cleaning Playwright installation..." -ForegroundColor Cyan
Write-Host ""

# Check if app is running
$process = Get-Process -Name "ShoutingIguana" -ErrorAction SilentlyContinue
if ($process) {
    Write-Host "WARNING: ShoutingIguana is currently running!" -ForegroundColor Yellow
    Write-Host "Please close the app and run this script again." -ForegroundColor Yellow
    exit 1
}

# Clean Playwright browser cache (primary location - Windows default)
$playwrightCache = "$env:LOCALAPPDATA\ms-playwright"
Write-Host "Checking: $playwrightCache"
if (Test-Path $playwrightCache) {
    try {
        $items = Get-ChildItem -Path $playwrightCache -Recurse -ErrorAction SilentlyContinue | Measure-Object
        Write-Host "  Found $($items.Count) files/folders"
        Remove-Item -Path $playwrightCache -Recurse -Force -ErrorAction Stop
        Write-Host "SUCCESS: Removed Playwright cache" -ForegroundColor Green
    }
    catch {
        Write-Host "ERROR: Failed to remove cache - $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Try running PowerShell as Administrator" -ForegroundColor Yellow
    }
} else {
    Write-Host "  No cache found (already clean)" -ForegroundColor Gray
}

# Check alternative location (Linux/Mac style on Windows)
$playwrightCache2 = "$env:USERPROFILE\.cache\ms-playwright"
Write-Host "Checking: $playwrightCache2"
if (Test-Path $playwrightCache2) {
    try {
        Remove-Item -Path $playwrightCache2 -Recurse -Force -ErrorAction Stop
        Write-Host "SUCCESS: Removed Playwright cache (.cache)" -ForegroundColor Green
    }
    catch {
        Write-Host "ERROR: Failed to remove cache - $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "  No cache found" -ForegroundColor Gray
}

# Remove Shouting Iguana settings
$settingsFile = "$env:LOCALAPPDATA\ShoutingIguana\settings.json"
Write-Host "Checking: $settingsFile"
if (Test-Path $settingsFile) {
    try {
        Remove-Item -Path $settingsFile -Force -ErrorAction Stop
        Write-Host "SUCCESS: Removed Shouting Iguana settings" -ForegroundColor Green
    }
    catch {
        Write-Host "ERROR: Failed to remove settings - $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "  No settings file found (already clean)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host "Cleanup complete!" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next time you run the app:" -ForegroundColor White
Write-Host "  1. You'll see the loading overlay" -ForegroundColor Gray
Write-Host "  2. Playwright will download Chromium (~150MB)" -ForegroundColor Gray
Write-Host "  3. This takes 1-2 minutes depending on your connection" -ForegroundColor Gray
