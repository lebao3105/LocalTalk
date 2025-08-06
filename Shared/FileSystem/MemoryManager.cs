using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime;

namespace Shared.FileSystem
{
    /// <summary>
    /// Memory management system for handling memory pressure and optimization
    /// </summary>
    public class MemoryManager : IDisposable
    {
        private readonly object _lock = new object();
        private DateTime _lastMemoryCheck = DateTime.MinValue;
        private readonly TimeSpan _memoryCheckInterval = TimeSpan.FromSeconds(30);
        private MemoryUsageInfo _lastMemoryInfo;
        private CancellationTokenSource _monitoringCancellationSource;
        private Task _monitoringTask;
        private bool _disposed = false;

        public event EventHandler<MemoryPressureEventArgs> MemoryPressureDetected;

        /// <summary>
        /// Gets current memory usage information
        /// </summary>
        public MemoryUsageInfo GetMemoryUsage()
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                
                // Cache memory info to avoid frequent system calls
                if (_lastMemoryInfo == null || now - _lastMemoryCheck > _memoryCheckInterval)
                {
                    _lastMemoryInfo = CollectMemoryInfo();
                    _lastMemoryCheck = now;
                }
                
                return _lastMemoryInfo;
            }
        }

        /// <summary>
        /// Checks if the system is under memory pressure
        /// </summary>
        public bool IsUnderMemoryPressure()
        {
            var memoryInfo = GetMemoryUsage();
            
            // Consider memory pressure if:
            // 1. Available memory is less than 100MB
            // 2. Memory usage is above 80%
            var lowMemoryThreshold = 100 * 1024 * 1024; // 100MB
            var highUsageThreshold = 0.8; // 80%
            
            var isLowMemory = memoryInfo.AvailableMemory < lowMemoryThreshold;
            var isHighUsage = memoryInfo.MemoryUsagePercentage > highUsageThreshold;
            
            var underPressure = isLowMemory || isHighUsage;
            
            if (underPressure)
            {
                var pressureLevel = isLowMemory && isHighUsage ? MemoryPressureLevel.Critical :
                                  isLowMemory ? MemoryPressureLevel.High :
                                  MemoryPressureLevel.Medium;
                
                OnMemoryPressureDetected(new MemoryPressureEventArgs
                {
                    PressureLevel = pressureLevel,
                    AvailableMemory = memoryInfo.AvailableMemory,
                    UsagePercentage = memoryInfo.MemoryUsagePercentage,
                    Timestamp = DateTime.Now
                });
            }
            
            return underPressure;
        }

        /// <summary>
        /// Attempts to relieve memory pressure through garbage collection and optimization
        /// </summary>
        public async Task RelieveMemoryPressureAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("Attempting to relieve memory pressure...");
                    
                    // Force garbage collection
                    GC.Collect(2, GCCollectionMode.Forced, true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(2, GCCollectionMode.Forced, true);
                    
                    // Compact the large object heap if available
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect();
                    
                    System.Diagnostics.Debug.WriteLine("Memory pressure relief completed");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during memory pressure relief: {ex.Message}");
                }
            });
            
            // Update memory info after cleanup
            lock (_lock)
            {
                _lastMemoryInfo = null; // Force refresh on next call
            }
        }

        /// <summary>
        /// Estimates optimal buffer size based on available memory
        /// </summary>
        public int GetOptimalBufferSize(int requestedSize, int minSize = 4096, int maxSize = 1048576)
        {
            var memoryInfo = GetMemoryUsage();
            
            // Use a percentage of available memory for buffer sizing
            var availableForBuffer = (long)(memoryInfo.AvailableMemory * 0.05); // 5% of available memory
            
            var optimalSize = (int)Math.Min(availableForBuffer, requestedSize);
            optimalSize = Math.Max(optimalSize, minSize);
            optimalSize = Math.Min(optimalSize, maxSize);
            
            return optimalSize;
        }

        /// <summary>
        /// Starts memory monitoring with proper lifecycle management
        /// </summary>
        public async Task StartMemoryMonitoringAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MemoryManager));

            // Stop any existing monitoring
            await StopMemoryMonitoringAsync();

            // Create new cancellation source that combines external token with internal disposal
            _monitoringCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _monitoringTask = Task.Run(async () =>
            {
                var cancellationToken = _monitoringCancellationSource.Token;

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (IsUnderMemoryPressure())
                        {
                            await RelieveMemoryPressureAsync();
                        }

                        await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken); // Check every minute
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancellation is requested
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Memory monitoring error: {ex.Message}");
                        try
                        {
                            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken); // Wait longer on error
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            }, _monitoringCancellationSource.Token);
        }

        /// <summary>
        /// Stops memory monitoring gracefully
        /// </summary>
        public async Task StopMemoryMonitoringAsync()
        {
            if (_monitoringCancellationSource != null)
            {
                _monitoringCancellationSource.Cancel();

                if (_monitoringTask != null)
                {
                    try
                    {
                        await _monitoringTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancellation is requested
                    }
                }

                _monitoringCancellationSource.Dispose();
                _monitoringCancellationSource = null;
                _monitoringTask = null;
            }
        }

        /// <summary>
        /// Collects current memory information from the system
        /// </summary>
        private MemoryUsageInfo CollectMemoryInfo()
        {
            try
            {
                var totalMemory = GC.GetTotalMemory(false);
                
                // These values are approximations since .NET doesn't provide direct access to system memory info
                // In a real implementation, you would use platform-specific APIs
                var estimatedTotalSystemMemory = GetEstimatedTotalSystemMemory();
                var estimatedAvailableMemory = estimatedTotalSystemMemory - totalMemory;
                
                var usagePercentage = (double)totalMemory / estimatedTotalSystemMemory;
                
                return new MemoryUsageInfo
                {
                    TotalMemory = estimatedTotalSystemMemory,
                    UsedMemory = totalMemory,
                    AvailableMemory = estimatedAvailableMemory,
                    MemoryUsagePercentage = usagePercentage,
                    GCTotalMemory = totalMemory,
                    Gen0Collections = GC.CollectionCount(0),
                    Gen1Collections = GC.CollectionCount(1),
                    Gen2Collections = GC.CollectionCount(2),
                    Timestamp = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error collecting memory info: {ex.Message}");
                
                // Return default values on error
                return new MemoryUsageInfo
                {
                    TotalMemory = 1024 * 1024 * 1024, // 1GB default
                    UsedMemory = GC.GetTotalMemory(false),
                    AvailableMemory = 512 * 1024 * 1024, // 512MB default
                    MemoryUsagePercentage = 0.5,
                    GCTotalMemory = GC.GetTotalMemory(false),
                    Timestamp = DateTime.Now
                };
            }
        }

        /// <summary>
        /// Estimates total system memory (platform-specific implementation needed)
        /// </summary>
        private long GetEstimatedTotalSystemMemory()
        {
            // This is a simplified estimation
            // In a real implementation, you would use:
            // - Windows: GlobalMemoryStatusEx
            // - iOS: sysctl
            // - Android: ActivityManager.MemoryInfo
            
            // For now, return a reasonable default based on platform
#if WINDOWS_UWP
            return 4L * 1024 * 1024 * 1024; // 4GB for UWP
#elif WINDOWS_PHONE
            return 1L * 1024 * 1024 * 1024; // 1GB for Windows Phone
#else
            return 2L * 1024 * 1024 * 1024; // 2GB default
#endif
        }

        /// <summary>
        /// Raises the MemoryPressureDetected event
        /// </summary>
        private void OnMemoryPressureDetected(MemoryPressureEventArgs args)
        {
            MemoryPressureDetected?.Invoke(this, args);
        }

        /// <summary>
        /// Disposes the MemoryManager and stops monitoring
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected dispose method
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    // Stop monitoring gracefully
                    StopMemoryMonitoringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during MemoryManager disposal: {ex.Message}");
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// Finalizer
        /// </summary>
        ~MemoryManager()
        {
            Dispose(false);
        }
    }

    /// <summary>
    /// Memory usage information
    /// </summary>
    public class MemoryUsageInfo
    {
        public long TotalMemory { get; set; }
        public long UsedMemory { get; set; }
        public long AvailableMemory { get; set; }
        public double MemoryUsagePercentage { get; set; }
        public long GCTotalMemory { get; set; }
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }
        public DateTime Timestamp { get; set; }

        public string GetFormattedSummary()
        {
            return $"Memory Usage: {MemoryUsagePercentage:P1} ({FormatBytes(UsedMemory)} / {FormatBytes(TotalMemory)}), " +
                   $"Available: {FormatBytes(AvailableMemory)}, " +
                   $"GC: Gen0={Gen0Collections}, Gen1={Gen1Collections}, Gen2={Gen2Collections}";
        }

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            
            return $"{number:n1} {suffixes[counter]}";
        }
    }

    /// <summary>
    /// Memory pressure event arguments
    /// </summary>
    public class MemoryPressureEventArgs : EventArgs
    {
        public MemoryPressureLevel PressureLevel { get; set; }
        public long AvailableMemory { get; set; }
        public double UsagePercentage { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Memory pressure levels
    /// </summary>
    public enum MemoryPressureLevel
    {
        Low,
        Medium,
        High,
        Critical
    }
}
