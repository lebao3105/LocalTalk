using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shared.Platform;

namespace Shared.Protocol
{
    /// <summary>
    /// Adaptive bandwidth management and QoS control system
    /// </summary>
    public class BandwidthQoSManager
    {
        private static BandwidthQoSManager _instance;
        private readonly ConcurrentDictionary<string, TransferContext> _activeTransfers;
        private readonly NetworkMonitor _networkMonitor;
        private readonly QoSConfiguration _config;
        private readonly Timer _adaptationTimer;
        private readonly object _lock = new object();

        public static BandwidthQoSManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new BandwidthQoSManager();
                }
                return _instance;
            }
        }

        public event EventHandler<BandwidthThrottledEventArgs> BandwidthThrottled;
        public event EventHandler<QoSAdjustedEventArgs> QoSAdjusted;
        public event EventHandler<NetworkCongestionEventArgs> NetworkCongestionDetected;

        private BandwidthQoSManager()
        {
            _activeTransfers = new ConcurrentDictionary<string, TransferContext>();
            _networkMonitor = new NetworkMonitor();
            _config = new QoSConfiguration();
            
            // Start adaptive bandwidth management
            _adaptationTimer = new Timer(AdaptBandwidthAllocation, null, 
                _config.AdaptationInterval, _config.AdaptationInterval);
            
            // Monitor network conditions
            _networkMonitor.NetworkConditionChanged += OnNetworkConditionChanged;
        }

        /// <summary>
        /// Registers a transfer for bandwidth management
        /// </summary>
        public async Task<string> RegisterTransferAsync(TransferRegistration registration)
        {
            var transferId = Guid.NewGuid().ToString();
            var context = new TransferContext
            {
                TransferId = transferId,
                Priority = registration.Priority,
                MaxBandwidth = registration.MaxBandwidth,
                MinBandwidth = registration.MinBandwidth,
                TransferType = registration.TransferType,
                FileSize = registration.FileSize,
                RegisteredAt = DateTime.Now,
                LastActivity = DateTime.Now,
                AllocatedBandwidth = CalculateInitialBandwidth(registration)
            };

            _activeTransfers[transferId] = context;
            
            // Rebalance bandwidth allocation
            await RebalanceBandwidthAsync();
            
            System.Diagnostics.Debug.WriteLine($"Registered transfer {transferId} with priority {registration.Priority}");
            return transferId;
        }

        /// <summary>
        /// Unregisters a transfer from bandwidth management
        /// </summary>
        public async Task UnregisterTransferAsync(string transferId)
        {
            if (_activeTransfers.TryRemove(transferId, out var context))
            {
                // Rebalance bandwidth allocation
                await RebalanceBandwidthAsync();
                System.Diagnostics.Debug.WriteLine($"Unregistered transfer {transferId}");
            }
        }

        /// <summary>
        /// Requests bandwidth allocation for data transfer
        /// </summary>
        public async Task<BandwidthAllocation> RequestBandwidthAsync(string transferId, int requestedBytes)
        {
            if (!_activeTransfers.TryGetValue(transferId, out var context))
            {
                return new BandwidthAllocation
                {
                    Granted = false,
                    ErrorMessage = "Transfer not registered"
                };
            }

            var allocation = new BandwidthAllocation
            {
                TransferId = transferId,
                RequestedBytes = requestedBytes,
                RequestedAt = DateTime.Now
            };

            try
            {
                // Check network conditions
                var networkCondition = await _networkMonitor.GetCurrentConditionAsync();
                
                // Calculate allowed bytes based on allocated bandwidth
                var allowedBytes = CalculateAllowedBytes(context, requestedBytes, networkCondition);
                
                // Apply throttling if necessary
                var throttleDelay = CalculateThrottleDelay(context, allowedBytes, requestedBytes);
                
                allocation.GrantedBytes = allowedBytes;
                allocation.ThrottleDelay = throttleDelay;
                allocation.Granted = true;
                
                // Update transfer statistics
                context.LastActivity = DateTime.Now;
                context.TotalBytesTransferred += allowedBytes;
                context.CurrentThroughput = CalculateThroughput(context);
                
                // Apply throttling if needed
                if (throttleDelay > TimeSpan.Zero)
                {
                    OnBandwidthThrottled(new BandwidthThrottledEventArgs
                    {
                        TransferId = transferId,
                        RequestedBytes = requestedBytes,
                        GrantedBytes = allowedBytes,
                        ThrottleDelay = throttleDelay,
                        Reason = "Bandwidth limit exceeded"
                    });
                    
                    await Task.Delay(throttleDelay);
                }
            }
            catch (Exception ex)
            {
                allocation.Granted = false;
                allocation.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Bandwidth allocation error for {transferId}: {ex}");
            }

            return allocation;
        }

        /// <summary>
        /// Gets current bandwidth statistics
        /// </summary>
        public BandwidthStatistics GetBandwidthStatistics()
        {
            var stats = new BandwidthStatistics
            {
                ActiveTransfers = _activeTransfers.Count,
                TotalAllocatedBandwidth = _activeTransfers.Values.Sum(c => c.AllocatedBandwidth),
                NetworkCondition = _networkMonitor.GetCurrentConditionAsync().Result,
                GeneratedAt = DateTime.Now
            };

            foreach (var context in _activeTransfers.Values)
            {
                stats.TransferStatistics.Add(new TransferStatistics
                {
                    TransferId = context.TransferId,
                    Priority = context.Priority,
                    AllocatedBandwidth = context.AllocatedBandwidth,
                    CurrentThroughput = context.CurrentThroughput,
                    TotalBytesTransferred = context.TotalBytesTransferred,
                    Duration = DateTime.Now - context.RegisteredAt
                });
            }

            return stats;
        }

        /// <summary>
        /// Adjusts QoS settings for a transfer
        /// </summary>
        public async Task<bool> AdjustQoSAsync(string transferId, QoSAdjustment adjustment)
        {
            if (!_activeTransfers.TryGetValue(transferId, out var context))
                return false;

            try
            {
                var oldPriority = context.Priority;
                var oldBandwidth = context.AllocatedBandwidth;

                // Apply adjustments
                if (adjustment.NewPriority.HasValue)
                    context.Priority = adjustment.NewPriority.Value;
                
                if (adjustment.NewMaxBandwidth.HasValue)
                    context.MaxBandwidth = adjustment.NewMaxBandwidth.Value;
                
                if (adjustment.NewMinBandwidth.HasValue)
                    context.MinBandwidth = adjustment.NewMinBandwidth.Value;

                // Recalculate bandwidth allocation
                await RebalanceBandwidthAsync();

                OnQoSAdjusted(new QoSAdjustedEventArgs
                {
                    TransferId = transferId,
                    OldPriority = oldPriority,
                    NewPriority = context.Priority,
                    OldBandwidth = oldBandwidth,
                    NewBandwidth = context.AllocatedBandwidth,
                    Reason = adjustment.Reason
                });

                System.Diagnostics.Debug.WriteLine($"QoS adjusted for transfer {transferId}: Priority {oldPriority} -> {context.Priority}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"QoS adjustment error for {transferId}: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Calculates initial bandwidth allocation for a transfer
        /// </summary>
        private long CalculateInitialBandwidth(TransferRegistration registration)
        {
            var baseBandwidth = _config.DefaultBandwidthPerTransfer;
            
            // Adjust based on priority
            var priorityMultiplier = registration.Priority switch
            {
                TransferPriority.Critical => 2.0,
                TransferPriority.High => 1.5,
                TransferPriority.Normal => 1.0,
                TransferPriority.Low => 0.5,
                TransferPriority.Background => 0.25,
                _ => 1.0
            };

            var allocatedBandwidth = (long)(baseBandwidth * priorityMultiplier);
            
            // Respect min/max constraints
            if (registration.MaxBandwidth.HasValue)
                allocatedBandwidth = Math.Min(allocatedBandwidth, registration.MaxBandwidth.Value);
            
            if (registration.MinBandwidth.HasValue)
                allocatedBandwidth = Math.Max(allocatedBandwidth, registration.MinBandwidth.Value);

            return allocatedBandwidth;
        }

        /// <summary>
        /// Calculates allowed bytes for a transfer request
        /// </summary>
        private int CalculateAllowedBytes(TransferContext context, int requestedBytes, NetworkCondition networkCondition)
        {
            // Calculate bytes per second based on allocated bandwidth
            var bytesPerSecond = context.AllocatedBandwidth;
            
            // Adjust for network conditions
            var conditionMultiplier = networkCondition switch
            {
                NetworkCondition.Excellent => 1.0,
                NetworkCondition.Good => 0.8,
                NetworkCondition.Fair => 0.6,
                NetworkCondition.Poor => 0.4,
                NetworkCondition.Critical => 0.2,
                _ => 0.5
            };

            var adjustedBytesPerSecond = (long)(bytesPerSecond * conditionMultiplier);
            
            // Calculate allowed bytes for this time window (1 second)
            var allowedBytes = Math.Min(requestedBytes, (int)adjustedBytesPerSecond);
            
            return Math.Max(allowedBytes, _config.MinimumAllowedBytes);
        }

        /// <summary>
        /// Calculates throttle delay for bandwidth limiting
        /// </summary>
        private TimeSpan CalculateThrottleDelay(TransferContext context, int allowedBytes, int requestedBytes)
        {
            if (allowedBytes >= requestedBytes)
                return TimeSpan.Zero;

            // Calculate delay based on the difference
            var excessBytes = requestedBytes - allowedBytes;
            var delayMs = (excessBytes * 1000) / Math.Max(context.AllocatedBandwidth, 1);
            
            return TimeSpan.FromMilliseconds(Math.Min(delayMs, _config.MaxThrottleDelay.TotalMilliseconds));
        }

        /// <summary>
        /// Calculates current throughput for a transfer
        /// </summary>
        private double CalculateThroughput(TransferContext context)
        {
            var duration = DateTime.Now - context.RegisteredAt;
            if (duration.TotalSeconds < 1)
                return 0;

            return context.TotalBytesTransferred / duration.TotalSeconds;
        }

        /// <summary>
        /// Rebalances bandwidth allocation among active transfers
        /// </summary>
        private async Task RebalanceBandwidthAsync()
        {
            await Task.Run(() =>
            {
                lock (_lock)
                {
                    var totalAvailableBandwidth = _config.TotalAvailableBandwidth;
                    var transfers = _activeTransfers.Values.OrderByDescending(t => t.Priority).ToList();
                    
                    if (!transfers.Any())
                        return;

                    // Calculate total weight based on priorities
                    var totalWeight = transfers.Sum(t => GetPriorityWeight(t.Priority));
                    
                    // Allocate bandwidth proportionally
                    foreach (var transfer in transfers)
                    {
                        var weight = GetPriorityWeight(transfer.Priority);
                        var proportionalBandwidth = (long)(totalAvailableBandwidth * weight / totalWeight);
                        
                        // Respect min/max constraints
                        if (transfer.MaxBandwidth.HasValue)
                            proportionalBandwidth = Math.Min(proportionalBandwidth, transfer.MaxBandwidth.Value);
                        
                        if (transfer.MinBandwidth.HasValue)
                            proportionalBandwidth = Math.Max(proportionalBandwidth, transfer.MinBandwidth.Value);
                        
                        transfer.AllocatedBandwidth = proportionalBandwidth;
                    }
                }
            });
        }

        /// <summary>
        /// Gets priority weight for bandwidth allocation
        /// </summary>
        private double GetPriorityWeight(TransferPriority priority)
        {
            return priority switch
            {
                TransferPriority.Critical => 8.0,
                TransferPriority.High => 4.0,
                TransferPriority.Normal => 2.0,
                TransferPriority.Low => 1.0,
                TransferPriority.Background => 0.5,
                _ => 1.0
            };
        }

        /// <summary>
        /// Adapts bandwidth allocation based on network conditions
        /// </summary>
        private async void AdaptBandwidthAllocation(object state)
        {
            try
            {
                var networkCondition = await _networkMonitor.GetCurrentConditionAsync();
                
                // Detect network congestion
                if (networkCondition == NetworkCondition.Poor || networkCondition == NetworkCondition.Critical)
                {
                    OnNetworkCongestionDetected(new NetworkCongestionEventArgs
                    {
                        Condition = networkCondition,
                        ActiveTransfers = _activeTransfers.Count,
                        DetectedAt = DateTime.Now
                    });
                    
                    // Reduce bandwidth for background transfers
                    await ReduceBackgroundTransferBandwidthAsync();
                }
                
                // Rebalance based on current conditions
                await RebalanceBandwidthAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bandwidth adaptation error: {ex}");
            }
        }

        /// <summary>
        /// Reduces bandwidth for background transfers during congestion
        /// </summary>
        private async Task ReduceBackgroundTransferBandwidthAsync()
        {
            await Task.Run(() =>
            {
                var backgroundTransfers = _activeTransfers.Values
                    .Where(t => t.Priority == TransferPriority.Background || t.Priority == TransferPriority.Low)
                    .ToList();

                foreach (var transfer in backgroundTransfers)
                {
                    transfer.AllocatedBandwidth = (long)(transfer.AllocatedBandwidth * 0.5); // Reduce by 50%
                    transfer.AllocatedBandwidth = Math.Max(transfer.AllocatedBandwidth, _config.MinimumAllowedBytes);
                }
            });
        }

        /// <summary>
        /// Handles network condition changes
        /// </summary>
        private async void OnNetworkConditionChanged(object sender, NetworkConditionChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Network condition changed: {e.OldCondition} -> {e.NewCondition}");
            
            // Trigger immediate bandwidth adaptation
            await Task.Run(() => AdaptBandwidthAllocation(null));
        }

        /// <summary>
        /// Raises the BandwidthThrottled event
        /// </summary>
        private void OnBandwidthThrottled(BandwidthThrottledEventArgs args)
        {
            BandwidthThrottled?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the QoSAdjusted event
        /// </summary>
        private void OnQoSAdjusted(QoSAdjustedEventArgs args)
        {
            QoSAdjusted?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the NetworkCongestionDetected event
        /// </summary>
        private void OnNetworkCongestionDetected(NetworkCongestionEventArgs args)
        {
            NetworkCongestionDetected?.Invoke(this, args);
        }

        /// <summary>
        /// Disposes the bandwidth manager
        /// </summary>
        public void Dispose()
        {
            _adaptationTimer?.Dispose();
            _networkMonitor?.Dispose();
        }
    }

    /// <summary>
    /// Transfer registration information
    /// </summary>
    public class TransferRegistration
    {
        public TransferPriority Priority { get; set; } = TransferPriority.Normal;
        public long? MaxBandwidth { get; set; }
        public long? MinBandwidth { get; set; }
        public TransferType TransferType { get; set; }
        public long FileSize { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Transfer context for bandwidth management
    /// </summary>
    internal class TransferContext
    {
        public string TransferId { get; set; }
        public TransferPriority Priority { get; set; }
        public long? MaxBandwidth { get; set; }
        public long? MinBandwidth { get; set; }
        public TransferType TransferType { get; set; }
        public long FileSize { get; set; }
        public DateTime RegisteredAt { get; set; }
        public DateTime LastActivity { get; set; }
        public long AllocatedBandwidth { get; set; }
        public long TotalBytesTransferred { get; set; }
        public double CurrentThroughput { get; set; }
    }

    /// <summary>
    /// Bandwidth allocation result
    /// </summary>
    public class BandwidthAllocation
    {
        public bool Granted { get; set; }
        public string TransferId { get; set; }
        public int RequestedBytes { get; set; }
        public int GrantedBytes { get; set; }
        public TimeSpan ThrottleDelay { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime RequestedAt { get; set; }
    }

    /// <summary>
    /// QoS adjustment parameters
    /// </summary>
    public class QoSAdjustment
    {
        public TransferPriority? NewPriority { get; set; }
        public long? NewMaxBandwidth { get; set; }
        public long? NewMinBandwidth { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// QoS configuration
    /// </summary>
    public class QoSConfiguration
    {
        public long TotalAvailableBandwidth { get; set; } = 10 * 1024 * 1024; // 10 MB/s
        public long DefaultBandwidthPerTransfer { get; set; } = 1024 * 1024; // 1 MB/s
        public int MinimumAllowedBytes { get; set; } = 1024; // 1 KB
        public TimeSpan MaxThrottleDelay { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan AdaptationInterval { get; set; } = TimeSpan.FromSeconds(10);
        public double CongestionThreshold { get; set; } = 0.8; // 80% utilization
    }

    /// <summary>
    /// Bandwidth statistics
    /// </summary>
    public class BandwidthStatistics
    {
        public int ActiveTransfers { get; set; }
        public long TotalAllocatedBandwidth { get; set; }
        public NetworkCondition NetworkCondition { get; set; }
        public List<TransferStatistics> TransferStatistics { get; set; } = new List<TransferStatistics>();
        public DateTime GeneratedAt { get; set; }
    }

    /// <summary>
    /// Transfer statistics
    /// </summary>
    public class TransferStatistics
    {
        public string TransferId { get; set; }
        public TransferPriority Priority { get; set; }
        public long AllocatedBandwidth { get; set; }
        public double CurrentThroughput { get; set; }
        public long TotalBytesTransferred { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Network monitor for detecting network conditions
    /// </summary>
    public class NetworkMonitor : IDisposable
    {
        private NetworkCondition _currentCondition = NetworkCondition.Good;
        private readonly Timer _monitorTimer;

        public event EventHandler<NetworkConditionChangedEventArgs> NetworkConditionChanged;

        public NetworkMonitor()
        {
            _monitorTimer = new Timer(MonitorNetworkCondition, null,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Gets current network condition
        /// </summary>
        public async Task<NetworkCondition> GetCurrentConditionAsync()
        {
            return await Task.FromResult(_currentCondition);
        }

        /// <summary>
        /// Monitors network condition
        /// </summary>
        private async void MonitorNetworkCondition(object state)
        {
            try
            {
                // This is a simplified implementation
                // In a real implementation, you would measure actual network metrics
                var newCondition = await DetectNetworkConditionAsync();

                if (newCondition != _currentCondition)
                {
                    var oldCondition = _currentCondition;
                    _currentCondition = newCondition;

                    NetworkConditionChanged?.Invoke(this, new NetworkConditionChangedEventArgs
                    {
                        OldCondition = oldCondition,
                        NewCondition = newCondition,
                        DetectedAt = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Network monitoring error: {ex}");
            }
        }

        /// <summary>
        /// Detects current network condition
        /// </summary>
        private async Task<NetworkCondition> DetectNetworkConditionAsync()
        {
            // Simplified network condition detection
            // In a real implementation, you would measure:
            // - Latency
            // - Packet loss
            // - Bandwidth utilization
            // - Connection stability

            await Task.Delay(10); // Simulate measurement

            // Return random condition for demonstration
            var random = new Random();
            var conditions = Enum.GetValues<NetworkCondition>();
            return conditions[random.Next(conditions.Length)];
        }

        public void Dispose()
        {
            _monitorTimer?.Dispose();
        }
    }

    /// <summary>
    /// Transfer priorities
    /// </summary>
    public enum TransferPriority
    {
        Background,
        Low,
        Normal,
        High,
        Critical
    }

    /// <summary>
    /// Transfer types
    /// </summary>
    public enum TransferType
    {
        Upload,
        Download,
        Sync
    }

    /// <summary>
    /// Network conditions
    /// </summary>
    public enum NetworkCondition
    {
        Excellent,
        Good,
        Fair,
        Poor,
        Critical
    }

    /// <summary>
    /// Bandwidth throttled event arguments
    /// </summary>
    public class BandwidthThrottledEventArgs : EventArgs
    {
        public string TransferId { get; set; }
        public int RequestedBytes { get; set; }
        public int GrantedBytes { get; set; }
        public TimeSpan ThrottleDelay { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// QoS adjusted event arguments
    /// </summary>
    public class QoSAdjustedEventArgs : EventArgs
    {
        public string TransferId { get; set; }
        public TransferPriority OldPriority { get; set; }
        public TransferPriority NewPriority { get; set; }
        public long OldBandwidth { get; set; }
        public long NewBandwidth { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Network congestion event arguments
    /// </summary>
    public class NetworkCongestionEventArgs : EventArgs
    {
        public NetworkCondition Condition { get; set; }
        public int ActiveTransfers { get; set; }
        public DateTime DetectedAt { get; set; }
    }

    /// <summary>
    /// Network condition changed event arguments
    /// </summary>
    public class NetworkConditionChangedEventArgs : EventArgs
    {
        public NetworkCondition OldCondition { get; set; }
        public NetworkCondition NewCondition { get; set; }
        public DateTime DetectedAt { get; set; }
    }
}
