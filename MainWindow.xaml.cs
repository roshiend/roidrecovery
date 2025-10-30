using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Security.Cryptography;
using System.Text;
using AndroidRecoveryTool.Models;
using AndroidRecoveryTool.Services;

namespace AndroidRecoveryTool
{
    public partial class MainWindow : Window
    {
        private readonly AdbService _adbService;
        private readonly FileRecoveryService _recoveryService;
        private readonly ObservableCollection<AndroidDevice> _devices;
        private readonly ObservableCollection<RecoverableFile> _recoverableFiles;
        private string? _selectedDeviceId;
        private string _recoveryDestinationPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        private readonly SemaphoreSlim _thumbLimiter = new SemaphoreSlim(2);
        private readonly bool _enableVideoThumbnails = false; // optimization: skip heavy video thumbnailing by default

        public MainWindow()
        {
            InitializeComponent();
            
            _adbService = new AdbService();
            _recoveryService = new FileRecoveryService(_adbService);
            _devices = new ObservableCollection<AndroidDevice>();
            _recoverableFiles = new ObservableCollection<RecoverableFile>();

            lstDevices.ItemsSource = _devices;
            dgFiles.ItemsSource = _recoverableFiles;
            
            // Set DataContext for thumbnail binding
            dgFiles.DataContext = _recoverableFiles;

            _recoveryService.FileFound += OnFileFound;
            _recoveryService.ProgressUpdate += OnProgressUpdate;

            CheckAdbAvailability();
            _ = RefreshDevicesAsync();
        }

        private void CheckAdbAvailability()
        {
            if (!_adbService.IsAdbAvailable())
            {
                var adbFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb");
                var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "adb", "download-adb.ps1");
                
                var message = "ADB (Android Debug Bridge) not found!\n\n" +
                    "To automatically download and setup ADB:\n" +
                    $"1. Open PowerShell in the project folder\n" +
                    $"2. Run: .\\adb\\download-adb.ps1\n\n" +
                    "OR manually setup:\n" +
                    "1. Download from: https://developer.android.com/studio/releases/platform-tools\n" +
                    $"2. Extract and copy adb.exe, AdbWinApi.dll, AdbWinUsbApi.dll to:\n{adbFolder}\n\n" +
                    "The application will automatically use ADB from the project folder.\n\n" +
                    "Without ADB, this tool cannot connect to Android devices.";

                txtStatus.Text = "ADB not found. Please setup ADB using the instructions shown.";
                
                System.Windows.MessageBox.Show(
                    message,
                    "ADB Not Found - Setup Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                var adbPath = _adbService.GetAdbPath();
                txtStatus.Text = $"ADB found at: {Path.GetDirectoryName(adbPath)}";
            }
        }

        private async void BtnRefreshDevices_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDevicesAsync();
        }

        private async Task RefreshDevicesAsync()
        {
            btnRefreshDevices.IsEnabled = false;
            txtStatus.Text = "Refreshing device list...";

            try
            {
                var devices = await _adbService.GetConnectedDevicesAsync();
                _devices.Clear();
                
                foreach (var device in devices)
                {
                    _devices.Add(device);
                }

                txtStatus.Text = devices.Count > 0
                    ? $"{devices.Count} device(s) found. Select a device to begin."
                    : "No devices found. Connect your Android device via USB and enable USB debugging.";
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Error refreshing devices: {ex.Message}";
            }
            finally
            {
                btnRefreshDevices.IsEnabled = true;
            }
        }

        private void LstDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstDevices.SelectedItem is AndroidDevice selectedDevice)
            {
                _selectedDeviceId = selectedDevice.DeviceId;
                btnStartScan.IsEnabled = selectedDevice.IsAuthorized;
                btnRevokeAuth.IsEnabled = !string.IsNullOrEmpty(selectedDevice.DeviceId);
                
                if (selectedDevice.IsAuthorized)
                {
                    txtStatus.Text = $"Device selected: {selectedDevice.DeviceName} ({selectedDevice.DeviceId})";
                }
                else
                {
                    var message = "⚠️ Device UNAUTHORIZED!\n\n" +
                        "SOLUTION - Try these steps:\n\n" +
                        "STEP 1: On your Android phone:\n" +
                        "• Check your phone screen for a popup: 'Allow USB debugging?'\n" +
                        "• If you see it: Check 'Always allow' and tap 'Allow' or 'OK'\n\n" +
                        "STEP 2: If no popup appears:\n" +
                        "• Unplug the USB cable\n" +
                        "• Wait 2 seconds\n" +
                        "• Plug it back in\n" +
                        "• Watch your phone screen for the popup\n\n" +
                        "STEP 3: If still no popup:\n" +
                        "• Click 'Revoke Authorization' button (forces new auth)\n" +
                        "• Unplug and replug USB cable\n" +
                        "• The popup should appear now\n\n" +
                        "STEP 4: Advanced (if still not working):\n" +
                        "• Settings → Developer Options → Revoke USB debugging authorizations\n" +
                        "• Unplug and replug USB\n" +
                        "• Allow when prompted\n\n" +
                        "After authorizing, click 'Refresh Devices' button.";
                    
                    txtStatus.Text = "Device not authorized. Check your phone screen for 'Allow USB debugging?' popup.";
                    
                    System.Windows.MessageBox.Show(
                        message,
                        "Device Authorization Required - READ CAREFULLY",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            else
            {
                _selectedDeviceId = null;
                btnStartScan.IsEnabled = false;
                btnRevokeAuth.IsEnabled = false;
            }
        }
        
        private async void BtnRestartAdb_Click(object sender, RoutedEventArgs e)
        {
            btnRestartAdb.IsEnabled = false;
            txtStatus.Text = "Restarting ADB server...";
            
            try
            {
                await _adbService.RestartAdbServerAsync();
                txtStatus.Text = "ADB server restarted. UNPLUG and REPLUG your USB cable, then check your phone for the dialog.";
                
                System.Windows.MessageBox.Show(
                    "ADB server has been restarted.\n\n" +
                    "IMPORTANT: Unplug and replug your USB cable NOW!\n\n" +
                    "1. Unplug USB cable from phone\n" +
                    "2. Wait 2 seconds\n" +
                    "3. Plug USB cable back in\n" +
                    "4. Watch your phone screen - you should see 'Allow USB debugging?' popup\n" +
                    "5. Tap 'Allow' (check 'Always allow' if option appears)\n" +
                    "6. Click 'Refresh Devices' button here",
                    "ADB Server Restarted - UNPLUG AND REPLUG USB NOW!",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                    
                await RefreshDevicesAsync();
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Error restarting ADB: {ex.Message}";
            }
            finally
            {
                btnRestartAdb.IsEnabled = true;
            }
        }
        
        private async void BtnRevokeAuth_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDeviceId == null)
                return;
                
            var result = System.Windows.MessageBox.Show(
                "This will revoke USB debugging authorization and force a new authorization request.\n\n" +
                "After clicking OK:\n" +
                "1. UNPLUG and REPLUG your USB cable\n" +
                "2. Your phone will show 'Allow USB debugging?' popup\n" +
                "3. Tap 'Allow' on your phone\n" +
                "4. Click 'Refresh Devices' here\n\n" +
                "Continue?",
                "Revoke USB Debugging Authorization",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
                
            if (result != MessageBoxResult.Yes)
                return;
                
            btnRevokeAuth.IsEnabled = false;
            txtStatus.Text = "Revoking authorization...";
            
            try
            {
                // Kill ADB server to clear connection
                await _adbService.RestartAdbServerAsync();
                
                // Try to revoke via ADB (may not work without root, but triggers re-auth)
                var revokeResult = await _adbService.ExecuteShellCommandAsync(_selectedDeviceId, "settings get global adb_enabled");
                
                txtStatus.Text = "Authorization revoked. UNPLUG and REPLUG USB cable NOW!";
                
                System.Windows.MessageBox.Show(
                    "⚠️ Authorization has been revoked!\n\n" +
                    "ACTION REQUIRED:\n\n" +
                    "1. UNPLUG USB cable from your phone RIGHT NOW\n" +
                    "2. Wait 3 seconds\n" +
                    "3. PLUG USB cable back in\n" +
                    "4. Watch your phone screen - a popup WILL appear\n" +
                    "5. On phone popup: Check 'Always allow' and tap 'Allow'\n" +
                    "6. Come back here and click 'Refresh Devices'",
                    "UNPLUG AND REPLUG USB CABLE NOW!",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                    
                await Task.Delay(2000);
                await RefreshDevicesAsync();
            }
            catch
            {
                txtStatus.Text = $"Note: Forcing re-authorization. Unplug and replug USB, then refresh devices.";
                
                System.Windows.MessageBox.Show(
                    "Attempting to force re-authorization...\n\n" +
                    "Now UNPLUG and REPLUG your USB cable.\n" +
                    "Your phone should show the authorization popup.",
                    "Unplug and Replug USB Cable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            finally
            {
                btnRevokeAuth.IsEnabled = true;
            }
        }

        private async void BtnStartScan_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDeviceId == null)
            {
                System.Windows.MessageBox.Show("Please select a device first.", "No Device Selected", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool scanPhotos = chkPhotos.IsChecked ?? false;
            bool scanVideos = chkVideos.IsChecked ?? false;
            bool scanDocuments = chkDocuments.IsChecked ?? false;
            bool scanAudio = chkAudio.IsChecked ?? false;
            bool scanOther = chkOther.IsChecked ?? false;

            if (!scanPhotos && !scanVideos && !scanDocuments && !scanAudio && !scanOther)
            {
                System.Windows.MessageBox.Show("Please select at least one file type to recover.", 
                    "No File Types Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _recoverableFiles.Clear();
            txtFileCount.Text = "(0 files)";

            btnStartScan.IsEnabled = false;
            btnStopScan.IsEnabled = true;
            txtStatus.Text = "Scanning for recoverable files...";

            try
            {
                var storagePath = txtStoragePath.Text.Trim();
                if (string.IsNullOrEmpty(storagePath))
                    storagePath = "/sdcard";

                var files = await _recoveryService.ScanForDeletedFilesAsync(
                    _selectedDeviceId,
                    storagePath,
                    scanPhotos,
                    scanVideos,
                    scanDocuments,
                    scanAudio,
                    scanOther
                );

                UpdateFileCount();
                txtStatus.Text = $"Scan complete. Found {files.Count} recoverable files.";
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Error during scan: {ex.Message}";
                System.Windows.MessageBox.Show($"Scan error: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnStartScan.IsEnabled = true;
                btnStopScan.IsEnabled = false;
            }
        }

        private void BtnStopScan_Click(object sender, RoutedEventArgs e)
        {
            _recoveryService.CancelScan();
            btnStopScan.IsEnabled = false;
            btnStartScan.IsEnabled = true;
            txtStatus.Text = "Scan cancelled.";
        }

        private void OnFileFound(object? sender, RecoverableFile file)
        {
            Dispatcher.Invoke(() =>
            {
                _recoverableFiles.Add(file);
                UpdateFileCount();
            });
            
            // Load thumbnail for the file asynchronously
            _ = LoadThumbnailAsync(file);
        }

        private async Task LoadThumbnailAsync(RecoverableFile file)
        {
            try
            {
                if (_selectedDeviceId == null)
                {
                    return;
                }

                // Only load thumbnails for images and videos
                var fileType = file.FileType.ToUpper();
                bool isImage = fileType == "JPG" || fileType == "JPEG" || fileType == "PNG" || fileType == "GIF" || fileType == "WEBP";
                bool isVideo = fileType == "MP4" || fileType == "AVI" || fileType == "MKV" || fileType == "MOV" || 
                              fileType == "M4V" || fileType == "FLV" || fileType == "WMV" || fileType == "3GP";
                bool isAudio = fileType == "MP3" || fileType == "WAV" || fileType == "M4A" || fileType == "AAC" || fileType == "OGG" || fileType == "FLAC";
                bool isDoc = fileType == "PDF" || fileType == "DOC" || fileType == "DOCX" || fileType == "PPT" || fileType == "PPTX" || fileType == "XLS" || fileType == "XLSX" || fileType == "TXT";

                if (!isImage && !isVideo && !isAudio && !isDoc) return;

                // Check cache first
                var cachePath = GetThumbnailCachePath(_selectedDeviceId, file);
                if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
                {
                    Dispatcher.Invoke(() => { file.ThumbnailPath = cachePath; });
                    return;
                }

                await _thumbLimiter.WaitAsync();
                try
                {
                // Create thumbnail directory
                var thumbDir = Path.Combine(Path.GetTempPath(), "AndroidRecoveryThumbnails");
                Directory.CreateDirectory(thumbDir);
                
                var thumbFileName = $"{Guid.NewGuid()}.{fileType.ToLower()}";
                var thumbPath = Path.Combine(thumbDir, thumbFileName);

                // Try to pull a small thumbnail from device (first few KB should be enough for preview)
                if (isImage)
                {
                    // For images, try to pull the file (it will be scaled by the Image control)
                    var testResult = await _adbService.ExecuteShellCommandAsync(_selectedDeviceId, $"test -f \"{file.DevicePath}\" && echo EXISTS");
                    if (testResult.Contains("EXISTS"))
                    {
                        // Pull thumbnail from device
                        var pullProcess = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = _adbService.GetAdbPath(),
                            Arguments = $"-s {_selectedDeviceId} pull \"{file.DevicePath}\" \"{thumbPath}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            WorkingDirectory = Path.GetDirectoryName(_adbService.GetAdbPath()) ?? ""
                        };

                        using var process = System.Diagnostics.Process.Start(pullProcess);
                        if (process != null)
                        {
                            await process.WaitForExitAsync();
                            await Task.Delay(500);
                            
                            if (File.Exists(thumbPath) && new FileInfo(thumbPath).Length > 0)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    file.ThumbnailPath = thumbPath;
                                });
                                return;
                            }
                        }
                    }
                }
                else if (isVideo)
                {
                    // Optimization: skip video thumbnails by default (heavy and slow on USB)
                    // Always show a generic video placeholder instead of black frames
                    try
                    {
                        if (File.Exists(cachePath))
                        {
                            // Remove any previously cached black thumbnails
                            File.Delete(cachePath);
                        }
                    }
                    catch { }
                    Dispatcher.Invoke(() => { file.ThumbnailPath = string.Empty; });
                    return;
                }
                else if (isAudio)
                {
                    var ok = await ExtractAudioThumbnailAsync(file, cachePath);
                    if (ok)
                    {
                        return;
                    }
                }
                else if (isDoc)
                {
                    var ok = await ExtractDocumentThumbnailAsync(file, cachePath);
                    if (ok)
                    {
                        return;
                    }
                }
                }
                finally
                {
                    _thumbLimiter.Release();
                }
            }
            catch
            {
                // Silently fail - thumbnails are optional
            }
        }

        private async Task<bool> ExtractVideoThumbnailAsync(RecoverableFile file, string cachePath)
        {
            try
            {
                if (_selectedDeviceId == null)
                {
                    return false;
                }

                // Cap work for very large videos (> 200MB) to avoid heavy transfers
                if (file.SizeInBytes > 200 * 1024 * 1024) return false;

                // Create thumbnail directory
                var thumbDir = Path.Combine(Path.GetTempPath(), "AndroidRecoveryThumbnails");
                Directory.CreateDirectory(thumbDir);
                var thumbPath = Path.Combine(thumbDir, $"thumb_{Guid.NewGuid()}.jpg");

                // Check if file exists on device
                var testResult = await _adbService.ExecuteShellCommandAsync(_selectedDeviceId, $"test -f \"{file.DevicePath}\" && echo EXISTS");
                bool existsOnDevice = testResult.Contains("EXISTS");

                // Pull video file temporarily (we'll delete it after extracting frame)
                var tempVideoDir = Path.Combine(Path.GetTempPath(), "AndroidRecoveryVideoThumbs");
                Directory.CreateDirectory(tempVideoDir);
                var extension = Path.GetExtension(file.DevicePath).TrimStart('.').ToLower();
                if (string.IsNullOrEmpty(extension))
                    extension = file.FileType.ToLower();
                var tempVideoPath = Path.Combine(tempVideoDir, $"temp_{Guid.NewGuid()}.{extension}");

                // Try fast path: use ffmpeg if available and pull only first 8 MB (often enough for a keyframe)
                var ffmpegPath = GetFfmpegPath();
                var ffprobePath = GetFfprobePath();
                bool usedFfmpeg = false;
                if (!string.IsNullOrEmpty(ffmpegPath) && File.Exists(ffmpegPath))
                {
                    try
                    {
                        // Attempt partial pull via exec-out + redirection (Windows-only)
                        // This streams first 8MB from device to local file; if it fails, we'll fall back
                        var cmd = $"\"{_adbService.GetAdbPath()}\" -s {_selectedDeviceId} exec-out \"dd if='{file.DevicePath}' bs=1M count=8\" > \"{tempVideoPath}\"";
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = "/c " + cmd,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };
                        using (var proc = System.Diagnostics.Process.Start(psi))
                        {
                            if (proc != null)
                            {
                                await proc.WaitForExitAsync();
                            }
                        }

                        // If partial file is too small or missing, fall back to full pull (only if <= 50MB)
                        var partialOk = File.Exists(tempVideoPath) && new FileInfo(tempVideoPath).Length > (512 * 1024);
                        if (!partialOk)
                        {
                            if (existsOnDevice && file.SizeInBytes <= 50 * 1024 * 1024)
                            {
                                var pullFull = new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = _adbService.GetAdbPath(),
                                    Arguments = $"-s {_selectedDeviceId} pull \"{file.DevicePath}\" \"{tempVideoPath}\"",
                                    UseShellExecute = false,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    CreateNoWindow = true,
                                    WorkingDirectory = Path.GetDirectoryName(_adbService.GetAdbPath()) ?? ""
                                };
                                using var pullProc = System.Diagnostics.Process.Start(pullFull);
                                if (pullProc != null)
                                {
                                    await pullProc.WaitForExitAsync();
                                }
                            }
                        }

                        if (File.Exists(tempVideoPath) && new FileInfo(tempVideoPath).Length > 0)
                        {
                            // Determine a good timestamp via ffprobe (20% of duration), fallback to 1s
                            double ts = 1;
                            if (!string.IsNullOrEmpty(ffprobePath) && File.Exists(ffprobePath))
                            {
                                var dur = await GetVideoDurationSecondsAsync(ffprobePath, tempVideoPath);
                                if (dur > 3) ts = Math.Min(Math.Max(1, dur * 0.2), dur - 1);
                            }

                            // Use ffmpeg to extract a frame (scaled thumbnail)
                            var ffArgs = $"-y -ss {ts.ToString(System.Globalization.CultureInfo.InvariantCulture)} -noaccurate_seek -i \"{tempVideoPath}\" -frames:v 1 -q:v 3 -vf scale=-2:180 \"{thumbPath}\"";
                            var ffpsi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = ffmpegPath,
                                Arguments = ffArgs,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            };
                            using var ff = System.Diagnostics.Process.Start(ffpsi);
                            if (ff != null)
                            {
                                await ff.WaitForExitAsync();
                            }

                            if (File.Exists(thumbPath) && new FileInfo(thumbPath).Length > 0)
                            {
                                usedFfmpeg = true;
                                // Save to cache and set path
                                try { File.Copy(thumbPath, cachePath, true); } catch { }
                                Dispatcher.Invoke(() => { file.ThumbnailPath = cachePath; });
                                return true;
                            }
                            else
                            {
                                // Retry at later timestamps to avoid black frames
                                foreach (var offset in new[] { 3, 5 })
                                {
                                    var ts2 = ts + offset;
                                    var ffArgs2 = $"-y -ss {ts2.ToString(System.Globalization.CultureInfo.InvariantCulture)} -noaccurate_seek -i \"{tempVideoPath}\" -frames:v 1 -q:v 3 -vf scale=-2:180 \"{thumbPath}\"";
                                    var ffpsi2 = new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = ffmpegPath,
                                        Arguments = ffArgs2,
                                        UseShellExecute = false,
                                        RedirectStandardOutput = true,
                                        RedirectStandardError = true,
                                        CreateNoWindow = true
                                    };
                                    using var ff2 = System.Diagnostics.Process.Start(ffpsi2);
                                    if (ff2 != null) await ff2.WaitForExitAsync();
                                    if (File.Exists(thumbPath) && new FileInfo(thumbPath).Length > 0)
                                    {
                                        usedFfmpeg = true;
                                        try { File.Copy(thumbPath, cachePath, true); } catch { }
                                        Dispatcher.Invoke(() => { file.ThumbnailPath = cachePath; });
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                // If ffmpeg path not found or failed, fall back to WPF capture approach
                if (!usedFfmpeg)
                {
                    if (!existsOnDevice)
                    {
                        // If deleted and not recoverable for thumbnail quickly, skip
                        if (file.SizeInBytes > 100 * 1024 * 1024) return false;

                        try
                        {
                            var tempRecoveryDir = tempVideoDir;
                            var fileCopy = new RecoverableFile
                            {
                                DevicePath = file.DevicePath,
                                FileName = file.FileName,
                                FileType = file.FileType,
                                SizeInBytes = file.SizeInBytes,
                                RecoveryStatus = file.RecoveryStatus
                            };

                            var recoverySuccess = await _recoveryService.RecoverFileAsync(_selectedDeviceId!, fileCopy, tempRecoveryDir);
                            if (recoverySuccess && !string.IsNullOrEmpty(fileCopy.DevicePath) && File.Exists(fileCopy.DevicePath))
                            {
                                tempVideoPath = fileCopy.DevicePath; // Use recovered local file
                            }
                            else
                            {
                                return false; // Cannot produce thumbnail
                            }
                        }
                        catch { return false; }
                    }

                    // If tempVideoPath is still empty or missing, attempt a small full pull (<= 50MB)
                    if (!File.Exists(tempVideoPath))
                    {
                        if (!existsOnDevice || file.SizeInBytes > 50 * 1024 * 1024) return false;

                        var pullProcess = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = _adbService.GetAdbPath(),
                            Arguments = $"-s {_selectedDeviceId} pull \"{file.DevicePath}\" \"{tempVideoPath}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            WorkingDirectory = Path.GetDirectoryName(_adbService.GetAdbPath()) ?? ""
                        };

                        using var pullProc = System.Diagnostics.Process.Start(pullProcess);
                        if (pullProc != null)
                        {
                            await pullProc.WaitForExitAsync();
                        }
                    }

                    if (!File.Exists(tempVideoPath)) return false;

                    try
                {
                        // Extract frame using MediaElement on a background thread
                        await Task.Run(() =>
                        {
                                try
                                {
                                    // Use dispatcher to create MediaElement on UI thread
                                    MediaElement? mediaElement = null;
                                    ManualResetEvent frameReady = new ManualResetEvent(false);
                                    bool frameCaptured = false;

                                    Dispatcher.Invoke(() =>
                                    {
                                        mediaElement = new MediaElement
                                        {
                                            LoadedBehavior = MediaState.Manual,
                                            UnloadedBehavior = MediaState.Manual,
                                            ScrubbingEnabled = true,
                                            Volume = 0 // Mute
                                        };

                                        // Mount into hidden host so it participates in the visual tree
                                        try
                                        {
                                            hiddenMediaHost.Children.Clear();
                                            hiddenMediaHost.Children.Add(mediaElement);
                                            hiddenMediaHost.UpdateLayout();
                                        }
                                        catch { }

                                        mediaElement.MediaOpened += async (s, e) =>
                                        {
                                            try
                                            {
                                                // Seek to 1 second (or start) to get a good frame
                                                mediaElement.Position = TimeSpan.FromSeconds(1);
                                                mediaElement.Pause();

                                                // Give layout/rendering a moment
                                                await Task.Delay(400);
                                                hiddenMediaHost.UpdateLayout();

                                                int width = (int)(mediaElement.NaturalVideoWidth > 0 ? mediaElement.NaturalVideoWidth : 640);
                                                int height = (int)(mediaElement.NaturalVideoHeight > 0 ? mediaElement.NaturalVideoHeight : 360);

                                                // Render the host containing the MediaElement
                                                var renderTargetBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                                                    width,
                                                    height,
                                                    96,
                                                    96,
                                                    System.Windows.Media.PixelFormats.Pbgra32);

                                                renderTargetBitmap.Render(hiddenMediaHost);

                                                // Save as JPEG
                                                var jpegEncoder = new System.Windows.Media.Imaging.JpegBitmapEncoder
                                                {
                                                    QualityLevel = 85
                                                };
                                                jpegEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(renderTargetBitmap));

                                                using (var fileStream = new FileStream(thumbPath, FileMode.Create))
                                                {
                                                    jpegEncoder.Save(fileStream);
                                                }

                                                frameCaptured = true;
                                            }
                                            catch { }
                                            finally
                                            {
                                                mediaElement.Close();
                                                frameReady.Set();
                                            }
                                        };

                                        mediaElement.MediaFailed += (s, e) =>
                                        {
                                            try { mediaElement?.Close(); } catch { }
                                            frameReady.Set();
                                        };

                                        // Open video
                                        mediaElement.Source = new Uri(tempVideoPath, UriKind.Absolute);
                                        mediaElement.Play();
                                    });

                                    // Wait up to 10 seconds for frame extraction
                                    if (frameReady.WaitOne(10000) && frameCaptured && File.Exists(thumbPath))
                                    {
                                        // Update thumbnail path on UI thread
                                        Dispatcher.Invoke(() =>
                                        {
                                            file.ThumbnailPath = thumbPath;
                                        });
                                    }

                                    // Cleanup MediaElement
                                    Dispatcher.Invoke(() =>
                                    {
                                        try
                                        {
                                            if (mediaElement != null)
                                            {
                                                mediaElement.Close();
                                                mediaElement = null;
                                            }
                                        }
                                        catch { }
                                    });
                                }
                                catch { }
                        });

                        // Cleanup temp video file
                        try
                        {
                            await Task.Delay(300);
                            File.Delete(tempVideoPath);
                        }
                        catch { }
                    }
                    catch { }
                }
                if (File.Exists(thumbPath) && new FileInfo(thumbPath).Length > 0)
                {
                    try { File.Copy(thumbPath, cachePath, true); } catch { }
                    Dispatcher.Invoke(() => { file.ThumbnailPath = cachePath; });
                    return true;
                }
                return false;
            }
            catch
            {
                // Silently fail - thumbnails are optional
                return false;
            }
        }

        private string GetFfmpegPath()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var local = Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe");
                if (File.Exists(local)) return local;
                // Also try alongside executable
                var inRoot = Path.Combine(baseDir, "ffmpeg.exe");
                if (File.Exists(inRoot)) return inRoot;
            }
            catch { }
            return string.Empty;
        }

        private string GetFfprobePath()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var local = Path.Combine(baseDir, "ffmpeg", "ffprobe.exe");
                if (File.Exists(local)) return local;
                var inRoot = Path.Combine(baseDir, "ffprobe.exe");
                if (File.Exists(inRoot)) return inRoot;
            }
            catch { }
            return string.Empty;
        }

        private async Task<double> GetVideoDurationSecondsAsync(string ffprobePath, string videoPath)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p != null)
                {
                    var output = await p.StandardOutput.ReadToEndAsync();
                    await p.WaitForExitAsync();
                    if (double.TryParse(output.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                    {
                        return seconds;
                    }
                }
            }
            catch { }
            return 0;
        }

        private static string GetThumbnailCachePath(string deviceId, RecoverableFile file)
        {
            var cacheDir = Path.Combine(Path.GetTempPath(), "AndroidRecoveryThumbCache");
            Directory.CreateDirectory(cacheDir);
            var key = $"{deviceId}|{file.DevicePath}|{file.SizeInBytes}";
            using var sha1 = SHA1.Create();
            var hash = Convert.ToHexString(sha1.ComputeHash(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
            return Path.Combine(cacheDir, $"{hash}.jpg");
        }

        private async Task<bool> ExtractAudioThumbnailAsync(RecoverableFile file, string cachePath)
        {
            try
            {
                if (_selectedDeviceId == null) return false;

                // Skip very large audio files (> 200MB)
                if (file.SizeInBytes > 200 * 1024 * 1024) return false;

                var ffmpegPath = GetFfmpegPath();
                if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath)) return false;

                // Prepare temp paths
                var tempDir = Path.Combine(Path.GetTempPath(), "AndroidRecoveryAudioThumbs");
                Directory.CreateDirectory(tempDir);
                var ext = string.IsNullOrEmpty(Path.GetExtension(file.DevicePath)) ? "mp3" : Path.GetExtension(file.DevicePath).TrimStart('.').ToLower();
                var tempAudio = Path.Combine(tempDir, $"temp_{Guid.NewGuid()}.{ext}");
                var thumbPath = Path.Combine(tempDir, $"thumb_{Guid.NewGuid()}.jpg");

                // Pull small portion (2MB) from device for speed
                var cmd = $"\"{_adbService.GetAdbPath()}\" -s {_selectedDeviceId} exec-out \"dd if='{file.DevicePath}' bs=1M count=2\" > \"{tempAudio}\"";
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + cmd,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    if (p != null) await p.WaitForExitAsync();
                }

                if (!File.Exists(tempAudio) || new FileInfo(tempAudio).Length < 8 * 1024)
                {
                    // Fallback: full pull if <= 20MB
                    if (file.SizeInBytes > 20 * 1024 * 1024) return false;
                    var pull = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _adbService.GetAdbPath(),
                        Arguments = $"-s {_selectedDeviceId} pull \"{file.DevicePath}\" \"{tempAudio}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(_adbService.GetAdbPath()) ?? ""
                    };
                    using var pullProc = System.Diagnostics.Process.Start(pull);
                    if (pullProc != null) await pullProc.WaitForExitAsync();
                }

                if (!File.Exists(tempAudio)) return false;

                // Try to extract embedded cover art
                var coverArgs = $"-y -i \"{tempAudio}\" -an -vcodec mjpeg -map 0:v:0 -frames:v 1 \"{thumbPath}\"";
                var ff1 = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = coverArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var proc1 = System.Diagnostics.Process.Start(ff1))
                {
                    if (proc1 != null) await proc1.WaitForExitAsync();
                }

                if (!File.Exists(thumbPath) || new FileInfo(thumbPath).Length == 0)
                {
                    // Generate waveform image if no cover
                    var waveArgs = $"-y -i \"{tempAudio}\" -filter_complex showwavespic=s=320x120:split_channels=0:colors=DodgerBlue -frames:v 1 \"{thumbPath}\"";
                    var ff2 = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = waveArgs,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    using (var proc2 = System.Diagnostics.Process.Start(ff2))
                    {
                        if (proc2 != null) await proc2.WaitForExitAsync();
                    }
                }

                // Save to cache
                if (File.Exists(thumbPath) && new FileInfo(thumbPath).Length > 0)
                {
                    try { File.Copy(thumbPath, cachePath, true); } catch { }
                    Dispatcher.Invoke(() => { file.ThumbnailPath = cachePath; });
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> ExtractDocumentThumbnailAsync(RecoverableFile file, string cachePath)
        {
            try
            {
                if (_selectedDeviceId == null) return false;

                // Pull the document locally (cap to 20MB)
                if (file.SizeInBytes > 20 * 1024 * 1024) return false;

                var tempDir = Path.Combine(Path.GetTempPath(), "AndroidRecoveryDocThumbs");
                Directory.CreateDirectory(tempDir);
                var ext = Path.GetExtension(file.DevicePath).ToLowerInvariant();
                var tempDoc = Path.Combine(tempDir, $"temp_{Guid.NewGuid()}{ext}");
                var tempPng = Path.Combine(tempDir, $"thumb_{Guid.NewGuid()}.png");

                var pull = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _adbService.GetAdbPath(),
                    Arguments = $"-s {_selectedDeviceId} pull \"{file.DevicePath}\" \"{tempDoc}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(_adbService.GetAdbPath()) ?? ""
                };
                using var pullProc = System.Diagnostics.Process.Start(pull);
                if (pullProc != null) await pullProc.WaitForExitAsync();
                if (!File.Exists(tempDoc)) return false;

                bool ok = false;
                // Try MuPDF mutool for PDFs
                if (ext == ".pdf")
                {
                    var mutool = GetMutoolPath();
                    if (!string.IsNullOrEmpty(mutool) && File.Exists(mutool))
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = mutool,
                            Arguments = $"draw -o \"{tempPng}\" -r 96 -w 320 -h 320 \"{tempDoc}\" 1",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };
                        using var p = System.Diagnostics.Process.Start(psi);
                        if (p != null) await p.WaitForExitAsync();
                        ok = File.Exists(tempPng) && new FileInfo(tempPng).Length > 0;
                    }
                }

                // Try LibreOffice headless for Office docs
                if (!ok && (ext == ".doc" || ext == ".docx" || ext == ".ppt" || ext == ".pptx" || ext == ".xls" || ext == ".xlsx"))
                {
                    var soffice = GetSofficePath();
                    if (!string.IsNullOrEmpty(soffice) && File.Exists(soffice))
                    {
                        var outDir = tempDir;
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = soffice,
                            Arguments = $"--headless --convert-to png --outdir \"{outDir}\" \"{tempDoc}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };
                        using var p = System.Diagnostics.Process.Start(psi);
                        if (p != null) await p.WaitForExitAsync();
                        // Pick first generated PNG
                        var firstPng = Directory.GetFiles(outDir, "*.png").OrderBy(f => f).FirstOrDefault();
                        if (!string.IsNullOrEmpty(firstPng))
                        {
                            tempPng = firstPng;
                            ok = true;
                        }
                    }
                }

                if (ok && File.Exists(tempPng))
                {
                    // Copy to cache as jpg (simple copy, allow png extension if needed)
                    try { File.Copy(tempPng, cachePath, true); } catch { }
                    Dispatcher.Invoke(() => { file.ThumbnailPath = cachePath; });
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private string GetMutoolPath()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var local = Path.Combine(baseDir, "tools", "mutool.exe");
                if (File.Exists(local)) return local;
                var inRoot = Path.Combine(baseDir, "mutool.exe");
                if (File.Exists(inRoot)) return inRoot;
            }
            catch { }
            return string.Empty;
        }

        private string GetSofficePath()
        {
            try
            {
                // Common Windows install paths
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program", "soffice.exe")
                };
                foreach (var p in candidates)
                {
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return string.Empty;
        }

        private void OnProgressUpdate(object? sender, string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtStatus.Text = message;
            });
        }

        private void UpdateFileCount()
        {
            var count = _recoverableFiles.Count;
            txtFileCount.Text = $"({count} files)";
            btnRecoverAll.IsEnabled = count > 0;
        }

        private void DgFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Legacy handler - thumbnails use click events instead
            UpdateRecoverButtonStates();
        }

        private void Thumbnail_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is RecoverableFile file)
            {
                _ = UpdatePreviewAsync(file);
            }
        }

        private void Thumbnail_Checked(object sender, RoutedEventArgs e)
        {
            UpdateRecoverButtonStates();
        }

        private void Thumbnail_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateRecoverButtonStates();
        }

        private void Thumbnail_Checkbox_Click(object sender, RoutedEventArgs e)
        {
            // Prevent the click from bubbling to the border
            e.Handled = true;
            UpdateRecoverButtonStates();
        }

        private void UpdateRecoverButtonStates()
        {
            var selectedCount = _recoverableFiles.Count(f => f.IsSelected);
            btnRecoverSelected.IsEnabled = selectedCount > 0;
        }
        
        private async Task UpdatePreviewAsync(RecoverableFile file)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    txtNoPreview.Visibility = Visibility.Collapsed;
                    txtPreviewLoadingStatus.Text = "Loading preview...";
                    
                    // Update file info first
                    txtPreviewFileName.Text = file.FileName;
                    txtPreviewType.Text = file.FileType;
                    txtPreviewSize.Text = file.FileSize;
                    txtPreviewPath.Text = file.Path;
                    txtPreviewStatus.Text = file.RecoveryStatus;
                    txtPreviewDevicePath.Text = file.DevicePath;
                    
                    // Hide all preview types first
                    imgPreview.Visibility = Visibility.Collapsed;
                    pnlTextPreview.Visibility = Visibility.Collapsed;
                    pnlVideoPreview.Visibility = Visibility.Collapsed;
                    pnlFileInfo.Visibility = Visibility.Visible;
                });

                // Show appropriate preview based on file type
                var fileType = file.FileType.ToUpper();
                
                if (fileType == "JPG" || fileType == "JPEG" || fileType == "PNG" || fileType == "GIF" || fileType == "WEBP")
                {
                    await LoadImagePreviewAsync(file);
                }
                else if (fileType == "TXT" || fileType == "PDF" || fileType == "DOC" || fileType == "DOCX")
                {
                    await LoadTextPreviewAsync(file);
                }
                else if (fileType == "MP4" || fileType == "AVI" || fileType == "MKV" || fileType == "MOV" || 
                         fileType == "M4V" || fileType == "FLV" || fileType == "WMV" || fileType == "3GP")
                {
                    await LoadVideoPreviewAsync(file);
                }
                else if (fileType == "MP3" || fileType == "WAV" || fileType == "M4A" || fileType == "AAC")
                {
                    ShowAudioInfo(file);
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        txtNoPreview.Text = "File preview not available for this file type";
                        txtNoPreview.Visibility = Visibility.Visible;
                        txtPreviewLoadingStatus.Text = "Preview not available";
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    txtNoPreview.Text = $"Preview error: {ex.Message}";
                    txtNoPreview.Visibility = Visibility.Visible;
                    txtPreviewLoadingStatus.Text = "Error";
                });
            }
        }

        private async Task LoadImagePreviewAsync(RecoverableFile file)
        {
            try
            {
                if (_selectedDeviceId == null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        txtNoPreview.Text = "No device selected";
                        txtNoPreview.Visibility = Visibility.Visible;
                    });
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    txtPreviewLoadingStatus.Text = "Downloading image...";
                });

                // Try to pull the file temporarily to show preview
                var tempDir = Path.Combine(Path.GetTempPath(), "AndroidRecoveryPreview");
                Directory.CreateDirectory(tempDir);
                var extension = file.FileType.ToLower();
                if (extension == "jpeg") extension = "jpg";
                var tempFilePath = Path.Combine(tempDir, $"preview_{Guid.NewGuid()}.{extension}");

                try
                {
                    // First verify file exists on device
                    var testCommand = $"\"{_adbService.GetAdbPath()}\" -s {_selectedDeviceId} shell test -f \"{file.DevicePath}\" && echo EXISTS";
                    
                    var testProcessInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {testCommand}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    bool fileExists = false;
                    using (var testProcess = System.Diagnostics.Process.Start(testProcessInfo))
                    {
                        if (testProcess != null)
                        {
                            var testOutput = await testProcess.StandardOutput.ReadToEndAsync();
                            await testProcess.WaitForExitAsync();
                            fileExists = testOutput.Contains("EXISTS");
                        }
                    }

                    if (!fileExists)
                    {
                        // File doesn't exist - attempt to recover it temporarily for preview
                        Dispatcher.Invoke(() =>
                        {
                            txtPreviewLoadingStatus.Text = "Recovering file for preview...";
                            txtNoPreview.Text = "Recovering deleted file temporarily for preview...\n\nThis may take a moment.";
                            txtNoPreview.Visibility = Visibility.Visible;
                        });
                        
                        // Try to recover file temporarily to preview location
                        var tempRecoveryDir = Path.Combine(Path.GetTempPath(), "AndroidRecoveryPreview");
                        Directory.CreateDirectory(tempRecoveryDir);
                        
                        // Create a copy of the file object to avoid modifying the original
                        var fileCopy = new RecoverableFile
                        {
                            DevicePath = file.DevicePath,
                            FileName = file.FileName,
                            FileType = file.FileType,
                            SizeInBytes = file.SizeInBytes,
                            RecoveryStatus = file.RecoveryStatus
                        };
                        
                        var recoverySuccess = await _recoveryService.RecoverFileAsync(
                            _selectedDeviceId!,
                            fileCopy,
                            tempRecoveryDir
                        );
                        
                        // Check if file was recovered - it will have updated DevicePath to local path
                        if (recoverySuccess && !string.IsNullOrEmpty(fileCopy.DevicePath) && File.Exists(fileCopy.DevicePath))
                        {
                            // Update tempFilePath to use recovered file
                            tempFilePath = fileCopy.DevicePath;
                            fileExists = true;
                            
                            Dispatcher.Invoke(() =>
                            {
                                txtPreviewLoadingStatus.Text = "File recovered - loading preview...";
                            });
                        }
                        else
                        {
                            // Recovery failed - show error
                            Dispatcher.Invoke(() =>
                            {
                                txtNoPreview.Text = $"⚠️ Cannot preview this deleted file.\n\nThe file cannot be temporarily recovered for preview.\n\nPossible reasons:\n• File is permanently overwritten\n• Requires root access for deep recovery\n• File is corrupted\n\nTry recovering it permanently to see if it's still intact.";
                                txtNoPreview.Visibility = Visibility.Visible;
                                txtPreviewLoadingStatus.Text = "Preview unavailable";
                                pnlFileInfo.Visibility = Visibility.Visible;
                            });
                            return;
                        }
                    }

                    // Use ADB pull only if file wasn't already recovered for preview
                    if (!fileExists || !File.Exists(tempFilePath))
                    {
                        var processInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = _adbService.GetAdbPath(),
                            Arguments = $"-s {_selectedDeviceId} pull \"{file.DevicePath}\" \"{tempFilePath}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            WorkingDirectory = Path.GetDirectoryName(_adbService.GetAdbPath())
                        };

                        using var process = System.Diagnostics.Process.Start(processInfo);
                        if (process != null)
                        {
                            var output = await process.StandardOutput.ReadToEndAsync();
                            var error = await process.StandardError.ReadToEndAsync();
                            await process.WaitForExitAsync();
                            
                            // Wait a bit for file to be written
                            await Task.Delay(500);
                        }
                    }
                    
                    // Load preview if file exists (either from recovery or pull)
                    if (File.Exists(tempFilePath) && new FileInfo(tempFilePath).Length > 0)
                    {
                            Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                                    bitmap.BeginInit();
                                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                    bitmap.UriSource = new Uri(tempFilePath, UriKind.Absolute);
                                    bitmap.EndInit();
                                    bitmap.Freeze();
                                    
                                    imgPreview.Source = bitmap;
                                    imgPreview.Visibility = Visibility.Visible;
                                    txtNoPreview.Visibility = Visibility.Collapsed;
                                    txtPreviewLoadingStatus.Text = "Preview ready";
                                    pnlFileInfo.Visibility = Visibility.Visible;
                                }
                                catch (Exception ex)
                                {
                                    txtNoPreview.Text = $"Could not load image: {ex.Message}";
                                    txtNoPreview.Visibility = Visibility.Visible;
                                    txtPreviewLoadingStatus.Text = "Error loading preview";
                                    imgPreview.Visibility = Visibility.Collapsed;
                                }
                            });
                        }
                        else
                        {
                            // File doesn't exist or is inaccessible - this is expected for deleted files
                            Dispatcher.Invoke(() =>
                            {
                                var statusMsg = file.RecoveryStatus.Contains("Deleted") || file.RecoveryStatus.Contains("Permanently") 
                                    ? "This is a DELETED file. Preview is not available until the file is recovered.\n\nClick 'Recover Selected' to restore the file, then you can open it from your computer."
                                    : $"File not accessible.\nPath: {file.DevicePath}\n\nThis file may need to be recovered first.";
                                
                                txtNoPreview.Text = $"⚠️ Cannot Preview Deleted File\n\n{statusMsg}";
                                txtNoPreview.Visibility = Visibility.Visible;
                                txtPreviewLoadingStatus.Text = "Deleted - Recover First";
                                pnlFileInfo.Visibility = Visibility.Visible;
                            });
                        }
                }
                finally
                {
                    // Cleanup temp file after a delay (user might still be viewing)
                    _ = Task.Delay(60000).ContinueWith(_ =>
                    {
                        try
                        {
                            if (File.Exists(tempFilePath))
                                File.Delete(tempFilePath);
                        }
                        catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    txtNoPreview.Text = $"Preview error: {ex.Message}";
                    txtNoPreview.Visibility = Visibility.Visible;
                    imgPreview.Visibility = Visibility.Collapsed;
                });
            }
        }

        private async Task LoadTextPreviewAsync(RecoverableFile file)
        {
            try
            {
                if (_selectedDeviceId == null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        txtNoPreview.Text = "No device selected";
                        txtNoPreview.Visibility = Visibility.Visible;
                    });
                    return;
                }

                if (file.SizeInBytes > 1024 * 1024) // Don't preview files larger than 1MB
                {
                    Dispatcher.Invoke(() =>
                    {
                        txtNoPreview.Text = "File too large for preview (max 1MB)";
                        txtNoPreview.Visibility = Visibility.Visible;
                    });
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    txtPreviewLoadingStatus.Text = "Downloading file...";
                });

                // Pull file temporarily for preview
                var tempDir = Path.Combine(Path.GetTempPath(), "AndroidRecoveryPreview");
                Directory.CreateDirectory(tempDir);
                var tempFilePath = Path.Combine(tempDir, $"preview_{Guid.NewGuid()}.txt");

                try
                {
                    // First verify file exists
                    var testCommand = $"\"{_adbService.GetAdbPath()}\" -s {_selectedDeviceId} shell test -f \"{file.DevicePath}\" && echo EXISTS";
                    
                    var testProcessInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {testCommand}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    bool fileExists = false;
                    using (var testProcess = System.Diagnostics.Process.Start(testProcessInfo))
                    {
                        if (testProcess != null)
                        {
                            var testOutput = await testProcess.StandardOutput.ReadToEndAsync();
                            await testProcess.WaitForExitAsync();
                            fileExists = testOutput.Contains("EXISTS");
                        }
                    }

                    if (!fileExists)
                    {
                        // File doesn't exist - attempt to recover it temporarily for preview
                        Dispatcher.Invoke(() =>
                        {
                            txtPreviewLoadingStatus.Text = "Recovering file for preview...";
                            txtNoPreview.Text = "Recovering deleted file temporarily for preview...\n\nThis may take a moment.";
                            txtNoPreview.Visibility = Visibility.Visible;
                        });
                        
                        // Try to recover file temporarily to preview location
                        var tempRecoveryDir = Path.Combine(Path.GetTempPath(), "AndroidRecoveryPreview");
                        Directory.CreateDirectory(tempRecoveryDir);
                        
                        // Create a copy of the file object to avoid modifying the original
                        var fileCopy = new RecoverableFile
                        {
                            DevicePath = file.DevicePath,
                            FileName = file.FileName,
                            FileType = file.FileType,
                            SizeInBytes = file.SizeInBytes,
                            RecoveryStatus = file.RecoveryStatus
                        };
                        
                        var recoverySuccess = await _recoveryService.RecoverFileAsync(
                            _selectedDeviceId!,
                            fileCopy,
                            tempRecoveryDir
                        );
                        
                        // Check if file was recovered - it will have updated DevicePath to local path
                        if (recoverySuccess && !string.IsNullOrEmpty(fileCopy.DevicePath) && File.Exists(fileCopy.DevicePath))
                        {
                            // Update tempFilePath to use recovered file
                            tempFilePath = fileCopy.DevicePath;
                            fileExists = true;
                            
                            Dispatcher.Invoke(() =>
                            {
                                txtPreviewLoadingStatus.Text = "File recovered - loading preview...";
                            });
                        }
                        else
                        {
                            // Recovery failed - show error
                            Dispatcher.Invoke(() =>
                            {
                                txtNoPreview.Text = $"⚠️ Cannot preview this deleted file.\n\nThe file cannot be temporarily recovered for preview.\n\nPossible reasons:\n• File is permanently overwritten\n• Requires root access for deep recovery\n• File is corrupted\n\nTry recovering it permanently to see if it's still intact.";
                                txtNoPreview.Visibility = Visibility.Visible;
                                txtPreviewLoadingStatus.Text = "Preview unavailable";
                            });
                            return;
                        }
                    }

                    // Use ADB pull only if file wasn't already recovered for preview
                    if (!fileExists || !File.Exists(tempFilePath))
                    {
                        var processInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = _adbService.GetAdbPath(),
                            Arguments = $"-s {_selectedDeviceId} pull \"{file.DevicePath}\" \"{tempFilePath}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            WorkingDirectory = Path.GetDirectoryName(_adbService.GetAdbPath())
                        };

                        using var process = System.Diagnostics.Process.Start(processInfo);
                        if (process != null)
                        {
                            var output = await process.StandardOutput.ReadToEndAsync();
                            var error = await process.StandardError.ReadToEndAsync();
                            await process.WaitForExitAsync();
                            
                            // Wait for file to be written
                            await Task.Delay(1000);
                        }
                    }
                    
                    // Load preview if file exists (either from recovery or pull)
                    if (File.Exists(tempFilePath) && new FileInfo(tempFilePath).Length > 0)
                    {
                            var text = await File.ReadAllTextAsync(tempFilePath);
                            // Limit preview to first 2000 characters
                            var previewText = text.Length > 2000 ? text.Substring(0, 2000) + "\n\n... (truncated - file is longer)" : text;
                            
                            Dispatcher.Invoke(() =>
                            {
                                txtPreview.Text = previewText;
                                pnlTextPreview.Visibility = Visibility.Visible;
                                txtNoPreview.Visibility = Visibility.Collapsed;
                                txtPreviewLoadingStatus.Text = "Preview ready";
                                pnlFileInfo.Visibility = Visibility.Visible;
                            });
                        }
                        else
                        {
                            // Try alternative method using shell cat
                            try
                            {
                                var catOutput = await _adbService.ExecuteShellCommandAsync(_selectedDeviceId, $"cat \"{file.DevicePath}\"");
                                if (!string.IsNullOrWhiteSpace(catOutput) && catOutput.Length > 0)
                                {
                                    await File.WriteAllTextAsync(tempFilePath, catOutput);
                                    var previewText = catOutput.Length > 2000 ? catOutput.Substring(0, 2000) + "\n\n... (truncated)" : catOutput;
                                    
                                    Dispatcher.Invoke(() =>
                                    {
                                        txtPreview.Text = previewText;
                                        pnlTextPreview.Visibility = Visibility.Visible;
                                        txtNoPreview.Visibility = Visibility.Collapsed;
                                        txtPreviewLoadingStatus.Text = "Preview ready";
                                        pnlFileInfo.Visibility = Visibility.Visible;
                                    });
                                    return;
                                }
                            }
                            catch { }

                            Dispatcher.Invoke(() =>
                            {
                                var statusMsg = file.RecoveryStatus.Contains("Deleted") || file.RecoveryStatus.Contains("Permanently")
                                    ? "⚠️ This is a DELETED file.\n\nPreview is not available for deleted files.\n\nTo view this file:\n1. Click 'Recover Selected' to restore it\n2. After recovery, open it from your computer"
                                    : $"File not accessible.\nPath: {file.DevicePath}\n\nThis file may need to be recovered first.";
                                
                                txtNoPreview.Text = statusMsg;
                                txtNoPreview.Visibility = Visibility.Visible;
                                txtPreviewLoadingStatus.Text = "Deleted - Recover First";
                                pnlFileInfo.Visibility = Visibility.Visible;
                            });
                        }
                }
                finally
                {
                    _ = Task.Delay(60000).ContinueWith(_ =>
                    {
                        try
                        {
                            if (File.Exists(tempFilePath))
                                File.Delete(tempFilePath);
                        }
                        catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    txtNoPreview.Text = $"Preview error: {ex.Message}";
                    txtNoPreview.Visibility = Visibility.Visible;
                    pnlTextPreview.Visibility = Visibility.Collapsed;
                });
            }
        }

        private async Task LoadVideoPreviewAsync(RecoverableFile file)
        {
            try
            {
                if (_selectedDeviceId == null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        txtNoPreview.Text = "No device selected";
                        txtNoPreview.Visibility = Visibility.Visible;
                    });
                    return;
                }

                // Don't preview very large videos (> 100MB) to save time
                if (file.SizeInBytes > 100 * 1024 * 1024)
                {
                    Dispatcher.Invoke(() =>
                    {
                        txtNoPreview.Text = "Video too large for preview (max 100MB)\n\nFile size: " + file.FileSize + "\n\nPlease recover the file to view it.";
                        txtNoPreview.Visibility = Visibility.Visible;
                        txtPreviewLoadingStatus.Text = "Too large";
                        pnlFileInfo.Visibility = Visibility.Visible;
                    });
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    txtPreviewLoadingStatus.Text = "Downloading video for preview...";
                });

                // Pull file temporarily for preview
                var tempDir = Path.Combine(Path.GetTempPath(), "AndroidRecoveryPreview");
                Directory.CreateDirectory(tempDir);
                var extension = Path.GetExtension(file.DevicePath).TrimStart('.').ToLower();
                if (string.IsNullOrEmpty(extension))
                    extension = file.FileType.ToLower();
                var tempFilePath = Path.Combine(tempDir, $"preview_{Guid.NewGuid()}.{extension}");

                try
                {
                    // First verify file exists on device using direct ADB shell command
                    var testResult = await _adbService.ExecuteShellCommandAsync(_selectedDeviceId, $"test -f \"{file.DevicePath}\" && echo EXISTS");
                    bool fileExists = testResult.Contains("EXISTS");

                    if (!fileExists)
                    {
                        // File doesn't exist - attempt to recover it temporarily for preview
                        Dispatcher.Invoke(() =>
                        {
                            txtPreviewLoadingStatus.Text = "Recovering video for preview...";
                            txtNoPreview.Text = "Recovering deleted video temporarily for preview...\n\nThis may take a moment for large files.";
                            txtNoPreview.Visibility = Visibility.Visible;
                        });
                        
                        // Try to recover file temporarily to preview location
                        var tempRecoveryDir = Path.Combine(Path.GetTempPath(), "AndroidRecoveryPreview");
                        Directory.CreateDirectory(tempRecoveryDir);
                        
                        // Create a copy of the file object to avoid modifying the original
                        var fileCopy = new RecoverableFile
                        {
                            DevicePath = file.DevicePath,
                            FileName = file.FileName,
                            FileType = file.FileType,
                            SizeInBytes = file.SizeInBytes,
                            RecoveryStatus = file.RecoveryStatus
                        };
                        
                        var recoverySuccess = await _recoveryService.RecoverFileAsync(
                            _selectedDeviceId!,
                            fileCopy,
                            tempRecoveryDir
                        );
                        
                        // Check if file was recovered - it will have updated DevicePath to local path
                        if (recoverySuccess && !string.IsNullOrEmpty(fileCopy.DevicePath) && File.Exists(fileCopy.DevicePath))
                        {
                            // Update tempFilePath to use recovered file
                            tempFilePath = fileCopy.DevicePath;
                            fileExists = true;
                            
                            Dispatcher.Invoke(() =>
                            {
                                txtPreviewLoadingStatus.Text = "Video recovered - loading preview...";
                            });
                        }
                        else
                        {
                            // Recovery failed - show error
                            Dispatcher.Invoke(() =>
                            {
                                txtNoPreview.Text = $"⚠️ Cannot preview this deleted video.\n\nThe video cannot be temporarily recovered for preview.\n\nPossible reasons:\n• File is permanently overwritten\n• Requires root access for deep recovery\n• Video is corrupted\n• File is too large\n\nTry recovering it permanently to see if it's still intact.";
                                txtNoPreview.Visibility = Visibility.Visible;
                                txtPreviewLoadingStatus.Text = "Preview unavailable";
                                pnlFileInfo.Visibility = Visibility.Visible;
                            });
                            return;
                        }
                    }

                    // Use ADB pull only if file wasn't already recovered for preview
                    if (!fileExists || !File.Exists(tempFilePath))
                    {
                        var processInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = _adbService.GetAdbPath(),
                            Arguments = $"-s {_selectedDeviceId} pull \"{file.DevicePath}\" \"{tempFilePath}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            WorkingDirectory = Path.GetDirectoryName(_adbService.GetAdbPath())
                        };

                        using var process = System.Diagnostics.Process.Start(processInfo);
                        if (process != null)
                        {
                            var output = await process.StandardOutput.ReadToEndAsync();
                            var error = await process.StandardError.ReadToEndAsync();
                            await process.WaitForExitAsync();
                            
                            // Wait for file to be written
                            await Task.Delay(1000);
                        }
                    }
                    
                    // Load video preview if file exists (either from recovery or pull)
                    if (File.Exists(tempFilePath) && new FileInfo(tempFilePath).Length > 0)
                    {
                        // Detect video rotation from metadata
                        double rotationAngle = 0;
                        var ffprobePath = GetFfprobePath();
                        if (!string.IsNullOrEmpty(ffprobePath) && File.Exists(ffprobePath))
                        {
                            rotationAngle = await GetVideoRotationAsync(ffprobePath, tempFilePath);
                        }

                        var finalRotation = rotationAngle; // Capture for closure
                        Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                // Apply rotation transform
                                var rotateTransform = mediaVideoPreview.RenderTransform as RotateTransform;
                                if (rotateTransform != null)
                                {
                                    rotateTransform.Angle = finalRotation;
                                }
                                
                                // Set video source
                                mediaVideoPreview.Source = new Uri(tempFilePath, UriKind.Absolute);
                                pnlVideoPreview.Visibility = Visibility.Visible;
                                imgPreview.Visibility = Visibility.Collapsed;
                                pnlTextPreview.Visibility = Visibility.Collapsed;
                                txtNoPreview.Visibility = Visibility.Collapsed;
                                txtPreviewLoadingStatus.Text = "Video loaded - Click Play to preview";
                                pnlFileInfo.Visibility = Visibility.Visible;
                                
                                // Enable video controls
                                btnPlayVideo.IsEnabled = true;
                                btnPauseVideo.IsEnabled = false;
                                btnStopVideo.IsEnabled = false;
                            }
                            catch (Exception ex)
                            {
                                txtNoPreview.Text = $"Could not load video: {ex.Message}\n\nFile may be corrupted or format not supported.";
                                txtNoPreview.Visibility = Visibility.Visible;
                                txtPreviewLoadingStatus.Text = "Error loading video";
                                pnlVideoPreview.Visibility = Visibility.Collapsed;
                            }
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            txtNoPreview.Text = $"Video file not accessible.\n\nPath: {file.DevicePath}\n\nThis video may need to be recovered first.";
                            txtNoPreview.Visibility = Visibility.Visible;
                            txtPreviewLoadingStatus.Text = "File not found";
                            pnlFileInfo.Visibility = Visibility.Visible;
                        });
                    }
                }
                finally
                {
                    // Cleanup temp file after a delay (user might still be viewing)
                    _ = Task.Delay(120000).ContinueWith(_ =>
                    {
                        try
                        {
                            if (File.Exists(tempFilePath))
                                File.Delete(tempFilePath);
                        }
                        catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    txtNoPreview.Text = $"Video preview error: {ex.Message}";
                    txtNoPreview.Visibility = Visibility.Visible;
                    pnlVideoPreview.Visibility = Visibility.Collapsed;
                    txtPreviewLoadingStatus.Text = "Error";
                });
            }
        }

        private void ShowVideoInfo(RecoverableFile file)
        {
                Dispatcher.Invoke(() =>
                {
                    txtPreview.Text = $"Video File Information\n\nFile: {file.FileName}\nType: {file.FileType}\nSize: {file.FileSize}\nStatus: {file.RecoveryStatus}\n\nTo view the video, recover the file first.";
                    pnlTextPreview.Visibility = Visibility.Visible;
                    txtNoPreview.Visibility = Visibility.Collapsed;
                    txtPreviewLoadingStatus.Text = "Info only";
                    pnlFileInfo.Visibility = Visibility.Visible;
                });
        }

        private void ShowAudioInfo(RecoverableFile file)
        {
                Dispatcher.Invoke(() =>
                {
                    txtPreview.Text = $"Audio File Information\n\nFile: {file.FileName}\nType: {file.FileType}\nSize: {file.FileSize}\nStatus: {file.RecoveryStatus}\n\nTo play the audio, recover the file first.";
                    pnlTextPreview.Visibility = Visibility.Visible;
                    txtNoPreview.Visibility = Visibility.Collapsed;
                    txtPreviewLoadingStatus.Text = "Info only";
                    pnlFileInfo.Visibility = Visibility.Visible;
                });
        }

        private void BtnPlayVideo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                mediaVideoPreview.Play();
                btnPlayVideo.IsEnabled = false;
                btnPauseVideo.IsEnabled = true;
                btnStopVideo.IsEnabled = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error playing video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnPauseVideo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                mediaVideoPreview.Pause();
                btnPlayVideo.IsEnabled = true;
                btnPauseVideo.IsEnabled = false;
            }
            catch { }
        }

        private void BtnStopVideo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                mediaVideoPreview.Stop();
                btnPlayVideo.IsEnabled = true;
                btnPauseVideo.IsEnabled = false;
                btnStopVideo.IsEnabled = false;
            }
            catch { }
        }

        private void MediaVideoPreview_MediaEnded(object sender, RoutedEventArgs e)
        {
            btnPlayVideo.IsEnabled = true;
            btnPauseVideo.IsEnabled = false;
            btnStopVideo.IsEnabled = false;
        }

        private async void MediaVideoPreview_MediaOpened(object sender, RoutedEventArgs e)
        {
            // Re-check rotation when video opens (in case it wasn't set earlier)
            try
            {
                if (mediaVideoPreview.Source != null && mediaVideoPreview.Source.IsFile)
                {
                    var videoPath = mediaVideoPreview.Source.LocalPath;
                    var ffprobePath = GetFfprobePath();
                    if (!string.IsNullOrEmpty(ffprobePath) && File.Exists(ffprobePath))
                    {
                        var rotation = await GetVideoRotationAsync(ffprobePath, videoPath);
                        Dispatcher.Invoke(() =>
                        {
                            var rotateTransform = mediaVideoPreview.RenderTransform as RotateTransform;
                            if (rotateTransform != null)
                            {
                                rotateTransform.Angle = rotation;
                            }
                        });
                    }
                }
            }
            catch { }
        }

        private async Task<double> GetVideoRotationAsync(string ffprobePath, string videoPath)
        {
            try
            {
                // Try to get rotation from stream tags (most common for phone videos)
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -show_entries stream_tags=rotate -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var p = System.Diagnostics.Process.Start(psi);
                if (p != null)
                {
                    var output = await p.StandardOutput.ReadToEndAsync();
                    await p.WaitForExitAsync();
                    
                    if (double.TryParse(output.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rot))
                    {
                        return rot;
                    }
                }

                // Alternative: check format tags
                var psi2 = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -show_entries format_tags=rotate -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p2 = System.Diagnostics.Process.Start(psi2);
                if (p2 != null)
                {
                    var output2 = await p2.StandardOutput.ReadToEndAsync();
                    await p2.WaitForExitAsync();
                    if (double.TryParse(output2.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rot2))
                    {
                        return rot2;
                    }
                }
            }
            catch { }
            return 0; // No rotation detected - video is already correct orientation
        }

        private void ClearPreview()
        {
            txtNoPreview.Visibility = Visibility.Visible;
            txtNoPreview.Text = "Select a file to preview";
            txtPreviewLoadingStatus.Text = "";
            imgPreview.Visibility = Visibility.Collapsed;
            pnlTextPreview.Visibility = Visibility.Collapsed;
            pnlVideoPreview.Visibility = Visibility.Collapsed;
            imgPreview.Source = null;
            txtPreview.Text = "";
            txtPreviewFileName.Text = "";
            txtPreviewType.Text = "";
            txtPreviewSize.Text = "";
            txtPreviewPath.Text = "";
            txtPreviewStatus.Text = "";
            txtPreviewDevicePath.Text = "";
            // Stop and clear video
            mediaVideoPreview.Source = null;
            btnPlayVideo.IsEnabled = false;
            btnPauseVideo.IsEnabled = false;
            btnStopVideo.IsEnabled = false;
        }

        private async void MenuUnlockScreen_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDeviceId == null)
            {
                System.Windows.MessageBox.Show("Please select a device first.", "No Device Selected", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                txtStatus.Text = "Unlocking screen...";
                
                // Wake up the device
                await _adbService.ExecuteShellCommandAsync(_selectedDeviceId, "input keyevent KEYCODE_WAKEUP");
                await Task.Delay(200);
                
                // Dismiss keyguard (unlock screen)
                await _adbService.ExecuteShellCommandAsync(_selectedDeviceId, "wm dismiss-keyguard");
                await Task.Delay(200);
                
                // Alternative method: swipe up to unlock (if dismiss-keyguard doesn't work)
                // Get screen dimensions first
                var dims = await _adbService.ExecuteShellCommandAsync(_selectedDeviceId, "wm size");
                var screenWidth = 1080; // Default
                var screenHeight = 1920; // Default
                
                if (dims.Contains("Physical size:"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(dims, @"Physical size: (\d+)x(\d+)");
                    if (match.Success)
                    {
                        screenWidth = int.Parse(match.Groups[1].Value);
                        screenHeight = int.Parse(match.Groups[2].Value);
                    }
                }
                
                // Swipe up gesture (from bottom center to top center)
                var swipeY1 = (int)(screenHeight * 0.8);
                var swipeY2 = (int)(screenHeight * 0.2);
                var swipeX = screenWidth / 2;
                
                await _adbService.ExecuteShellCommandAsync(_selectedDeviceId, 
                    $"input swipe {swipeX} {swipeY1} {swipeX} {swipeY2} 500");
                
                txtStatus.Text = "Screen unlocked (if device has pattern/PIN, you may still need to enter it manually).";
                
                System.Windows.MessageBox.Show(
                    "Unlock command sent to device.\n\n" +
                    "Note: If your device has a lock pattern, PIN, or password, you may still need to enter it manually on the device screen.",
                    "Screen Unlock",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Error unlocking screen: {ex.Message}";
                System.Windows.MessageBox.Show($"Failed to unlock screen: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show(
                "Android File Recovery Tool\n\n" +
                "Recover deleted photos, videos, documents, and more from your Android device.\n\n" +
                "Features:\n" +
                "• Recover deleted files from Android devices\n" +
                "• Preview files before recovery\n" +
                "• Support for images, videos, documents, and audio\n" +
                "• Deep recovery for permanently deleted files (requires root)\n\n" +
                "Version: 1.0",
                "About",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private async void BtnRecoverSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDeviceId == null)
                return;

            var selectedFiles = _recoverableFiles.Where(f => f.IsSelected).ToList();
            if (selectedFiles.Count == 0)
                return;

            var folderDialog = new FolderBrowserDialog
            {
                Description = "Select folder to save recovered files",
                SelectedPath = _recoveryDestinationPath
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _recoveryDestinationPath = folderDialog.SelectedPath;
                await RecoverFiles(selectedFiles);
            }
        }

        private async void BtnRecoverAll_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDeviceId == null || _recoverableFiles.Count == 0)
                return;

            var folderDialog = new FolderBrowserDialog
            {
                Description = "Select folder to save recovered files",
                SelectedPath = _recoveryDestinationPath
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _recoveryDestinationPath = folderDialog.SelectedPath;
                await RecoverFiles(_recoverableFiles.ToList());
            }
        }

        private async Task RecoverFiles(System.Collections.Generic.List<RecoverableFile> files)
        {
            btnRecoverSelected.IsEnabled = false;
            btnRecoverAll.IsEnabled = false;

            int recovered = 0;
            int failed = 0;
            int total = files.Count;

            try
            {
                txtStatus.Text = $"Starting recovery of {total} file(s)...";
                
                foreach (var file in files)
                {
                    if (file.RecoveryStatus == "Recovered")
                    {
                        recovered++;
                        continue;
                    }

                    Dispatcher.Invoke(() =>
                    {
                        txtProgress.Text = $"Recovering {recovered + failed + 1}/{total}... {file.FileName}";
                        txtStatus.Text = $"Recovering: {file.FileName} ({recovered + failed + 1}/{total})";
                    });
                    
                    // Check if file still exists on device before attempting recovery
                    try
                    {
                        var testResult = await _adbService.ExecuteShellCommandAsync(_selectedDeviceId!, $"test -f \"{file.DevicePath}\" && echo EXISTS");
                        bool fileExists = testResult.Contains("EXISTS");
                        
                        if (!fileExists && (file.RecoveryStatus.Contains("Deleted") || file.RecoveryStatus.Contains("Permanently")))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                file.RecoveryStatus = "Cannot Recover - File Permanently Deleted";
                                var index = _recoverableFiles.IndexOf(file);
                                if (index >= 0)
                                {
                                    _recoverableFiles[index] = file;
                                }
                            });
                            failed++;
                            continue;
                        }
                    }
                    catch { }

                    var success = await _recoveryService.RecoverFileAsync(
                        _selectedDeviceId!,
                        file,
                        _recoveryDestinationPath
                    );

                    if (success)
                    {
                        recovered++;
                    }
                    else
                    {
                        failed++;
                    }

                    // Update UI with current file status
                    Dispatcher.Invoke(() =>
                    {
                        var index = _recoverableFiles.IndexOf(file);
                        if (index >= 0)
                        {
                            _recoverableFiles[index] = file;
                        }
                        
                        // Refresh DataGrid
                        dgFiles.Items.Refresh();
                    });
                }

                Dispatcher.Invoke(() =>
                {
                    txtStatus.Text = $"Recovery complete. {recovered} recovered, {failed} failed out of {total} files.";
                    txtProgress.Text = "";
                });
                
                if (recovered > 0)
                {
                    var result = System.Windows.MessageBox.Show(
                        $"Recovery Results:\n\n✓ Successfully recovered: {recovered} file(s)\n✗ Failed: {failed} file(s)\n\n" +
                        $"Recovered files saved to:\n{_recoveryDestinationPath}\n\n" +
                        $"Open recovery folder?",
                        "Recovery Complete",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start("explorer.exe", _recoveryDestinationPath);
                        }
                        catch
                        {
                            System.Windows.MessageBox.Show($"Files saved to: {_recoveryDestinationPath}", "Recovery Complete");
                        }
                    }
                }
                else if (failed > 0)
                {
                    System.Windows.MessageBox.Show(
                        $"Recovery Failed:\n\nCould not recover any files.\n\n" +
                        $"Possible reasons:\n" +
                        $"• Files are permanently deleted (overwritten)\n" +
                        $"• Files require root access for recovery\n" +
                        $"• Files are in inaccessible locations\n" +
                        $"• Device disconnected or unauthorized\n\n" +
                        $"Check file status in the list for details.",
                        "Recovery Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    txtStatus.Text = $"Error during recovery: {ex.Message}";
                    txtProgress.Text = "";
                });
                System.Windows.MessageBox.Show($"Recovery error: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateRecoverButtonStates();
                    btnRecoverAll.IsEnabled = _recoverableFiles.Count > 0;
                    txtProgress.Text = "";
                });
            }
        }
    }
}

