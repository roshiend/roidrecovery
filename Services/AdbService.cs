using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AndroidRecoveryTool.Models;

namespace AndroidRecoveryTool.Services
{
    public class AdbService
    {
        private string _adbPath;
        private List<AndroidDevice> _devices = new List<AndroidDevice>();

        public AdbService()
        {
            _adbPath = FindAdbPath();
        }

        private string FindAdbPath()
        {
            // First, check in the project's local adb folder
            var projectRoot = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(projectRoot))
            {
                // Fallback to current directory if assembly location is null
                projectRoot = Directory.GetCurrentDirectory();
            }

            var localAdbPath = Path.Combine(projectRoot, "adb", "adb.exe");
            if (File.Exists(localAdbPath))
            {
                return localAdbPath;
            }

            // Also check in bin directory (when running from development)
            var binAdbPath = Path.Combine(projectRoot, "..", "..", "..", "adb", "adb.exe");
            if (File.Exists(binAdbPath))
            {
                return Path.GetFullPath(binAdbPath);
            }

            // Check common system installation paths
            var commonPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    "Android", "Sdk", "platform-tools", "adb.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                    "AppData", "Local", "Android", "Sdk", "platform-tools", "adb.exe"),
                @"C:\Android\platform-tools\adb.exe",
                Path.Combine(projectRoot, "..", "adb", "adb.exe"), // Relative to bin folder
            };

            foreach (var path in commonPaths)
            {
                var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(projectRoot, path);
                if (File.Exists(fullPath))
                    return Path.GetFullPath(fullPath);
            }

            // Last resort: try if adb is in PATH
            return "adb";
        }

        public async Task<List<AndroidDevice>> GetConnectedDevicesAsync()
        {
            _devices.Clear();
            
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = "devices -l",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var process = Process.Start(processInfo);
                if (process == null) return _devices;

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                var lines = output.Split('\n')
                    .Where(line => !string.IsNullOrWhiteSpace(line) && 
                                   !line.Contains("List of devices") &&
                                   !line.Contains("daemon") &&
                                   !line.Contains("attached"))
                    .ToList();

                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;

                    var status = parts[1].ToLower();
                    var device = new AndroidDevice
                    {
                        DeviceId = parts[0],
                        Status = status == "device" ? "Authorized" : 
                                 status == "unauthorized" ? "Unauthorized - Tap Allow on Phone" : 
                                 status,
                        StatusColor = status == "device" ? "Green" : "Orange",
                        IsAuthorized = status == "device",
                        ConnectedAt = DateTime.Now
                    };

                    var modelPart = parts.FirstOrDefault(p => p.StartsWith("model:"));
                    if (modelPart != null)
                    {
                        device.DeviceName = modelPart.Replace("model:", "").Replace("_", " ");
                    }
                    else
                    {
                        device.DeviceName = await GetDeviceModelAsync(device.DeviceId);
                    }

                    _devices.Add(device);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting devices: {ex.Message}");
            }

            return _devices;
        }

        private async Task<string> GetDeviceModelAsync(string deviceId)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = $"-s {deviceId} shell getprop ro.product.model",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var process = Process.Start(processInfo);
                if (process == null) return "Unknown Device";

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                return output.Trim();
            }
            catch
            {
                return "Unknown Device";
            }
        }

        public async Task<bool> CheckRootAccessAsync(string deviceId)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = $"-s {deviceId} shell su -c 'id'",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var process = Process.Start(processInfo);
                if (process == null) return false;

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                return output.Contains("uid=0");
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> ExecuteShellCommandAsync(string deviceId, string command)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = $"-s {deviceId} shell {command}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var process = Process.Start(processInfo);
                if (process == null) return string.Empty;

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                return output;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
        
        public async Task<string> GetDeviceUsbStatusAsync(string deviceId)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = $"-s {deviceId} get-state",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var process = Process.Start(processInfo);
                if (process == null) return "unknown";

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                return output.Trim().ToLower();
            }
            catch
            {
                return "error";
            }
        }

        public string GetAdbPath() => _adbPath;
        
        public bool IsAdbAvailable()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = "version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) return false;
                
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task RestartAdbServerAsync()
        {
            try
            {
                // Kill ADB server
                var killProcessInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = "kill-server",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var killProcess = Process.Start(killProcessInfo);
                if (killProcess != null)
                {
                    await killProcess.WaitForExitAsync();
                }

                // Wait a bit
                await Task.Delay(500);

                // Start ADB server
                var startProcessInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = "start-server",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var startProcess = Process.Start(startProcessInfo);
                if (startProcess != null)
                {
                    await startProcess.WaitForExitAsync();
                }

                // Wait for server to be ready
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to restart ADB server: {ex.Message}", ex);
            }
        }
    }
}

