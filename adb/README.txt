===========================================
   ADB (Android Debug Bridge) Setup
===========================================

To enable Android device connectivity, you need to place ADB files in this folder.

REQUIRED FILES:
---------------
1. adb.exe
2. AdbWinApi.dll
3. AdbWinUsbApi.dll

HOW TO GET ADB FILES:
---------------------

Option 1: Direct Download (Recommended)
1. Visit: https://developer.android.com/studio/releases/platform-tools
2. Download "SDK Platform-Tools for Windows"
3. Extract the ZIP file
4. Copy these 3 files from the extracted folder:
   - adb.exe
   - AdbWinApi.dll
   - AdbWinUsbApi.dll
5. Paste them into this "adb" folder

Option 2: Using Command Line (PowerShell)
Run this command in PowerShell from the project root:
  Invoke-WebRequest -Uri "https://dl.google.com/android/repository/platform-tools-latest-windows.zip" -OutFile "platform-tools.zip"
  Expand-Archive -Path "platform-tools.zip" -DestinationPath "temp-adb"
  Copy-Item "temp-adb\platform-tools\adb.exe" -Destination "adb\"
  Copy-Item "temp-adb\platform-tools\AdbWinApi.dll" -Destination "adb\"
  Copy-Item "temp-adb\platform-tools\AdbWinUsbApi.dll" -Destination "adb\"
  Remove-Item -Recurse -Force "temp-adb"
  Remove-Item "platform-tools.zip"

VERIFICATION:
-------------
After placing the files, run the application again.
The application will automatically detect ADB in this folder.

NOTE:
-----
The application will use ADB from this folder first,
then check system-wide installations if not found here.

