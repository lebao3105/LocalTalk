using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Shared.Platform;

namespace Shared.FileSystem
{
    /// <summary>
    /// Storage space analysis, quota management, and intelligent cleanup system
    /// </summary>
    public class StorageManager
    {
        private static StorageManager _instance;
        private readonly Timer _cleanupTimer;
        private readonly StorageConfiguration _config;
        private readonly object _lock = new object();

        public static StorageManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new StorageManager();
                }
                return _instance;
            }
        }

        public event EventHandler<StorageQuotaEventArgs> QuotaExceeded;
        public event EventHandler<StorageCleanupEventArgs> CleanupCompleted;
        public event EventHandler<LowStorageEventArgs> LowStorageWarning;

        private StorageManager()
        {
            _config = new StorageConfiguration();
            
            // Start periodic cleanup
            _cleanupTimer = new Timer(PerformPeriodicCleanup, null, 
                _config.CleanupInterval, _config.CleanupInterval);
        }

        /// <summary>
        /// Analyzes storage space for a given path
        /// </summary>
        public async Task<StorageAnalysis> AnalyzeStorageAsync(string path)
        {
            var analysis = new StorageAnalysis
            {
                Path = path,
                AnalyzedAt = DateTime.Now
            };

            try
            {
                var driveInfo = new DriveInfo(Path.GetPathRoot(path));
                
                analysis.TotalSpace = driveInfo.TotalSize;
                analysis.FreeSpace = driveInfo.AvailableFreeSpace;
                analysis.UsedSpace = analysis.TotalSpace - analysis.FreeSpace;
                analysis.UsagePercentage = (double)analysis.UsedSpace / analysis.TotalSpace * 100;

                // Analyze directory structure if it exists
                if (Directory.Exists(path))
                {
                    var directoryAnalysis = await AnalyzeDirectoryAsync(path);
                    analysis.DirectorySize = directoryAnalysis.TotalSize;
                    analysis.FileCount = directoryAnalysis.FileCount;
                    analysis.DirectoryCount = directoryAnalysis.DirectoryCount;
                    analysis.LargestFiles = directoryAnalysis.LargestFiles;
                    analysis.FileTypeBreakdown = directoryAnalysis.FileTypeBreakdown;
                }

                // Determine storage status
                analysis.Status = DetermineStorageStatus(analysis);
                
                System.Diagnostics.Debug.WriteLine($"Storage analysis completed for {path}: {analysis.UsagePercentage:F1}% used");
            }
            catch (Exception ex)
            {
                analysis.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Storage analysis error for {path}: {ex}");
            }

            return analysis;
        }

        /// <summary>
        /// Checks if there's enough space for a file operation
        /// </summary>
        public async Task<SpaceCheckResult> CheckAvailableSpaceAsync(string path, long requiredBytes)
        {
            var result = new SpaceCheckResult
            {
                Path = path,
                RequiredBytes = requiredBytes,
                CheckedAt = DateTime.Now
            };

            try
            {
                var driveInfo = new DriveInfo(Path.GetPathRoot(path));
                result.AvailableBytes = driveInfo.AvailableFreeSpace;
                result.HasSufficientSpace = result.AvailableBytes >= requiredBytes;
                
                if (!result.HasSufficientSpace)
                {
                    result.ShortfallBytes = requiredBytes - result.AvailableBytes;
                    
                    // Check if cleanup could free enough space
                    var cleanupEstimate = await EstimateCleanupSpaceAsync(path);
                    result.CanBeFreedByCleanup = cleanupEstimate >= result.ShortfallBytes;
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Space check error for {path}: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Performs intelligent cleanup to free storage space
        /// </summary>
        public async Task<CleanupResult> PerformCleanupAsync(string path, CleanupOptions options = null)
        {
            options = options ?? new CleanupOptions();
            var result = new CleanupResult
            {
                Path = path,
                StartedAt = DateTime.Now,
                Options = options
            };

            try
            {
                var initialAnalysis = await AnalyzeStorageAsync(path);
                result.InitialFreeSpace = initialAnalysis.FreeSpace;

                var cleanedFiles = new List<CleanedFileInfo>();

                // Clean temporary files
                if (options.CleanTemporaryFiles)
                {
                    var tempFiles = await CleanTemporaryFilesAsync(path);
                    cleanedFiles.AddRange(tempFiles);
                }

                // Clean old cache files
                if (options.CleanCacheFiles)
                {
                    var cacheFiles = await CleanCacheFilesAsync(path);
                    cleanedFiles.AddRange(cacheFiles);
                }

                // Clean old log files
                if (options.CleanLogFiles)
                {
                    var logFiles = await CleanLogFilesAsync(path);
                    cleanedFiles.AddRange(logFiles);
                }

                // Clean duplicate files
                if (options.CleanDuplicateFiles)
                {
                    var duplicateFiles = await CleanDuplicateFilesAsync(path);
                    cleanedFiles.AddRange(duplicateFiles);
                }

                // Clean large old files
                if (options.CleanLargeOldFiles)
                {
                    var largeOldFiles = await CleanLargeOldFilesAsync(path, options.LargeFileThreshold, options.OldFileThreshold);
                    cleanedFiles.AddRange(largeOldFiles);
                }

                result.CleanedFiles = cleanedFiles;
                result.FilesDeleted = cleanedFiles.Count;
                result.SpaceFreed = cleanedFiles.Sum(f => f.Size);

                var finalAnalysis = await AnalyzeStorageAsync(path);
                result.FinalFreeSpace = finalAnalysis.FreeSpace;
                result.CompletedAt = DateTime.Now;
                result.Duration = result.CompletedAt - result.StartedAt;
                result.Success = true;

                OnCleanupCompleted(new StorageCleanupEventArgs
                {
                    Path = path,
                    SpaceFreed = result.SpaceFreed,
                    FilesDeleted = result.FilesDeleted,
                    Duration = result.Duration
                });

                System.Diagnostics.Debug.WriteLine($"Cleanup completed for {path}: {result.SpaceFreed} bytes freed, {result.FilesDeleted} files deleted");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Cleanup error for {path}: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Sets storage quota for a path
        /// </summary>
        public async Task<bool> SetQuotaAsync(string path, long quotaBytes)
        {
            try
            {
                // This is a simplified implementation
                // In a real implementation, you would use platform-specific quota APIs
                _config.PathQuotas[path] = quotaBytes;
                
                // Check current usage against quota
                var analysis = await AnalyzeStorageAsync(path);
                if (analysis.DirectorySize > quotaBytes)
                {
                    OnQuotaExceeded(new StorageQuotaEventArgs
                    {
                        Path = path,
                        QuotaBytes = quotaBytes,
                        UsedBytes = analysis.DirectorySize,
                        ExcessBytes = analysis.DirectorySize - quotaBytes
                    });
                }

                System.Diagnostics.Debug.WriteLine($"Set quota for {path}: {quotaBytes} bytes");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting quota for {path}: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Gets storage statistics for all monitored paths
        /// </summary>
        public async Task<StorageStatistics> GetStorageStatisticsAsync()
        {
            var stats = new StorageStatistics
            {
                GeneratedAt = DateTime.Now
            };

            try
            {
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady);
                
                foreach (var drive in drives)
                {
                    var driveStats = new DriveStatistics
                    {
                        Name = drive.Name,
                        DriveType = drive.DriveType.ToString(),
                        TotalSize = drive.TotalSize,
                        FreeSpace = drive.AvailableFreeSpace,
                        UsedSpace = drive.TotalSize - drive.AvailableFreeSpace,
                        UsagePercentage = (double)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize * 100
                    };
                    
                    stats.DriveStatistics.Add(driveStats);
                }

                stats.TotalSpace = stats.DriveStatistics.Sum(d => d.TotalSize);
                stats.TotalFreeSpace = stats.DriveStatistics.Sum(d => d.FreeSpace);
                stats.TotalUsedSpace = stats.DriveStatistics.Sum(d => d.UsedSpace);
                stats.OverallUsagePercentage = (double)stats.TotalUsedSpace / stats.TotalSpace * 100;
            }
            catch (Exception ex)
            {
                stats.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Error getting storage statistics: {ex}");
            }

            return stats;
        }

        /// <summary>
        /// Analyzes a directory structure
        /// </summary>
        private async Task<DirectoryAnalysis> AnalyzeDirectoryAsync(string path)
        {
            return await Task.Run(() =>
            {
                var analysis = new DirectoryAnalysis();
                var fileTypeBreakdown = new Dictionary<string, FileTypeInfo>();

                try
                {
                    var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                    var directories = Directory.GetDirectories(path, "*", SearchOption.AllDirectories);

                    analysis.FileCount = files.Length;
                    analysis.DirectoryCount = directories.Length;

                    var largestFiles = new List<FileInfo>();

                    foreach (var filePath in files)
                    {
                        try
                        {
                            var fileInfo = new FileInfo(filePath);
                            analysis.TotalSize += fileInfo.Length;

                            // Track largest files
                            largestFiles.Add(fileInfo);
                            if (largestFiles.Count > 10)
                            {
                                largestFiles = largestFiles.OrderByDescending(f => f.Length).Take(10).ToList();
                            }

                            // Track file type breakdown
                            var extension = fileInfo.Extension.ToLowerInvariant();
                            if (string.IsNullOrEmpty(extension))
                                extension = "(no extension)";

                            if (!fileTypeBreakdown.ContainsKey(extension))
                            {
                                fileTypeBreakdown[extension] = new FileTypeInfo
                                {
                                    Extension = extension,
                                    Count = 0,
                                    TotalSize = 0
                                };
                            }

                            fileTypeBreakdown[extension].Count++;
                            fileTypeBreakdown[extension].TotalSize += fileInfo.Length;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error analyzing file {filePath}: {ex.Message}");
                        }
                    }

                    analysis.LargestFiles = largestFiles.OrderByDescending(f => f.Length).Take(10).ToList();
                    analysis.FileTypeBreakdown = fileTypeBreakdown.Values.OrderByDescending(ft => ft.TotalSize).ToList();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error analyzing directory {path}: {ex}");
                }

                return analysis;
            });
        }

        /// <summary>
        /// Determines storage status based on usage
        /// </summary>
        private StorageStatus DetermineStorageStatus(StorageAnalysis analysis)
        {
            if (analysis.UsagePercentage >= _config.CriticalThreshold)
                return StorageStatus.Critical;
            else if (analysis.UsagePercentage >= _config.WarningThreshold)
                return StorageStatus.Warning;
            else if (analysis.UsagePercentage >= _config.HealthyThreshold)
                return StorageStatus.Healthy;
            else
                return StorageStatus.Excellent;
        }

        /// <summary>
        /// Estimates how much space could be freed by cleanup
        /// </summary>
        private async Task<long> EstimateCleanupSpaceAsync(string path)
        {
            // This is a simplified estimation
            // In a real implementation, you would scan for cleanable files
            return await Task.FromResult(100 * 1024 * 1024); // 100MB estimate
        }

        /// <summary>
        /// Cleans temporary files
        /// </summary>
        private async Task<List<CleanedFileInfo>> CleanTemporaryFilesAsync(string path)
        {
            return await Task.Run(() =>
            {
                var cleanedFiles = new List<CleanedFileInfo>();
                
                try
                {
                    var tempPatterns = new[] { "*.tmp", "*.temp", "~*", "*.bak" };
                    
                    foreach (var pattern in tempPatterns)
                    {
                        var files = Directory.GetFiles(path, pattern, SearchOption.AllDirectories);
                        
                        foreach (var file in files)
                        {
                            try
                            {
                                var fileInfo = new FileInfo(file);
                                if (DateTime.Now - fileInfo.LastWriteTime > _config.TempFileAge)
                                {
                                    var size = fileInfo.Length;
                                    File.Delete(file);
                                    
                                    cleanedFiles.Add(new CleanedFileInfo
                                    {
                                        Path = file,
                                        Size = size,
                                        Type = CleanupType.TemporaryFile,
                                        DeletedAt = DateTime.Now
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error deleting temp file {file}: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error cleaning temporary files: {ex}");
                }
                
                return cleanedFiles;
            });
        }

        /// <summary>
        /// Cleans cache files
        /// </summary>
        private async Task<List<CleanedFileInfo>> CleanCacheFilesAsync(string path)
        {
            return await Task.Run(() =>
            {
                var cleanedFiles = new List<CleanedFileInfo>();
                
                try
                {
                    var cacheDirectories = new[] { "cache", "Cache", "temp", "Temp" };
                    
                    foreach (var cacheDir in cacheDirectories)
                    {
                        var cachePath = Path.Combine(path, cacheDir);
                        if (Directory.Exists(cachePath))
                        {
                            var files = Directory.GetFiles(cachePath, "*", SearchOption.AllDirectories);
                            
                            foreach (var file in files)
                            {
                                try
                                {
                                    var fileInfo = new FileInfo(file);
                                    if (DateTime.Now - fileInfo.LastAccessTime > _config.CacheFileAge)
                                    {
                                        var size = fileInfo.Length;
                                        File.Delete(file);
                                        
                                        cleanedFiles.Add(new CleanedFileInfo
                                        {
                                            Path = file,
                                            Size = size,
                                            Type = CleanupType.CacheFile,
                                            DeletedAt = DateTime.Now
                                        });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error deleting cache file {file}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error cleaning cache files: {ex}");
                }
                
                return cleanedFiles;
            });
        }

        /// <summary>
        /// Cleans log files
        /// </summary>
        private async Task<List<CleanedFileInfo>> CleanLogFilesAsync(string path)
        {
            return await Task.Run(() =>
            {
                var cleanedFiles = new List<CleanedFileInfo>();
                
                try
                {
                    var logPatterns = new[] { "*.log", "*.log.*", "*.txt" };
                    
                    foreach (var pattern in logPatterns)
                    {
                        var files = Directory.GetFiles(path, pattern, SearchOption.AllDirectories);
                        
                        foreach (var file in files.Where(f => f.ToLowerInvariant().Contains("log")))
                        {
                            try
                            {
                                var fileInfo = new FileInfo(file);
                                if (DateTime.Now - fileInfo.LastWriteTime > _config.LogFileAge)
                                {
                                    var size = fileInfo.Length;
                                    File.Delete(file);
                                    
                                    cleanedFiles.Add(new CleanedFileInfo
                                    {
                                        Path = file,
                                        Size = size,
                                        Type = CleanupType.LogFile,
                                        DeletedAt = DateTime.Now
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error deleting log file {file}: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error cleaning log files: {ex}");
                }
                
                return cleanedFiles;
            });
        }

        /// <summary>
        /// Cleans duplicate files
        /// </summary>
        private async Task<List<CleanedFileInfo>> CleanDuplicateFilesAsync(string path)
        {
            // This is a simplified implementation
            // In a real implementation, you would use file hashing to find duplicates
            return await Task.FromResult(new List<CleanedFileInfo>());
        }

        /// <summary>
        /// Cleans large old files
        /// </summary>
        private async Task<List<CleanedFileInfo>> CleanLargeOldFilesAsync(string path, long sizeThreshold, TimeSpan ageThreshold)
        {
            return await Task.Run(() =>
            {
                var cleanedFiles = new List<CleanedFileInfo>();
                
                try
                {
                    var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                    
                    foreach (var file in files)
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            if (fileInfo.Length > sizeThreshold && 
                                DateTime.Now - fileInfo.LastAccessTime > ageThreshold)
                            {
                                var size = fileInfo.Length;
                                File.Delete(file);
                                
                                cleanedFiles.Add(new CleanedFileInfo
                                {
                                    Path = file,
                                    Size = size,
                                    Type = CleanupType.LargeOldFile,
                                    DeletedAt = DateTime.Now
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error deleting large old file {file}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error cleaning large old files: {ex}");
                }
                
                return cleanedFiles;
            });
        }

        /// <summary>
        /// Performs periodic cleanup
        /// </summary>
        private async void PerformPeriodicCleanup(object state)
        {
            try
            {
                var stats = await GetStorageStatisticsAsync();
                
                // Check if any drive is running low on space
                foreach (var drive in stats.DriveStatistics)
                {
                    if (drive.UsagePercentage > _config.WarningThreshold)
                    {
                        OnLowStorageWarning(new LowStorageEventArgs
                        {
                            DriveName = drive.Name,
                            UsagePercentage = drive.UsagePercentage,
                            FreeSpace = drive.FreeSpace,
                            TotalSpace = drive.TotalSize
                        });
                        
                        // Perform automatic cleanup if enabled
                        if (_config.EnableAutomaticCleanup)
                        {
                            await PerformCleanupAsync(drive.Name, new CleanupOptions
                            {
                                CleanTemporaryFiles = true,
                                CleanCacheFiles = true,
                                CleanLogFiles = true
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Periodic cleanup error: {ex}");
            }
        }

        /// <summary>
        /// Raises the QuotaExceeded event
        /// </summary>
        private void OnQuotaExceeded(StorageQuotaEventArgs args)
        {
            QuotaExceeded?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the CleanupCompleted event
        /// </summary>
        private void OnCleanupCompleted(StorageCleanupEventArgs args)
        {
            CleanupCompleted?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the LowStorageWarning event
        /// </summary>
        private void OnLowStorageWarning(LowStorageEventArgs args)
        {
            LowStorageWarning?.Invoke(this, args);
        }

        /// <summary>
        /// Disposes the storage manager
        /// </summary>
        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }
    }
}
