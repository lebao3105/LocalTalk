using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Shared.Platform;

namespace Shared.FileSystem
{
    /// <summary>
    /// File system monitoring and change detection with file lock handling
    /// </summary>
    public class FileSystemMonitor
    {
        private static FileSystemMonitor _instance;
        private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers;
        private readonly ConcurrentDictionary<string, MonitoredPath> _monitoredPaths;
        private readonly ConcurrentDictionary<string, FileLockInfo> _lockedFiles;
        private readonly Timer _lockCheckTimer;
        private readonly MonitorConfiguration _config;

        public static FileSystemMonitor Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new FileSystemMonitor();
                }
                return _instance;
            }
        }

        public event EventHandler<FileChangedEventArgs> FileChanged;
        public event EventHandler<FileCreatedEventArgs> FileCreated;
        public event EventHandler<FileDeletedEventArgs> FileDeleted;
        public event EventHandler<FileRenamedEventArgs> FileRenamed;
        public event EventHandler<FileLockEventArgs> FileLocked;
        public event EventHandler<FileLockEventArgs> FileUnlocked;

        private FileSystemMonitor()
        {
            _watchers = new ConcurrentDictionary<string, FileSystemWatcher>();
            _monitoredPaths = new ConcurrentDictionary<string, MonitoredPath>();
            _lockedFiles = new ConcurrentDictionary<string, FileLockInfo>();
            _config = new MonitorConfiguration();

            // Start file lock monitoring
            _lockCheckTimer = new Timer(CheckFileLocks, null,
                _config.LockCheckInterval, _config.LockCheckInterval);
        }

        /// <summary>
        /// Starts monitoring a directory for file changes
        /// </summary>
        public async Task<string> StartMonitoringAsync(string path, MonitoringOptions options = null)
        {
            var monitorId = Guid.NewGuid().ToString();
            options = options ?? new MonitoringOptions();

            try
            {
                if (!Directory.Exists(path))
                {
                    throw new DirectoryNotFoundException($"Directory not found: {path}");
                }

                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = options.IncludeSubdirectories,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                  NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                // Apply file filters
                if (options.FileFilters?.Any() == true)
                {
                    watcher.Filter = string.Join("|", options.FileFilters);
                }

                // Subscribe to events
                watcher.Changed += OnFileChanged;
                watcher.Created += OnFileCreated;
                watcher.Deleted += OnFileDeleted;
                watcher.Renamed += OnFileRenamed;
                watcher.Error += OnWatcherError;

                var monitoredPath = new MonitoredPath
                {
                    MonitorId = monitorId,
                    Path = path,
                    Options = options,
                    StartedAt = DateTime.Now,
                    LastActivity = DateTime.Now,
                    EventCount = 0
                };

                _watchers[monitorId] = watcher;
                _monitoredPaths[monitorId] = monitoredPath;

                System.Diagnostics.Debug.WriteLine($"Started monitoring {path} with ID {monitorId}");
                return monitorId;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting file system monitoring for {path}: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Stops monitoring a directory
        /// </summary>
        public async Task<bool> StopMonitoringAsync(string monitorId)
        {
            try
            {
                if (_watchers.TryRemove(monitorId, out var watcher))
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }

                if (_monitoredPaths.TryRemove(monitorId, out var monitoredPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Stopped monitoring {monitoredPath.Path} with ID {monitorId}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping file system monitoring {monitorId}: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Checks if a file is currently locked
        /// </summary>
        public async Task<bool> IsFileLockedAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        return false; // File is not locked
                    }
                }
                catch (IOException)
                {
                    return true; // File is locked
                }
                catch (UnauthorizedAccessException)
                {
                    return true; // File is locked or access denied
                }
                catch (Exception)
                {
                    return false; // Other errors, assume not locked
                }
            });
        }

        /// <summary>
        /// Waits for a file to become unlocked
        /// </summary>
        public async Task<bool> WaitForFileUnlockAsync(string filePath, TimeSpan timeout = default)
        {
            if (timeout == default)
                timeout = _config.DefaultUnlockTimeout;

            var startTime = DateTime.Now;

            while (DateTime.Now - startTime < timeout)
            {
                if (!await IsFileLockedAsync(filePath))
                {
                    return true;
                }

                await Task.Delay(_config.LockCheckInterval);
            }

            return false; // Timeout reached
        }

        /// <summary>
        /// Gets information about monitored paths
        /// </summary>
        public List<MonitoringInfo> GetMonitoringInfo()
        {
            return _monitoredPaths.Values
                .Select(mp => new MonitoringInfo
                {
                    MonitorId = mp.MonitorId,
                    Path = mp.Path,
                    StartedAt = mp.StartedAt,
                    LastActivity = mp.LastActivity,
                    EventCount = mp.EventCount,
                    IsActive = _watchers.ContainsKey(mp.MonitorId),
                    Options = mp.Options
                })
                .ToList();
        }

        /// <summary>
        /// Gets information about currently locked files
        /// </summary>
        public List<FileLockInfo> GetLockedFiles()
        {
            return _lockedFiles.Values.ToList();
        }

        /// <summary>
        /// Handles file changed events
        /// </summary>
        private async void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                var watcher = sender as FileSystemWatcher;
                var monitorId = GetMonitorIdForWatcher(watcher);

                if (monitorId != null && _monitoredPaths.TryGetValue(monitorId, out var monitoredPath))
                {
                    monitoredPath.LastActivity = DateTime.Now;
                    monitoredPath.EventCount++;

                    // Check if file is locked
                    var isLocked = await IsFileLockedAsync(e.FullPath);

                    FileChanged?.Invoke(this, new FileChangedEventArgs
                    {
                        MonitorId = monitorId,
                        FilePath = e.FullPath,
                        FileName = e.Name,
                        ChangeType = e.ChangeType,
                        IsLocked = isLocked,
                        Timestamp = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling file changed event: {ex}");
            }
        }

        /// <summary>
        /// Handles file created events
        /// </summary>
        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            try
            {
                var watcher = sender as FileSystemWatcher;
                var monitorId = GetMonitorIdForWatcher(watcher);

                if (monitorId != null && _monitoredPaths.TryGetValue(monitorId, out var monitoredPath))
                {
                    monitoredPath.LastActivity = DateTime.Now;
                    monitoredPath.EventCount++;

                    FileCreated?.Invoke(this, new FileCreatedEventArgs
                    {
                        MonitorId = monitorId,
                        FilePath = e.FullPath,
                        FileName = e.Name,
                        Timestamp = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling file created event: {ex}");
            }
        }

        /// <summary>
        /// Handles file deleted events
        /// </summary>
        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            try
            {
                var watcher = sender as FileSystemWatcher;
                var monitorId = GetMonitorIdForWatcher(watcher);

                if (monitorId != null && _monitoredPaths.TryGetValue(monitorId, out var monitoredPath))
                {
                    monitoredPath.LastActivity = DateTime.Now;
                    monitoredPath.EventCount++;

                    // Remove from locked files if it was locked
                    _lockedFiles.TryRemove(e.FullPath, out _);

                    FileDeleted?.Invoke(this, new FileDeletedEventArgs
                    {
                        MonitorId = monitorId,
                        FilePath = e.FullPath,
                        FileName = e.Name,
                        Timestamp = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling file deleted event: {ex}");
            }
        }

        /// <summary>
        /// Handles file renamed events
        /// </summary>
        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            try
            {
                var watcher = sender as FileSystemWatcher;
                var monitorId = GetMonitorIdForWatcher(watcher);

                if (monitorId != null && _monitoredPaths.TryGetValue(monitorId, out var monitoredPath))
                {
                    monitoredPath.LastActivity = DateTime.Now;
                    monitoredPath.EventCount++;

                    FileRenamed?.Invoke(this, new FileRenamedEventArgs
                    {
                        MonitorId = monitorId,
                        OldFilePath = e.OldFullPath,
                        NewFilePath = e.FullPath,
                        OldFileName = e.OldName,
                        NewFileName = e.Name,
                        Timestamp = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling file renamed event: {ex}");
            }
        }

        /// <summary>
        /// Handles watcher errors
        /// </summary>
        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"File system watcher error: {e.GetException()}");
        }

        /// <summary>
        /// Gets monitor ID for a watcher
        /// </summary>
        private string GetMonitorIdForWatcher(FileSystemWatcher watcher)
        {
            return _watchers.FirstOrDefault(kvp => kvp.Value == watcher).Key;
        }

        /// <summary>
        /// Periodically checks for file locks
        /// </summary>
        private async void CheckFileLocks(object state)
        {
            try
            {
                var filesToCheck = new List<string>();

                // Collect files from monitored paths
                foreach (var monitoredPath in _monitoredPaths.Values)
                {
                    if (Directory.Exists(monitoredPath.Path))
                    {
                        var files = Directory.GetFiles(monitoredPath.Path, "*",
                            monitoredPath.Options.IncludeSubdirectories ?
                            SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                        filesToCheck.AddRange(files);
                    }
                }

                // Check lock status for each file
                foreach (var filePath in filesToCheck.Take(_config.MaxFilesToCheckPerCycle))
                {
                    var isCurrentlyLocked = await IsFileLockedAsync(filePath);
                    var wasLocked = _lockedFiles.ContainsKey(filePath);

                    if (isCurrentlyLocked && !wasLocked)
                    {
                        // File became locked
                        var lockInfo = new FileLockInfo
                        {
                            FilePath = filePath,
                            LockedAt = DateTime.Now,
                            LastChecked = DateTime.Now
                        };

                        _lockedFiles[filePath] = lockInfo;

                        FileLocked?.Invoke(this, new FileLockEventArgs
                        {
                            FilePath = filePath,
                            LockInfo = lockInfo,
                            Timestamp = DateTime.Now
                        });
                    }
                    else if (!isCurrentlyLocked && wasLocked)
                    {
                        // File became unlocked
                        if (_lockedFiles.TryRemove(filePath, out var lockInfo))
                        {
                            FileUnlocked?.Invoke(this, new FileLockEventArgs
                            {
                                FilePath = filePath,
                                LockInfo = lockInfo,
                                Timestamp = DateTime.Now
                            });
                        }
                    }
                    else if (wasLocked)
                    {
                        // Update last checked time
                        _lockedFiles[filePath].LastChecked = DateTime.Now;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking file locks: {ex}");
            }
        }

        /// <summary>
        /// Disposes the file system monitor
        /// </summary>
        public void Dispose()
        {
            _lockCheckTimer?.Dispose();

            foreach (var watcher in _watchers.Values)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            _watchers.Clear();
            _monitoredPaths.Clear();
            _lockedFiles.Clear();
        }
    }

    /// <summary>
    /// Monitoring configuration
    /// </summary>
    public class MonitorConfiguration
    {
        #region Default Configuration Constants
        /// <summary>
        /// Default interval for checking file locks in seconds.
        /// </summary>
        private const int DefaultLockCheckIntervalSeconds = 5;

        /// <summary>
        /// Default timeout for waiting for file unlock in minutes.
        /// </summary>
        private const int DefaultUnlockTimeoutMinutes = 5;

        /// <summary>
        /// Default maximum number of files to check per monitoring cycle.
        /// </summary>
        private const int DefaultMaxFilesToCheckPerCycle = 100;
        #endregion

        public TimeSpan LockCheckInterval { get; set; } = TimeSpan.FromSeconds(DefaultLockCheckIntervalSeconds);
        public TimeSpan DefaultUnlockTimeout { get; set; } = TimeSpan.FromMinutes(DefaultUnlockTimeoutMinutes);
        public int MaxFilesToCheckPerCycle { get; set; } = DefaultMaxFilesToCheckPerCycle;
        public bool EnableLockMonitoring { get; set; } = true;
        public bool EnableChangeDetection { get; set; } = true;
    }

    /// <summary>
    /// Monitoring options
    /// </summary>
    public class MonitoringOptions
    {
        public bool IncludeSubdirectories { get; set; } = true;
        public List<string> FileFilters { get; set; } = new List<string>();
        public bool MonitorLocks { get; set; } = true;
        public bool MonitorChanges { get; set; } = true;
        public bool MonitorCreation { get; set; } = true;
        public bool MonitorDeletion { get; set; } = true;
        public bool MonitorRenames { get; set; } = true;
    }

    /// <summary>
    /// Monitored path information
    /// </summary>
    internal class MonitoredPath
    {
        public string MonitorId { get; set; }
        public string Path { get; set; }
        public MonitoringOptions Options { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime LastActivity { get; set; }
        public int EventCount { get; set; }
    }

    /// <summary>
    /// Monitoring information for external consumption
    /// </summary>
    public class MonitoringInfo
    {
        public string MonitorId { get; set; }
        public string Path { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime LastActivity { get; set; }
        public int EventCount { get; set; }
        public bool IsActive { get; set; }
        public MonitoringOptions Options { get; set; }
        public TimeSpan Duration => DateTime.Now - StartedAt;
    }

    /// <summary>
    /// File lock information
    /// </summary>
    public class FileLockInfo
    {
        public string FilePath { get; set; }
        public DateTime LockedAt { get; set; }
        public DateTime LastChecked { get; set; }
        public TimeSpan LockDuration => DateTime.Now - LockedAt;
    }

    /// <summary>
    /// Base file system event arguments
    /// </summary>
    public abstract class FileSystemEventArgsBase : EventArgs
    {
        public string MonitorId { get; set; }
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// File changed event arguments
    /// </summary>
    public class FileChangedEventArgs : FileSystemEventArgsBase
    {
        public WatcherChangeTypes ChangeType { get; set; }
        public bool IsLocked { get; set; }
    }

    /// <summary>
    /// File created event arguments
    /// </summary>
    public class FileCreatedEventArgs : FileSystemEventArgsBase
    {
    }

    /// <summary>
    /// File deleted event arguments
    /// </summary>
    public class FileDeletedEventArgs : FileSystemEventArgsBase
    {
    }

    /// <summary>
    /// File renamed event arguments
    /// </summary>
    public class FileRenamedEventArgs : FileSystemEventArgsBase
    {
        public string OldFilePath { get; set; }
        public string NewFilePath { get; set; }
        public string OldFileName { get; set; }
        public string NewFileName { get; set; }
    }

    /// <summary>
    /// File lock event arguments
    /// </summary>
    public class FileLockEventArgs : EventArgs
    {
        public string FilePath { get; set; }
        public FileLockInfo LockInfo { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
