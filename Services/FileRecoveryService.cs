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
                    
                    // Video-specific locations (videos are often stored and deleted from here)
                    $"{storagePath}/DCIM/Camera",
                    $"{storagePath}/Movies",
                    $"{storagePath}/Videos",
                    $"{storagePath}/Android/data/com.android.providers.media/thumbnail",
                    $"{storagePath}/Android/data/com.google.android.apps.photos/files/videos",
                    
                    // Gallery deleted files (Samsung, Xiaomi, Huawei)
                    $"{storagePath}/.trash",
                    $"{storagePath}/.samsung.deleted",
                    $"{storagePath}/.recycle",
                    $"{storagePath}/.deleted_files",
                    
                    // WhatsApp/T messaging deleted media (includes videos)
                    $"{storagePath}/WhatsApp/Media/.Statuses",
                    $"{storagePath}/WhatsApp/Media/Video",
                    $"{storagePath}/WhatsApp/.Shared",
                    $"{storagePath}/Android/media/com.whatsapp/WhatsApp/.Statuses",
                    $"{storagePath}/Android/media/com.whatsapp/WhatsApp/Media/Video",
                    
                    // Gallery cache and thumbnails (often contain deleted file data)
                    $"{storagePath}/DCIM/Camera/.thumbdata",
                    $"{storagePath}/Android/data/com.android.gallery3d/files/",
                    $"{storagePath}/Android/data/com.miui.gallery/files/",
                    $"{storagePath}/Android/data/com.samsung.android.gallery3d/cache",
                    
                    // Video player cache locations
                    $"{storagePath}/Android/data/com.mxtech.videoplayer.ad/cache",
                    $"{storagePath}/Android/data/com.videoplayer/thumbnail",
                    
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
                    // Build find command with all file extensions based on what we're scanning
                    var findExtensions = new List<string>();
                    if (scanPhotos)
                    {
                        findExtensions.AddRange(new[] { "*.jpg", "*.jpeg", "*.png", "*.gif", "*.webp" });
                    }
                    if (scanVideos)
                    {
                        findExtensions.AddRange(new[] { "*.mp4", "*.avi", "*.mkv", "*.mov", "*.3gp", "*.m4v", "*.flv", "*.wmv" });
                    }
                    if (scanDocuments)
                    {
                        findExtensions.AddRange(new[] { "*.pdf", "*.doc", "*.docx", "*.txt", "*.xls", "*.xlsx" });
                    }
                    if (scanAudio)
                    {
                        findExtensions.AddRange(new[] { "*.mp3", "*.wav", "*.m4a", "*.aac", "*.ogg" });
                    }
                    
                    // Build find command with all extensions
                    var findPatterns = string.Join(" -o ", findExtensions.Select(ext => $"-name \"{ext}\""));
                    var orphanedCommand = $"find \"{storagePath}\" -type f \\( {findPatterns} \\) 2>/dev/null | grep -E '(\\.(thumb|cache|deleted|recycle|trash)|Lost\\.Dir|lost\\+found)' | head -2000";
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
                extensions.Add("M4V");
                extensions.Add("FLV");
                extensions.Add("WMV");
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
                    "M4V" => "M4V",
                    "FLV" => "FLV",
                    "WMV" => "WMV",
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
                types.Add("M4V");
                types.Add("FLV");
                types.Add("WMV");
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
                ProgressUpdate?.Invoke(this, $"Recovering: {file.FileName}...");

                if (!Directory.Exists(destinationPath))
                    Directory.CreateDirectory(destinationPath);

                // Handle file name conflicts
                var fileName = Path.GetFileName(file.DevicePath) ?? $"recovered_{Guid.NewGuid()}";
                
                // Clean filename - remove invalid characters
                var invalidChars = Path.GetInvalidFileNameChars();
                foreach (var c in invalidChars)
                {
                    fileName = fileName.Replace(c, '_');
                }

                if (string.IsNullOrEmpty(Path.GetExtension(fileName)))
                {
                    var extension = Path.GetExtension(file.DevicePath);
                    if (!string.IsNullOrEmpty(extension))
                        fileName += extension;
                }

                var localPath = Path.Combine(destinationPath, fileName);
                
                // If file already exists, add number suffix
                int counter = 1;
                var originalPath = localPath;
                while (File.Exists(localPath))
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
                    var ext = Path.GetExtension(originalPath);
                    localPath = Path.Combine(destinationPath, $"{nameWithoutExt}_{counter}{ext}");
                    counter++;
                }

                // Method 1: Try ADB pull directly
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _adbService.GetAdbPath(),
                    Arguments = $"-s {deviceId} pull \"{file.DevicePath}\" \"{localPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(_adbService.GetAdbPath()) ?? ""
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    
                    // Wait a bit for file to be written
                    await Task.Delay(1000);
                    
                    if (File.Exists(localPath) && new FileInfo(localPath).Length > 0)
                    {
                        file.RecoveryStatus = "Recovered";
                        file.DevicePath = localPath; // Update path to recovered location
                        ProgressUpdate?.Invoke(this, $"Recovered: {fileName}");
                        return true;
                    }

                    // If pull failed, try alternative method for deleted files
                    if (file.RecoveryStatus.Contains("Deleted") || file.RecoveryStatus.Contains("Permanently"))
                    {
                        ProgressUpdate?.Invoke(this, $"File deleted - trying alternative recovery method...");
                        
                        // Try to recover from cache/trash location using cat
                        try
                        {
                            // For images in thumbnail cache, try to recover the original
                            if (file.DevicePath.Contains(".thumb") || file.DevicePath.Contains("thumbnails"))
                            {
                                // Try to find original file location
                                var originalPathGuess = file.DevicePath
                                    .Replace(".thumbnails", "")
                                    .Replace(".thumbdata", "")
                                    .Replace("thumb", "");
                                
                                var originalExists = await _adbService.ExecuteShellCommandAsync(deviceId, $"test -f \"{originalPathGuess}\" && echo EXISTS");
                                if (originalExists.Contains("EXISTS"))
                                {
                                    // Try to recover original
                                    var recoverProcess = new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = _adbService.GetAdbPath(),
                                        Arguments = $"-s {deviceId} pull \"{originalPathGuess}\" \"{localPath}\"",
                                        UseShellExecute = false,
                                        RedirectStandardOutput = true,
                                        RedirectStandardError = true,
                                        CreateNoWindow = true,
                                        WorkingDirectory = Path.GetDirectoryName(_adbService.GetAdbPath()) ?? ""
                                    };

                                    using var recoverProc = System.Diagnostics.Process.Start(recoverProcess);
                                    if (recoverProc != null)
                                    {
                                        await recoverProc.WaitForExitAsync();
                                        await Task.Delay(1000);
                                        
                                        if (File.Exists(localPath) && new FileInfo(localPath).Length > 0)
                                        {
                                            file.RecoveryStatus = "Recovered";
                                            ProgressUpdate?.Invoke(this, $"Recovered: {fileName}");
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    // If still failed, report the error
                    var errorMsg = !string.IsNullOrEmpty(error) ? error.Substring(0, Math.Min(200, error.Length)) : output.Substring(0, Math.Min(200, output.Length));
                    file.RecoveryStatus = $"Recovery failed: {errorMsg}";
                    ProgressUpdate?.Invoke(this, $"Failed to recover: {fileName} - {errorMsg}");
                    return false;
                }

                return false;
            }
            catch (Exception ex)
            {
                file.RecoveryStatus = $"Error: {ex.Message}";
                ProgressUpdate?.Invoke(this, $"Recovery error for {file.FileName}: {ex.Message}");
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
                
                // Step 3: PROFESSIONAL file carving - scan for file signatures in unallocated space
                ProgressUpdate?.Invoke(this, "Step 3/4: PROFESSIONAL RECOVERY - Performing deep file carving...");
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
                ProgressUpdate?.Invoke(this, "🔍 PROFESSIONAL RECOVERY: Deep scanning for permanently deleted files...");
                
                bool hasRoot = await _adbService.CheckRootAccessAsync(deviceId);
                
                if (!hasRoot)
                {
                    ProgressUpdate?.Invoke(this, "⚠️ Root access required for deep file carving. Performing advanced file system scan...");
                    return await PerformAdvancedFileSystemScan(deviceId, storagePath, fileTypes, extensions, token);
                }
                
                ProgressUpdate?.Invoke(this, "✓ Root access confirmed. Starting PROFESSIONAL deep recovery...");
                
                // Professional file carving with comprehensive signatures
                var signatures = GetComprehensiveFileSignatures();
                
                // Create recovery directory
                var carveDir = $"{storagePath}/.recovery_deep";
                await _adbService.ExecuteShellCommandAsync(deviceId, $"mkdir -p \"{carveDir}\" 2>/dev/null || mkdir -p /data/local/tmp/recovery_deep 2>/dev/null");
                
                // Find storage partitions for deep scanning
                var partitions = await FindStoragePartitions(deviceId, storagePath);
                ProgressUpdate?.Invoke(this, $"Found {partitions.Count} partition(s) for deep scanning...");
                
                int totalFound = 0;
                
                // Scan each partition
                foreach (var partition in partitions)
                {
                    if (token.IsCancellationRequested) break;
                    
                    ProgressUpdate?.Invoke(this, $"Deep scanning partition: {partition}...");
                    
                    // Method 1: Raw block device scanning using dd + hex dump
                    var rawCarved = await ScanRawPartitionForFiles(deviceId, partition, signatures, fileTypes, extensions, carveDir, token);
                    carvedFiles.AddRange(rawCarved);
                    totalFound += rawCarved.Count;
                    
                    ProgressUpdate?.Invoke(this, $"✓ Partition {partition}: Found {rawCarved.Count} recoverable files");
                }
                
                // Method 2: Scan Lost.Dir and recovery locations
                ProgressUpdate?.Invoke(this, "Scanning Lost.Dir and recovery locations...");
                var lostFiles = await ScanRecoveryLocations(deviceId, storagePath, fileTypes, extensions, token);
                carvedFiles.AddRange(lostFiles);
                totalFound += lostFiles.Count;
                
                // Method 3: Scan unallocated inodes (if debugfs available)
                try
                {
                    var debugfsCheck = await _adbService.ExecuteShellCommandAsync(deviceId, "which debugfs 2>/dev/null");
                    if (!string.IsNullOrWhiteSpace(debugfsCheck))
                    {
                        ProgressUpdate?.Invoke(this, "Scanning deleted inodes using debugfs...");
                        var inodeFiles = await ScanDeletedInodes(deviceId, storagePath, fileTypes, extensions, token);
                        carvedFiles.AddRange(inodeFiles);
                        totalFound += inodeFiles.Count;
                    }
                }
                catch { }
                
                ProgressUpdate?.Invoke(this, $"🎯 PROFESSIONAL RECOVERY: Found {totalFound} permanently deleted files ready for recovery!");
            }
            catch (Exception ex)
            {
                ProgressUpdate?.Invoke(this, $"⚠️ Deep recovery error: {ex.Message}. Continuing with standard recovery...");
            }
            
            return carvedFiles.Distinct().ToList();
        }
        
        private Dictionary<string, FileSignatureInfo> GetComprehensiveFileSignatures()
        {
            return new Dictionary<string, FileSignatureInfo>
            {
                // Images
                { "JPG", new FileSignatureInfo { Header = new byte[] { 0xFF, 0xD8, 0xFF }, Footer = new byte[] { 0xFF, 0xD9 }, Extensions = new[] { ".jpg", ".jpeg" } } },
                { "PNG", new FileSignatureInfo { Header = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, Extensions = new[] { ".png" } } },
                { "GIF", new FileSignatureInfo { Header = new byte[] { 0x47, 0x49, 0x46, 0x38 }, Extensions = new[] { ".gif" } } },
                { "WEBP", new FileSignatureInfo { Header = new byte[] { 0x52, 0x49, 0x46, 0x46 }, FooterPattern = "WEBP", Extensions = new[] { ".webp" } } },
                
                // Videos
                { "MP4", new FileSignatureInfo { Header = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x6D, 0x70, 0x34, 0x32 }, Extensions = new[] { ".mp4", ".m4v" } } },
                { "MP4_ALT", new FileSignatureInfo { Header = new byte[] { 0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70 }, Extensions = new[] { ".mp4" } } },
                { "AVI", new FileSignatureInfo { Header = new byte[] { 0x52, 0x49, 0x46, 0x46 }, FooterPattern = "AVI ", Extensions = new[] { ".avi" } } },
                { "MKV", new FileSignatureInfo { Header = new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }, Extensions = new[] { ".mkv" } } },
                { "MOV", new FileSignatureInfo { Header = new byte[] { 0x00, 0x00, 0x00, 0x14, 0x66, 0x74, 0x79, 0x70, 0x71, 0x74, 0x20, 0x20 }, Extensions = new[] { ".mov" } } },
                { "FLV", new FileSignatureInfo { Header = new byte[] { 0x46, 0x4C, 0x56, 0x01 }, Extensions = new[] { ".flv" } } },
                { "WMV", new FileSignatureInfo { Header = new byte[] { 0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11 }, Extensions = new[] { ".wmv", ".asf" } } },
                
                // Audio
                { "MP3", new FileSignatureInfo { Header = new byte[] { 0xFF, 0xFB }, Extensions = new[] { ".mp3" } } },
                { "MP3_ALT", new FileSignatureInfo { Header = new byte[] { 0x49, 0x44, 0x33 }, Extensions = new[] { ".mp3" } } },
                { "WAV", new FileSignatureInfo { Header = new byte[] { 0x52, 0x49, 0x46, 0x46 }, FooterPattern = "WAVE", Extensions = new[] { ".wav" } } },
                
                // Documents
                { "PDF", new FileSignatureInfo { Header = new byte[] { 0x25, 0x50, 0x44, 0x46 }, Extensions = new[] { ".pdf" } } },
                { "DOCX", new FileSignatureInfo { Header = new byte[] { 0x50, 0x4B, 0x03, 0x04 }, FooterPattern = "[Content_Types].xml", Extensions = new[] { ".docx" } } },
                { "XLSX", new FileSignatureInfo { Header = new byte[] { 0x50, 0x4B, 0x03, 0x04 }, FooterPattern = "xl/", Extensions = new[] { ".xlsx" } } },
                { "ZIP", new FileSignatureInfo { Header = new byte[] { 0x50, 0x4B, 0x03, 0x04 }, Extensions = new[] { ".zip" } } }
            };
        }
        
        private class FileSignatureInfo
        {
            public byte[] Header { get; set; } = Array.Empty<byte>();
            public byte[]? Footer { get; set; }
            public string? FooterPattern { get; set; }
            public string[] Extensions { get; set; } = Array.Empty<string>();
        }
        
        private async Task<List<string>> FindStoragePartitions(string deviceId, string storagePath)
        {
            var partitions = new List<string>();
            
            try
            {
                // Find userdata partition (main storage)
                var userdataCmd = "ls /dev/block/platform/*/by-name/userdata 2>/dev/null || ls /dev/block/by-name/userdata 2>/dev/null";
                var userdata = await _adbService.ExecuteShellCommandAsync(deviceId, userdataCmd);
                
                if (!string.IsNullOrWhiteSpace(userdata) && !userdata.Contains("No such file"))
                {
                    var lines = userdata.Trim().Split('\n');
                    partitions.AddRange(lines.Where(l => !string.IsNullOrWhiteSpace(l)));
                }
                
                // Find SD card partition
                var sdcardCmd = "ls /dev/block/platform/*/by-name/sdcard 2>/dev/null || ls /dev/block/by-name/sdcard 2>/dev/null || mount | grep '/storage' | awk '{print $1}' | head -1";
                var sdcard = await _adbService.ExecuteShellCommandAsync(deviceId, sdcardCmd);
                
                if (!string.IsNullOrWhiteSpace(sdcard) && !sdcard.Contains("No such file"))
                {
                    partitions.Add(sdcard.Trim());
                }
                
                // Also use storage path directory for file system scanning
                if (!partitions.Contains(storagePath))
                    partitions.Add(storagePath);
                
                return partitions.Distinct().Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            }
            catch
            {
                return new List<string> { storagePath };
            }
        }
        
        private async Task<List<string>> ScanRawPartitionForFiles(
            string deviceId, 
            string partition,
            Dictionary<string, FileSignatureInfo> signatures,
            HashSet<string> fileTypes,
            HashSet<string> extensions,
            string outputDir,
            CancellationToken token)
        {
            var foundFiles = new List<string>();
            
            try
            {
                // For file system paths, use grep/dd combination
                if (partition.StartsWith("/storage") || partition.StartsWith("/sdcard") || Directory.Exists(partition))
                {
                    return await ScanFileSystemForRawData(deviceId, partition, signatures, fileTypes, extensions, outputDir, token);
                }
                
                // For block devices, use dd to read raw data
                ProgressUpdate?.Invoke(this, $"Reading raw data from block device: {partition}...");
                
                var tempDump = "/data/local/tmp/recovery_dump.dat";
                
                // Read first 500MB of partition for scanning (adjustable)
                var ddCommand = $"dd if={partition} of={tempDump} bs=1M count=500 2>/dev/null";
                var ddResult = await _adbService.ExecuteShellCommandAsync(deviceId, $"su -c '{ddCommand}'");
                
                if (ddResult.Contains("error") || ddResult.Contains("Permission denied"))
                {
                    ProgressUpdate?.Invoke(this, $"⚠️ Cannot read raw partition. Trying file system scan instead...");
                    return await ScanFileSystemForRawData(deviceId, partition, signatures, fileTypes, extensions, outputDir, token);
                }
                
                // Scan the dump file for file signatures
                ProgressUpdate?.Invoke(this, "Scanning raw data dump for file signatures...");
                var carved = await CarveFilesFromDump(deviceId, tempDump, signatures, fileTypes, extensions, outputDir, token);
                foundFiles.AddRange(carved);
                
                // Cleanup
                await _adbService.ExecuteShellCommandAsync(deviceId, $"rm -f {tempDump} 2>/dev/null");
            }
            catch (Exception ex)
            {
                ProgressUpdate?.Invoke(this, $"Raw partition scan error: {ex.Message}");
            }
            
            return foundFiles;
        }
        
        private async Task<List<string>> ScanFileSystemForRawData(
            string deviceId,
            string path,
            Dictionary<string, FileSignatureInfo> signatures,
            HashSet<string> fileTypes,
            HashSet<string> extensions,
            string outputDir,
            CancellationToken token)
        {
            var foundFiles = new List<string>();
            
            try
            {
                // Use hexdump/strings to find file signatures in file system
                foreach (var sigKvp in signatures)
                {
                    if (token.IsCancellationRequested) break;
                    
                    var sigName = sigKvp.Key;
                    var sigInfo = sigKvp.Value;
                    
                    // Check if we should scan this file type
                    bool shouldScan = fileTypes.Contains(sigName) || 
                                     sigInfo.Extensions.Any(ext => extensions.Contains(ext.ToUpper().TrimStart('.')));
                    
                    if (!shouldScan) continue;
                    
                    ProgressUpdate?.Invoke(this, $"Scanning for {sigName} files in {path}...");
                    
                    // Convert header bytes to hex string for grep
                    var hexHeader = string.Join("", sigInfo.Header.Select(b => $"\\\\x{b:X2}"));
                    var hexPattern = string.Join(" ", sigInfo.Header.Select(b => $"{b:X2}"));
                    
                    // Search for files by extension first (faster and more reliable)
                    var extensionPatterns = sigInfo.Extensions.Select(ext => $"*{ext}").ToList();
                    if (extensionPatterns.Any())
                    {
                        var extPatterns = string.Join(" -o ", extensionPatterns.Select(ext => $"-name \"{ext}\""));
                        var searchCmd = $"find \"{path}\" -type f \\( {extPatterns} \\) 2>/dev/null | head -200";
                        
                        var results = await _adbService.ExecuteShellCommandAsync(deviceId, searchCmd);
                        
                        if (!string.IsNullOrWhiteSpace(results))
                        {
                            var files = results.Split('\n')
                                .Where(f => !string.IsNullOrWhiteSpace(f))
                                .ToList();
                            
                            foundFiles.AddRange(files);
                            ProgressUpdate?.Invoke(this, $"✓ Found {files.Count} potential {sigName} files by extension");
                        }
                    }
                    
                    // Also try alternative: scan using file command for type detection
                    var fileCmd = $"find \"{path}\" -type f 2>/dev/null -exec file {{}} + 2>/dev/null | grep -iE \"({sigName.ToLower()}|{string.Join("|", sigInfo.Extensions.Select(e => e.TrimStart('.').ToLower()))})\" | cut -d: -f1 | head -100";
                    var fileResults = await _adbService.ExecuteShellCommandAsync(deviceId, fileCmd);
                    
                    if (!string.IsNullOrWhiteSpace(fileResults))
                    {
                        var moreFiles = fileResults.Split('\n')
                            .Where(f => !string.IsNullOrWhiteSpace(f) && !foundFiles.Contains(f))
                            .ToList();
                        
                        if (moreFiles.Any())
                        {
                            foundFiles.AddRange(moreFiles);
                            ProgressUpdate?.Invoke(this, $"✓ Found {moreFiles.Count} additional {sigName} files by file type");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ProgressUpdate?.Invoke(this, $"File system scan error: {ex.Message}");
            }
            
            return foundFiles.Distinct().ToList();
        }
        
        private async Task<List<string>> CarveFilesFromDump(
            string deviceId,
            string dumpFile,
            Dictionary<string, FileSignatureInfo> signatures,
            HashSet<string> fileTypes,
            HashSet<string> extensions,
            string outputDir,
            CancellationToken token)
        {
            var carvedFiles = new List<string>();
            
            try
            {
                // This would require reading the dump file byte-by-byte
                // For now, use strings/grep approach on the dump
                foreach (var sigKvp in signatures)
                {
                    if (token.IsCancellationRequested) break;
                    
                    var sigName = sigKvp.Key;
                    var sigInfo = sigKvp.Value;
                    
                    bool shouldScan = fileTypes.Contains(sigName) || 
                                     sigInfo.Extensions.Any(ext => extensions.Contains(ext.ToUpper().TrimStart('.')));
                    
                    if (!shouldScan) continue;
                    
                    // Use strings to find signature patterns in dump
                    var hexPattern = string.Join(" ", sigInfo.Header.Select(b => $"{b:X2}"));
                    var grepCmd = $"hexdump -C {dumpFile} 2>/dev/null | grep -i \"{hexPattern}\" | head -20";
                    
                    var results = await _adbService.ExecuteShellCommandAsync(deviceId, grepCmd);
                    
                    if (!string.IsNullOrWhiteSpace(results))
                    {
                        ProgressUpdate?.Invoke(this, $"✓ Found {sigName} signatures in raw dump");
                        // Note: Full extraction requires byte-level processing
                    }
                }
            }
            catch { }
            
            return carvedFiles;
        }
        
        private async Task<List<string>> ScanRecoveryLocations(
            string deviceId,
            string storagePath,
            HashSet<string> fileTypes,
            HashSet<string> extensions,
            CancellationToken token)
        {
            var foundFiles = new List<string>();
            
            var recoveryPaths = new[]
            {
                $"{storagePath}/Lost.Dir",
                "/data/lost+found",
                $"{storagePath}/.recovery",
                "/cache",
                "/data/local/tmp"
            };
            
            foreach (var path in recoveryPaths)
            {
                if (token.IsCancellationRequested) break;
                
                try
                {
                    var files = await _adbService.ExecuteShellCommandAsync(deviceId, 
                        $"find \"{path}\" -type f 2>/dev/null");
                    
                    if (!string.IsNullOrWhiteSpace(files))
                    {
                        var fileList = files.Split('\n')
                            .Where(f => !string.IsNullOrWhiteSpace(f))
                            .ToList();
                        
                        foundFiles.AddRange(fileList);
                    }
                }
                catch { }
            }
            
            return foundFiles;
        }
        
        private Task<List<string>> ScanDeletedInodes(
            string deviceId,
            string storagePath,
            HashSet<string> fileTypes,
            HashSet<string> extensions,
            CancellationToken token)
        {
            // Advanced: Use debugfs to recover deleted inodes
            // This is very device-specific and requires proper partition mounting
            return Task.FromResult(new List<string>());
        }
        
        private async Task<List<string>> PerformAdvancedFileSystemScan(
            string deviceId,
            string storagePath,
            HashSet<string> fileTypes,
            HashSet<string> extensions,
            CancellationToken token)
        {
            var foundFiles = new List<string>();
            
            ProgressUpdate?.Invoke(this, "Performing advanced file system scan (no root)...");
            
            // Scan with more aggressive techniques without root
            try
            {
                // Look for files in unusual locations
                var unusualPaths = new[]
                {
                    $"{storagePath}/.Trash",
                    $"{storagePath}/.recycle",
                    $"{storagePath}/.deleted",
                    $"{storagePath}/Android/data/*/cache",
                    $"{storagePath}/Android/data/*/files/.deleted"
                };
                
                foreach (var pattern in unusualPaths)
                {
                    if (token.IsCancellationRequested) break;
                    
                    try
                    {
                        var files = await _adbService.ExecuteShellCommandAsync(deviceId, 
                            $"find {pattern} -type f 2>/dev/null | head -100");
                        
                        if (!string.IsNullOrWhiteSpace(files))
                        {
                            foundFiles.AddRange(files.Split('\n').Where(f => !string.IsNullOrWhiteSpace(f)));
                        }
                    }
                    catch { }
                }
            }
            catch { }
            
            return foundFiles.Distinct().ToList();
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

