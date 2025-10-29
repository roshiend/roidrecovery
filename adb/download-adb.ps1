# PowerShell script to automatically download and setup ADB
Write-Host "Downloading Android SDK Platform Tools..." -ForegroundColor Cyan

# Create temp directory
$tempDir = "$PSScriptRoot\temp-adb"
$zipFile = "$PSScriptRoot\platform-tools.zip"

# Download platform tools
try {
    Invoke-WebRequest -Uri "https://dl.google.com/android/repository/platform-tools-latest-windows.zip" -OutFile $zipFile -UseBasicParsing
    Write-Host "Download complete!" -ForegroundColor Green
} catch {
    Write-Host "Error downloading: $_" -ForegroundColor Red
    exit 1
}

# Extract
Write-Host "Extracting files..." -ForegroundColor Cyan
Expand-Archive -Path $zipFile -DestinationPath $tempDir -Force

# Copy required files
Write-Host "Copying ADB files..." -ForegroundColor Cyan
Copy-Item "$tempDir\platform-tools\adb.exe" -Destination "$PSScriptRoot\" -Force
Copy-Item "$tempDir\platform-tools\AdbWinApi.dll" -Destination "$PSScriptRoot\" -Force
Copy-Item "$tempDir\platform-tools\AdbWinUsbApi.dll" -Destination "$PSScriptRoot\" -Force

# Cleanup
Write-Host "Cleaning up..." -ForegroundColor Cyan
Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
Remove-Item $zipFile -ErrorAction SilentlyContinue

Write-Host "`nADB setup complete!" -ForegroundColor Green
Write-Host "Files are ready in: $PSScriptRoot" -ForegroundColor Green
Write-Host "You can now run the Android Recovery Tool application." -ForegroundColor Green

