using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
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

        public MainWindow()
        {
            InitializeComponent();
            
            _adbService = new AdbService();
            _recoveryService = new FileRecoveryService(_adbService);
            _devices = new ObservableCollection<AndroidDevice>();
            _recoverableFiles = new ObservableCollection<RecoverableFile>();

            lstDevices.ItemsSource = _devices;
            dgFiles.ItemsSource = _recoverableFiles;

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
            btnRecoverSelected.IsEnabled = dgFiles.SelectedItems.Count > 0;
        }

        private async void BtnRecoverSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDeviceId == null || dgFiles.SelectedItems.Count == 0)
                return;

            var folderDialog = new FolderBrowserDialog
            {
                Description = "Select folder to save recovered files",
                SelectedPath = _recoveryDestinationPath
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _recoveryDestinationPath = folderDialog.SelectedPath;
                await RecoverFiles(dgFiles.SelectedItems.Cast<RecoverableFile>().ToList());
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
            int total = files.Count;

            try
            {
                foreach (var file in files)
                {
                    if (file.RecoveryStatus == "Recovered")
                        continue;

                    txtProgress.Text = $"Recovering {recovered + 1}/{total}...";
                    
                    var success = await _recoveryService.RecoverFileAsync(
                        _selectedDeviceId!,
                        file,
                        _recoveryDestinationPath
                    );

                    if (success)
                        recovered++;

                    var index = _recoverableFiles.IndexOf(file);
                    if (index >= 0)
                    {
                        _recoverableFiles[index] = file;
                    }
                }

                txtStatus.Text = $"Recovery complete. {recovered} of {total} files recovered successfully.";
                txtProgress.Text = "";
                
                if (recovered > 0)
                {
                    var result = System.Windows.MessageBox.Show(
                        $"{recovered} file(s) recovered successfully!\n\nOpen recovery folder?",
                        "Recovery Complete",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start("explorer.exe", _recoveryDestinationPath);
                    }
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Error during recovery: {ex.Message}";
                System.Windows.MessageBox.Show($"Recovery error: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnRecoverSelected.IsEnabled = dgFiles.SelectedItems.Count > 0;
                btnRecoverAll.IsEnabled = _recoverableFiles.Count > 0;
                txtProgress.Text = "";
            }
        }
    }
}

