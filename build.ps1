# FM26 Accessibility Mod - Build Script
# Run this from PowerShell in the project root directory

param(
    [switch]$Release,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

Write-Host "FM26 Access Build Script" -ForegroundColor Cyan
Write-Host "========================" -ForegroundColor Cyan

if ($Clean) {
    Write-Host "Cleaning build artifacts..." -ForegroundColor Yellow
    if (Test-Path "FM26Access\bin") { Remove-Item -Recurse -Force "FM26Access\bin" }
    if (Test-Path "FM26Access\obj") { Remove-Item -Recurse -Force "FM26Access\obj" }
    Write-Host "Clean complete." -ForegroundColor Green
}

$config = if ($Release) { "Release" } else { "Debug" }
Write-Host "Building in $config configuration..." -ForegroundColor Yellow

# Check if Tolk.dll exists
$tolkPath = "FM26Access\libs\Tolk.dll"
if (-not (Test-Path $tolkPath)) {
    Write-Host ""
    Write-Host "WARNING: Tolk.dll not found!" -ForegroundColor Red
    Write-Host "NVDA integration will not work without Tolk.dll" -ForegroundColor Red
    Write-Host ""
    Write-Host "To download Tolk:" -ForegroundColor Yellow
    Write-Host "1. Go to: https://github.com/dkager/tolk/releases" -ForegroundColor White
    Write-Host "2. Download the latest release zip" -ForegroundColor White
    Write-Host "3. Extract Tolk.dll (x64 version) to: $tolkPath" -ForegroundColor White
    Write-Host ""
}

# Build the project
dotnet build FM26Access.sln -c $config

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Build successful!" -ForegroundColor Green
    Write-Host ""
    Write-Host "The plugin has been copied to:" -ForegroundColor Cyan
    Write-Host "F:\Steam\steamapps\common\Football Manager 26 Demo\BepInEx\plugins\FM26Access\" -ForegroundColor White
    Write-Host ""
    Write-Host "To test:" -ForegroundColor Yellow
    Write-Host "1. Make sure NVDA is running" -ForegroundColor White
    Write-Host "2. Launch Football Manager 26 Demo" -ForegroundColor White
    Write-Host "3. Wait for 'FM26 Access loaded' announcement" -ForegroundColor White
    Write-Host "4. After ~5 seconds, UI scan will complete" -ForegroundColor White
    Write-Host "5. Check BepInEx\UIDiscovery.txt for results" -ForegroundColor White
} else {
    Write-Host ""
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
