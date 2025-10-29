# Android File Recovery Tool

A C# WPF-based GUI application for recovering deleted files from Android devices.

## Features

- 🔍 **Automatic Device Detection**: Detects connected Android devices via ADB
- 📱 **Multiple File Types**: Support for photos, videos, documents, audio, and other files
- 💾 **Selective Recovery**: Choose specific files or recover all at once
- 🎨 **Modern UI**: Clean and intuitive user interface
- ⚡ **Real-time Progress**: See scan and recovery progress in real-time
- 📦 **Bundled ADB**: ADB included with the project - no separate installation needed!

## Requirements

1. **.NET 9.0 SDK** or later (you have this installed ✓)
2. **ADB Setup**: Follow the instructions below to setup ADB

## ADB Setup Instructions

### Automatic Setup (Recommended)

1. Open PowerShell in the project folder
2. Run the setup script:
   ```powershell
   .\adb\download-adb.ps1
   ```
3. The script will automatically download and setup ADB in the `adb` folder

### Manual Setup

1. Download Android SDK Platform Tools from:
   https://developer.android.com/studio/releases/platform-tools

2. Extract the ZIP file

3. Copy these 3 files to the `adb` folder in the project:
   - `adb.exe`
   - `AdbWinApi.dll`
   - `AdbWinUsbApi.dll`

4. The application will automatically detect and use ADB from the `adb` folder

## Building and Running

```bash
# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run
```

Or build a release version:

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

## Usage

1. **Setup ADB**: Ensure ADB files are in the `adb` folder (see above)

2. **Connect Your Device**: Connect your Android device via USB

3. **Enable USB Debugging** on your device:
   - Go to Settings → About Phone
   - Tap "Build Number" 7 times to enable Developer Options
   - Go to Settings → Developer Options
   - Enable "USB Debugging"

4. **In the Application**:
   - Click "🔄 Refresh Devices" button
   - Select your device from the list
   - Choose which file types to recover (Photos, Videos, Documents, etc.)
   - Enter storage path (default: `/sdcard`)
   - Click "🔍 Start Recovery Scan"
   - Select files and click "💾 Recover Selected" or "💾 Recover All"
   - Choose destination folder to save recovered files

## Supported File Types

- **Photos**: JPG, PNG
- **Videos**: MP4, AVI
- **Documents**: PDF, DOC, DOCX
- **Audio**: MP3, WAV
- **Other**: All other file types

## Project Structure

```
AndroidRecoveryTool/
├── adb/                  # ADB files folder (place ADB here)
│   ├── download-adb.ps1  # Automatic ADB download script
│   └── README.txt        # ADB setup instructions
├── Models/               # Data models
├── Services/             # ADB and recovery services
├── App.xaml              # Application definition
├── MainWindow.xaml       # Main UI window
└── AndroidRecoveryTool.csproj
```

## Notes

⚠️ **Important Limitations**:

- **Root Access**: Full recovery of deleted files requires root access on your Android device
- **Without Root**: The tool can only recover files that are still accessible through normal file system operations
- **Deleted Files**: Truly deleted files (overwritten by the system) may not be recoverable without root access
- **Storage Access**: The tool scans the specified storage path (default: /sdcard)

## Troubleshooting

### ADB Not Found
- Ensure ADB files are placed in the `adb` folder
- Run the `download-adb.ps1` script to automatically setup ADB
- Check that `adb.exe`, `AdbWinApi.dll`, and `AdbWinUsbApi.dll` are all present

### Device Not Detected
- Ensure USB debugging is enabled on your device
- Check USB cable connection
- Try different USB ports
- Verify device drivers are installed
- Make sure you authorize the computer when prompted on your device

### No Files Found
- Ensure you've selected at least one file type
- Verify the storage path is correct (try `/sdcard` or `/storage/emulated/0`)
- Check if device has root access for deleted file recovery

## Technical Details

- **Framework**: .NET 9.0
- **UI**: WPF (Windows Presentation Foundation)
- **Architecture**: Service-based with separation of concerns
- **ADB Integration**: Uses bundled ADB from project folder, falls back to system-wide installation

## License

This project is provided as-is for educational and recovery purposes.

## Disclaimer

This tool is intended for recovering your own files from your own devices. Always ensure you have proper authorization before attempting file recovery on any device.

