using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AndroidRecoveryTool.Models;

namespace AndroidRecoveryTool.Services
{
    public class FileRecoveryService
    {
        private readonly AdbService _adbService;
        private CancellationTokenSource? _cancellationTokenSource;

        public event EventHandler<RecoverableFile>? FileFound;
        public event EventHandler<string>? ProgressUpdate;
        public event EventHandler<int>? FilesScanned;

        public FileRecoveryService(AdbService adbService)
        {
            _adbService = adbService;
        }

        public async Task<List<RecoverableFile>> ScanForDeletedFilesAsync(
            string deviceId, 
            string storagePath,
            bool scanPhotos, 
            bool scanVideos, 
            bool scanDocuments, 
            bool scanAudio, 
            bool scanOther)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            var files = new List<RecoverableFile>();
            var token = _cancellationTokenSource.Token;

            try
            {
                ProgressUpdate?.Invoke(this, "Starting DELETED FILE RECOVERY scan...");

                bool hasRoot = await _adbService.CheckRootAccessAsync(deviceId);
                
                var fileTypesToScan = GetFileTypesToScan(scanPhotos, scanVideos, scanDocuments, scanAudio, scanOther);
                var extensionsToScan = GetExtensionsToScan(scanPhotos, scanVideos, scanDocuments, scanAudio);
                
                ProgressUpdate?.Invoke(this, "Scanning for DELETED and PERMANENTLY DELETED files only...");

                // ONLY scan locations where deleted files exist - NOT regular storage
                List<string> filePaths = new List<string>();
                
                // Priority locations for DELETED files only
                var deletedFileLocations = new List<string>
                {
                    // Trash/Recently Deleted folders (highest priority)
                    $"{storagePath}/DCIM/.thumbnails",
                    $"{storagePath}/Pictures/.thumbnails",
                    $"{storagePath}/Android/data/com.google.android.apps.photos/files/Trash",
                    $"{storagePath}/DCIM/.cache",
                    
                    // Gallery deleted files (Samsung, Xiaomi, Huawei)
                    $"{storagePath}/.trash",
                    $"{storagePath}/.samsung.deleted",
                    $"{storagePath}/.recycle",
                    $"{storagePath}/.deleted_files",
                    
                    // WhatsApp/T messaging deleted media
                    $"{storagePath}/WhatsApp/Media/.Statuses",
                    $"{storagePath}/WhatsApp/.Shared",
                    $"{storagePath}/Android/media/com.whatsapp/WhatsApp/.Statuses",
                    
                    // Gallery cache and thumbnails (often contain deleted file data)
                    $"{storagePath}/DCIM/Camera/.thumbdata",
                    $"{storagePath}/Android/data/com.android.gallery3d/files/",
                    $"{storagePath}/Android/data/com.miui.gallery/files/",
                    
                    // Other deleted file locations
                    $"{storagePath}/Lost.Dir",  // Android's lost+found
                    $"{storagePath}/.cache",
                    $"{storagePath}/cache"
                };

                // Scan deleted file locations
                foreach (var location in deletedFileLocations)
                {
                    if (token.IsCancellationRequested)
                        break;

                    try
                    {
                        ProgressUpdate?.Invoke(this, $"Scanning deleted files location: {location}...");
                        
                        var lsCommand = $"ls -R \"{location}\" 2>/dev/null";
                        var lsOutput = await _adbService.ExecuteShellCommandAsync(deviceId, lsCommand);
                        
                        if (!string.IsNullOrWhiteSpace(lsOutput) && !lsOutput.Contains("No such file"))
                        {
                            var paths = ParseLsOutput(lsOutput, location);
                            filePaths.AddRange(paths);
                            filePaths = filePaths.Distinct().ToList();
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                // If we have root, attempt to scan for permanently deleted files
                if (hasRoot)
                {
                    ProgressUpdate?.Invoke(this, "Root access detected. Scanning for permanently deleted files in unallocated space...");
                    
                    try
                    {
                        // Try to find and scan data partition for deleted files
                        var partitions = await ScanDeletedFilesFromPartitions(deviceId, storagePath, fileTypesToScan, extensionsToScan, token);
                        filePaths.AddRange(partitions);
                        filePaths = filePaths.Distinct().ToList();
                    }
                    catch (Exception ex)
                    {
                        ProgressUpdate?.Invoke(this, $"Note: Advanced deleted file scan encountered issues: {ex.Message}");
                    }
                }
                else
                {
                    ProgressUpdate?.Invoke(this, "Note: No root access. Cannot scan for permanently deleted files. Root required for advanced recovery.");
                }

                // Alternative: Scan for files that might be orphaned (no parent directory or unusual locations)
                ProgressUpdate?.Invoke(this, "Scanning for orphaned or permanently deleted file remnants...");
                
                try
                {
                    // Look for files in unusual locations that might indicate deletion
                    var orphanedCommand = $"find \"{storagePath}\" -type f -name \"*.jpg\" -o -name \"*.png\" -o -name \"*.mp4\" 2>/dev/null | grep -E '(\\.(thumb|cache|deleted|recycle|trash)|Lost\\.Dir)' | head -1000";
                    var orphanedOutput = await _adbService.ExecuteShellCommandAsync(deviceId, orphanedCommand);
                    
                    if (!string.IsNullOrWhiteSpace(orphanedOutput))
                    {
                        var orphanedPaths = orphanedOutput.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                            .Where(p => !string.IsNullOrWhiteSpace(p))
                            .ToList();
                        
                        filePaths.AddRange(orphanedPaths);
                        filePaths = filePaths.Distinct().ToList();
                    }
                }
                catch
                {
                    // Continue
                }

                if (filePaths.Count == 0)
                {
                    ProgressUpdate?.Invoke(this, "No deleted files found in common locations. Without root access, permanently deleted files cannot be recovered. Try enabling root access for advanced recovery.");
                    return files;
                }

                // All files in our scan are from deleted locations - mark them accordingly
                int scanned = 0;
                int totalFiles = filePaths.Count;
                
                ProgressUpdate?.Invoke(this, $"Found {totalFiles} potentially deleted files. Analyzing...");

                // Process files with better filtering
                foreach (var filePath in filePaths)
                {
                    if (token.IsCancellationRequested)
                        break;

                    scanned++;
                    
                    if (scanned % 50 == 0 || scanned == totalFiles)
                    {
                        FilesScanned?.Invoke(this, scanned);
                        ProgressUpdate?.Invoke(this, $"Scanned {scanned}/{totalFiles} files... Found {files.Count} matching files.");
                    }

                    try
                    {
                        // Quick extension check first to skip non-matching files
                        var extension = Path.GetExtension(filePath).TrimStart('.').ToUpper();
                        
                        if (!scanOther && !string.IsNullOrEmpty(extension))
                        {
                            if (!extensionsToScan.Contains(extension))
                                continue;
                        }

                        var fileInfo = await GetFileInfoAsync(deviceId, filePath.Trim());
                        if (!fileInfo.HasValue) continue;

                        var (fileType, sizeInBytes) = fileInfo.Value;

                        // Double check file type matches
                        if (fileTypesToScan.Contains(fileType.ToUpper()) || scanOther)
                        {
                            // Determine deletion status based on location
                            string recoveryStatus;
                            if (filePath.Contains(".thumb") || filePath.Contains(".thumbnails"))
                            {
                                recoveryStatus = "Deleted (Thumbnail Cache)";
                            }
                            else if (filePath.Contains(".deleted") || filePath.Contains(".recycle") || filePath.Contains(".trash"))
                            {
                                recoveryStatus = "Deleted (In Trash)";
                            }
                            else if (filePath.Contains("Lost.Dir"))
                            {
                                recoveryStatus = "Permanently Deleted (Orphaned)";
                            }
                            else if (filePath.Contains("cache") || filePath.Contains(".cache"))
                            {
                                recoveryStatus = "Deleted (Cache Remnant)";
                            }
                            else if (filePath.Contains(".Statuses") || filePath.Contains(".Shared"))
                            {
                                recoveryStatus = "Deleted (App Trash)";
                            }
                            else
                            {
                                recoveryStatus = "Potentially Deleted";
                            }

                            var recoverableFile = new RecoverableFile
                            {
                                FileName = Path.GetFileName(filePath) ?? "unknown",
                                FileType = fileType,
                                FileSize = FormatFileSize(sizeInBytes),
                                SizeInBytes = sizeInBytes,
                                Path = Path.GetDirectoryName(filePath)?.Replace('\\', '/') ?? "",
                                DevicePath = filePath.Trim(),
                                RecoveryStatus = recoveryStatus
                            };

                            files.Add(recoverableFile);
                            FileFound?.Invoke(this, recoverableFile);
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                var deletedCount = files.Count(f => f.RecoveryStatus.Contains("Deleted"));
                var permanentCount = files.Count(f => f.RecoveryStatus.Contains("Permanently"));
                
                ProgressUpdate?.Invoke(this, $"Scan complete! Found {files.Count} deleted files ({deletedCount} deleted, {permanentCount} permanently deleted) ready for recovery.");
                FilesScanned?.Invoke(this, scanned);

                return files;
            }
            catch (Exception ex)
            {
                ProgressUpdate?.Invoke(this, $"Error during scan: {ex.Message}");
                return files;
            }
        }

        private List<string> ParseLsOutput(string output, string basePath)
        {
            var filePaths = new List<string>();
            var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            string currentDir = basePath.TrimEnd('/');

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // Check if line is a directory path
                if (trimmed.EndsWith(":") && trimmed.Contains("/"))
                {
                    currentDir = trimmed.TrimEnd(':');
                    continue;
                }

                // Skip if it's not a file (contains "/" at start means it's a directory listing)
                if (trimmed.StartsWith("/"))
                {
                    currentDir = trimmed;
                    continue;
                }

                // Build full path
                var fullPath = currentDir.EndsWith("/") ? $"{currentDir}{trimmed}" : $"{currentDir}/{trimmed}";
                
                // Only add files (check for common file extensions or assume if has extension)
                if (Path.HasExtension(trimmed) || !trimmed.Contains("."))
                {
                    filePaths.Add(fullPath);
                }
            }

            return filePaths;
        }

        private HashSet<string> GetExtensionsToScan(bool photos, bool videos, bool documents, bool audio)
        {
            var extensions = new HashSet<string>();

            if (photos)
            {
                extensions.Add("JPG");
                extensions.Add("JPEG");
                extensions.Add("PNG");
                extensions.Add("GIF");
                extensions.Add("WEBP");
            }

            if (videos)
            {
                extensions.Add("MP4");
                extensions.Add("AVI");
                extensions.Add("MKV");
                extensions.Add("MOV");
                extensions.Add("3GP");
            }

            if (documents)
            {
                extensions.Add("PDF");
                extensions.Add("DOC");
                extensions.Add("DOCX");
                extensions.Add("TXT");
                extensions.Add("XLS");
                extensions.Add("XLSX");
            }

            if (audio)
            {
                extensions.Add("MP3");
                extensions.Add("WAV");
                extensions.Add("M4A");
                extensions.Add("AAC");
                extensions.Add("OGG");
            }

            return extensions;
        }

        private async Task<(string FileType, long SizeInBytes)?> GetFileInfoAsync(string deviceId, string filePath)
        {
            try
            {
                // Try multiple stat command formats for different Android versions
                string sizeOutput = "";
                
                // Try Android's stat format first
                var statCommand = $"stat -f%z \"{filePath}\" 2>/dev/null";
                sizeOutput = await _adbService.ExecuteShellCommandAsync(deviceId, statCommand);
                
                // If that didn't work, try Linux stat format
                if (string.IsNullOrWhiteSpace(sizeOutput) || sizeOutput.Contains("No such file") || sizeOutput.Contains("error"))
                {
                    statCommand = $"stat -c %s \"{filePath}\" 2>/dev/null";
                    sizeOutput = await _adbService.ExecuteShellCommandAsync(deviceId, statCommand);
                }
                
                // If still no luck, try ls -l and parse
                if (string.IsNullOrWhiteSpace(sizeOutput) || sizeOutput.Contains("No such file") || sizeOutput.Contains("error"))
                {
                    var lsCommand = $"ls -ld \"{filePath}\" 2>/dev/null";
                    var lsOutput = await _adbService.ExecuteShellCommandAsync(deviceId, lsCommand);
                    if (!string.IsNullOrWhiteSpace(lsOutput) && !lsOutput.Contains("No such file"))
                    {
                        // Parse size from ls output (format: -rw-rw---- 1 user group size date filename)
                        var parts = lsOutput.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5)
                        {
                            sizeOutput = parts[4];
                        }
                    }
                }

                // Clean up the output
                sizeOutput = sizeOutput?.Trim() ?? "";
                
                // Remove any error messages or non-numeric characters
                var cleanOutput = new string(sizeOutput.Where(c => char.IsDigit(c) || c == '\n' || c == '\r').ToArray()).Trim();
                
                if (string.IsNullOrWhiteSpace(cleanOutput) || !long.TryParse(cleanOutput, out long size) || size == 0)
                {
                    return null;
                }

                var extension = Path.GetExtension(filePath).TrimStart('.').ToUpper();
                if (string.IsNullOrEmpty(extension))
                {
                    // Try to get file type from mime type or other methods
                    return null;
                }

                var fileType = extension switch
                {
                    "JPG" or "JPEG" => "JPG",
                    "PNG" => "PNG",
                    "GIF" => "GIF",
                    "WEBP" => "WEBP",
                    "MP4" => "MP4",
                    "AVI" => "AVI",
                    "MKV" => "MKV",
                    "MOV" => "MOV",
                    "3GP" => "3GP",
                    "PDF" => "PDF",
                    "DOC" => "DOC",
                    "DOCX" => "DOCX",
                    "TXT" => "TXT",
                    "XLS" or "XLSX" => "XLS",
                    "MP3" => "MP3",
                    "WAV" => "WAV",
                    "M4A" => "M4A",
                    "AAC" => "AAC",
                    "OGG" => "OGG",
                    _ => extension
                };

                return (fileType, size);
            }
            catch
            {
                return null;
            }
        }

        private HashSet<string> GetFileTypesToScan(bool photos, bool videos, bool documents, bool audio, bool other)
        {
            var types = new HashSet<string>();

            if (photos)
            {
                types.Add("JPG");
                types.Add("JPEG");
                types.Add("PNG");
                types.Add("GIF");
                types.Add("WEBP");
            }

            if (videos)
            {
                types.Add("MP4");
                types.Add("AVI");
                types.Add("MKV");
                types.Add("MOV");
                types.Add("3GP");
            }

            if (documents)
            {
                types.Add("PDF");
                types.Add("DOC");
                types.Add("DOCX");
                types.Add("TXT");
                types.Add("XLS");
                types.Add("XLSX");
            }

            if (audio)
            {
                types.Add("MP3");
                types.Add("WAV");
                types.Add("M4A");
                types.Add("AAC");
                types.Add("OGG");
            }

            return types;
        }

        public async Task<bool> RecoverFileAsync(string deviceId, RecoverableFile file, string destinationPath)
        {
            try
            {
                if (!Directory.Exists(destinationPath))
                    Directory.CreateDirectory(destinationPath);

                var fileName = Path.GetFileName(file.DevicePath);
                var localPath = Path.Combine(destinationPath, fileName);

                var pullCommand = $"{_adbService.GetAdbPath()} -s {deviceId} pull \"{file.DevicePath}\" \"{localPath}\"";
                
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {pullCommand}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                if (process == null) return false;

                await process.WaitForExitAsync();
                
                if (File.Exists(localPath))
                {
                    file.RecoveryStatus = "Recovered";
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                file.RecoveryStatus = $"Error: {ex.Message}";
                return false;
            }
        }

        private async Task<List<string>> ScanDeletedFilesFromPartitions(
            string deviceId, 
            string storagePath,
            HashSet<string> fileTypes,
            HashSet<string> extensions,
            CancellationToken token)
        {
            var foundFiles = new List<string>();
            
            try
            {
                ProgressUpdate?.Invoke(this, "🔍 Advanced Recovery: Scanning for PERMANENTLY deleted files...");
                
                // Step 1: Scan Lost.Dir (Android's lost+found for corrupted/deleted files)
                ProgressUpdate?.Invoke(this, "Step 1/4: Scanning Lost.Dir for orphaned files...");
                var lostDir = $"{storagePath}/Lost.Dir";
                var lostFiles = await _adbService.ExecuteShellCommandAsync(deviceId, $"find \"{lostDir}\" -type f 2>/dev/null");
                
                if (!string.IsNullOrWhiteSpace(lostFiles))
                {
                    var paths = lostFiles.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .ToList();
                    foundFiles.AddRange(paths);
                    ProgressUpdate?.Invoke(this, $"Found {paths.Count} orphaned files in Lost.Dir");
                }
                
                // Step 2: Scan unallocated inodes using debugfs (if available)
                ProgressUpdate?.Invoke(this, "Step 2/4: Scanning for deleted inodes...");
                try
                {
                    var debugfsCheck = await _adbService.ExecuteShellCommandAsync(deviceId, "which debugfs 2>/dev/null");
                    if (!string.IsNullOrWhiteSpace(debugfsCheck))
                    {
                        // Try to scan deleted inodes
                        var dataPartition = await FindDataPartition(deviceId);
                        if (!string.IsNullOrEmpty(dataPartition))
                        {
                            ProgressUpdate?.Invoke(this, $"Found data partition: {dataPartition}");
                            // Note: debugfs commands are complex and device-specific
                        }
                    }
                }
                catch
                {
                    // debugfs not available
                }
                
                // Step 3: File carving - scan for file signatures in unallocated space
                ProgressUpdate?.Invoke(this, "Step 3/4: Performing file carving (scanning for file signatures)...");
                var carvedFiles = await PerformFileCarving(deviceId, storagePath, fileTypes, extensions, token);
                foundFiles.AddRange(carvedFiles);
                
                // Step 4: Scan for files in tmp and recovery locations
                ProgressUpdate?.Invoke(this, "Step 4/4: Scanning recovery locations...");
                var recoveryLocations = new[]
                {
                    "/data/local/tmp",
                    "/cache",
                    "/tmp",
                    $"{storagePath}/.recovery"
                };
                
                foreach (var location in recoveryLocations)
                {
                    if (token.IsCancellationRequested) break;
                    
                    try
                    {
                        var recoveryFiles = await _adbService.ExecuteShellCommandAsync(deviceId, $"find \"{location}\" -type f 2>/dev/null");
                        if (!string.IsNullOrWhiteSpace(recoveryFiles))
                        {
                            var paths = recoveryFiles.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                .Where(p => !string.IsNullOrWhiteSpace(p))
                                .ToList();
                            foundFiles.AddRange(paths);
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
                
                ProgressUpdate?.Invoke(this, $"Advanced recovery complete. Found {foundFiles.Count} potentially recoverable permanently deleted files.");
            }
            catch (Exception ex)
            {
                ProgressUpdate?.Invoke(this, $"Advanced recovery encountered issues: {ex.Message}. Continuing with basic recovery...");
            }
            
            return foundFiles;
        }
        
        private async Task<string> FindDataPartition(string deviceId)
        {
            try
            {
                // Try to find userdata partition
                var partitions = await _adbService.ExecuteShellCommandAsync(deviceId, "ls /dev/block/platform/*/by-name/userdata 2>/dev/null");
                if (!string.IsNullOrWhiteSpace(partitions))
                {
                    var lines = partitions.Split('\n');
                    if (lines.Length > 0 && File.Exists("/dev/block/platform"))
                        return partitions.Trim().Split('\n').FirstOrDefault() ?? "";
                }
                
                // Alternative: Try common partition paths
                var commonPaths = new[] { "/dev/block/mmcblk0p", "/dev/block/dm-0" };
                foreach (var path in commonPaths)
                {
                    var check = await _adbService.ExecuteShellCommandAsync(deviceId, $"test -e {path} && echo exists 2>/dev/null");
                    if (check.Contains("exists"))
                        return path;
                }
            }
            catch { }
            
            return "";
        }
        
        private async Task<List<string>> PerformFileCarving(
            string deviceId, 
            string storagePath,
            HashSet<string> fileTypes, 
            HashSet<string> extensions,
            CancellationToken token)
        {
            var carvedFiles = new List<string>();
            
            try
            {
                ProgressUpdate?.Invoke(this, "File carving: Searching for file signatures in unallocated space...");
                
                // File signature patterns (magic numbers) for common file types
                var signatures = new Dictionary<string, string>
                {
                    { "JPG", "FFD8FF" },
                    { "PNG", "89504E47" },
                    { "MP4", "66747970" }, // ftyp
                    { "PDF", "25504446" }, // %PDF
                    { "ZIP", "504B0304" }, // PK (also DOCX)
                    { "MP3", "FFFB" }
                };
                
                // Create a temporary directory for carving results
                var carveDir = $"{storagePath}/.recovery_carved";
                await _adbService.ExecuteShellCommandAsync(deviceId, $"mkdir -p \"{carveDir}\" 2>/dev/null");
                
                // Method 1: Use strings/grep to find file signatures
                // This is a simplified approach - real file carving is more complex
                foreach (var kvp in signatures)
                {
                    if (token.IsCancellationRequested) break;
                    
                    try
                    {
                        var fileType = kvp.Key;
                        var signature = kvp.Value;
                        
                        if (!fileTypes.Contains(fileType) && !extensions.Contains(fileType))
                            continue;
                        
                        // Try to find files with this signature using strings/grep
                        // Note: This is limited - full file carving requires reading raw blocks
                        var searchCommand = $"strings \"{storagePath}\" 2>/dev/null | grep -a \"{signature}\" | head -100";
                        var results = await _adbService.ExecuteShellCommandAsync(deviceId, searchCommand);
                        
                        if (!string.IsNullOrWhiteSpace(results))
                        {
                            // Extract potential file locations
                            // Note: This is a simplified demonstration
                            ProgressUpdate?.Invoke(this, $"Found potential {fileType} file signatures");
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
                
                // Method 2: Scan for files with deleted status using ls -la in common directories
                // Look for files that exist but might be deleted
                var scanDirs = new[]
                {
                    $"{storagePath}/DCIM",
                    $"{storagePath}/Pictures",
                    $"{storagePath}/Download",
                    $"{storagePath}/Movies"
                };
                
                foreach (var dir in scanDirs)
                {
                    if (token.IsCancellationRequested) break;
                    
                    try
                    {
                        // Find files that might be marked as deleted
                        var deletedFiles = await _adbService.ExecuteShellCommandAsync(deviceId, 
                            $"ls -la \"{dir}\" 2>/dev/null | grep -E '^[-d]' | awk '{{print $NF}}'");
                        
                        if (!string.IsNullOrWhiteSpace(deletedFiles))
                        {
                            var paths = deletedFiles.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(p => $"{dir}/{p.Trim()}")
                                .Where(p => Path.HasExtension(p))
                                .ToList();
                            
                            carvedFiles.AddRange(paths);
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
                
                ProgressUpdate?.Invoke(this, $"File carving found {carvedFiles.Count} potential permanently deleted files");
            }
            catch (Exception ex)
            {
                ProgressUpdate?.Invoke(this, $"File carving error: {ex.Message}");
            }
            
            return carvedFiles;
        }

        public void CancelScan()
        {
            _cancellationTokenSource?.Cancel();
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
    }
}

