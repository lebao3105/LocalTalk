using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Shared.Platform;

namespace Shared.Network
{
    /// <summary>
    /// Network condition monitoring, automatic protocol adaptation, and seamless failover
    /// between different network interfaces
    /// </summary>
    public class NetworkMonitor : IDisposable
    {
        #region Constants
        /// <summary>
        /// Initial delay before starting monitoring in seconds.
        /// </summary>
        private const int InitialDelaySeconds = 1;

        /// <summary>
        /// Default timeout for network operations in milliseconds.
        /// </summary>
        private const int DefaultTimeoutMilliseconds = 5000;

        /// <summary>
        /// Maximum number of history entries to maintain.
        /// </summary>
        private const int MaxHistoryEntries = 100;

        /// <summary>
        /// Timeout for ping operations in milliseconds.
        /// </summary>
        private const int PingTimeoutMilliseconds = 3000;

        /// <summary>
        /// Chunk size for excellent network conditions (8MB).
        /// </summary>
        private const int ExcellentNetworkChunkSize = 8 * 1024 * 1024;

        /// <summary>
        /// Concurrent connections for excellent network conditions.
        /// </summary>
        private const int ExcellentNetworkConnections = 8;

        /// <summary>
        /// Chunk size for good network conditions (4MB).
        /// </summary>
        private const int GoodNetworkChunkSize = 4 * 1024 * 1024;

        /// <summary>
        /// Concurrent connections for good network conditions.
        /// </summary>
        private const int GoodNetworkConnections = 4;

        /// <summary>
        /// Chunk size for fair network conditions (1MB).
        /// </summary>
        private const int FairNetworkChunkSize = 1024 * 1024;

        /// <summary>
        /// Concurrent connections for fair network conditions.
        /// </summary>
        private const int FairNetworkConnections = 2;
        #endregion

        private static NetworkMonitor _instance;
        private readonly ConcurrentDictionary<string, NetworkInterface> _monitoredInterfaces;
        private readonly ConcurrentDictionary<string, NetworkCondition> _networkConditions;
        private readonly Timer _monitoringTimer;
        private readonly NetworkMonitorConfiguration _config;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private bool _disposed;

        /// <summary>
        /// Gets the singleton instance of the NetworkMonitor
        /// </summary>
        public static NetworkMonitor Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new NetworkMonitor();
                }
                return _instance;
            }
        }

        /// <summary>
        /// Event raised when network conditions change for a monitored interface
        /// </summary>
        public event EventHandler<NetworkConditionChangedEventArgs> NetworkConditionChanged;

        /// <summary>
        /// Event raised when a network interface is added, removed, or modified
        /// </summary>
        public event EventHandler<NetworkInterfaceChangedEventArgs> NetworkInterfaceChanged;

        /// <summary>
        /// Event raised when a network failure is detected
        /// </summary>
        public event EventHandler<NetworkFailureDetectedEventArgs> NetworkFailureDetected;

        /// <summary>
        /// Event raised when network connectivity is recovered
        /// </summary>
        public event EventHandler<NetworkRecoveredEventArgs> NetworkRecovered;

        private NetworkMonitor()
        {
            _monitoredInterfaces = new ConcurrentDictionary<string, NetworkInterface>();
            _networkConditions = new ConcurrentDictionary<string, NetworkCondition>();
            _config = new NetworkMonitorConfiguration();
            _cancellationTokenSource = new CancellationTokenSource();

            // Start monitoring timer
            _monitoringTimer = new Timer(MonitorNetworkConditions, null,
                TimeSpan.FromSeconds(InitialDelaySeconds), TimeSpan.FromSeconds(_config.MonitoringIntervalSeconds));

            // Subscribe to network change events
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

            // Initialize with current network interfaces
            InitializeNetworkInterfaces();
        }

        /// <summary>
        /// Starts monitoring a specific network interface
        /// </summary>
        /// <param name="interfaceId">Unique identifier of the network interface to monitor</param>
        public void StartMonitoring(string interfaceId)
        {
            var networkInterface = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(ni => ni.Id == interfaceId);

            if (networkInterface != null)
            {
                _monitoredInterfaces[interfaceId] = networkInterface;
                _networkConditions[interfaceId] = new NetworkCondition
                {
                    InterfaceId = interfaceId,
                    InterfaceName = networkInterface.Name,
                    Status = NetworkStatus.Unknown,
                    LastChecked = DateTime.Now,
                    LatencyHistory = new Queue<double>(),
                    BandwidthHistory = new Queue<double>(),
                    PacketLossHistory = new Queue<double>()
                };
            }
        }

        /// <summary>
        /// Stops monitoring a specific network interface
        /// </summary>
        /// <param name="interfaceId">Unique identifier of the network interface to stop monitoring</param>
        public void StopMonitoring(string interfaceId)
        {
            _monitoredInterfaces.TryRemove(interfaceId, out _);
            _networkConditions.TryRemove(interfaceId, out _);
        }

        /// <summary>
        /// Gets the current network condition for an interface
        /// </summary>
        /// <param name="interfaceId">Unique identifier of the network interface</param>
        /// <returns>Current network condition or null if interface is not monitored</returns>
        public NetworkCondition GetNetworkCondition(string interfaceId)
        {
            return _networkConditions.TryGetValue(interfaceId, out var condition) ? condition : null;
        }

        /// <summary>
        /// Gets all current network conditions
        /// </summary>
        public List<NetworkCondition> GetAllNetworkConditions()
        {
            return _networkConditions.Values.ToList();
        }

        /// <summary>
        /// Gets the best available network interface based on current conditions
        /// </summary>
        public string GetBestNetworkInterface()
        {
            var availableConditions = _networkConditions.Values
                .Where(c => c.Status == NetworkStatus.Good || c.Status == NetworkStatus.Fair)
                .OrderByDescending(c => c.QualityScore)
                .ToList();

            return availableConditions.FirstOrDefault()?.InterfaceId;
        }

        /// <summary>
        /// Performs a network connectivity test
        /// </summary>
        public async Task<NetworkTestResult> TestConnectivityAsync(string interfaceId, string targetHost = "8.8.8.8")
        {
            try
            {
                var ping = new Ping();
                var reply = await ping.SendPingAsync(targetHost, _config.PingTimeoutMs);

                var result = new NetworkTestResult
                {
                    InterfaceId = interfaceId,
                    TargetHost = targetHost,
                    Success = reply.Status == IPStatus.Success,
                    Latency = reply.Status == IPStatus.Success ? reply.RoundtripTime : -1,
                    Timestamp = DateTime.Now
                };

                if (!result.Success)
                {
                    result.ErrorMessage = reply.Status.ToString();
                }

                return result;
            }
            catch (Exception ex)
            {
                return new NetworkTestResult
                {
                    InterfaceId = interfaceId,
                    TargetHost = targetHost,
                    Success = false,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.Now
                };
            }
        }

        /// <summary>
        /// Adapts transfer protocol based on network conditions
        /// </summary>
        public TransferProtocolRecommendation GetProtocolRecommendation(string interfaceId)
        {
            var condition = GetNetworkCondition(interfaceId);
            if (condition == null)
            {
                return new TransferProtocolRecommendation
                {
                    RecommendedProtocol = TransferProtocol.HTTP,
                    ChunkSize = 1024 * 1024, // 1MB default
                    ConcurrentConnections = 1,
                    Reason = "Interface not monitored"
                };
            }

            return condition.Status switch
            {
                NetworkStatus.Excellent => new TransferProtocolRecommendation
                {
                    RecommendedProtocol = TransferProtocol.HTTP,
                    ChunkSize = ExcellentNetworkChunkSize,
                    ConcurrentConnections = ExcellentNetworkConnections,
                    CompressionEnabled = false,
                    Reason = "Excellent network conditions"
                },
                NetworkStatus.Good => new TransferProtocolRecommendation
                {
                    RecommendedProtocol = TransferProtocol.HTTP,
                    ChunkSize = GoodNetworkChunkSize,
                    ConcurrentConnections = GoodNetworkConnections,
                    CompressionEnabled = false,
                    Reason = "Good network conditions"
                },
                NetworkStatus.Fair => new TransferProtocolRecommendation
                {
                    RecommendedProtocol = TransferProtocol.HTTP,
                    ChunkSize = FairNetworkChunkSize,
                    ConcurrentConnections = FairNetworkConnections,
                    CompressionEnabled = true,
                    Reason = "Fair network conditions - enabling compression"
                },
                NetworkStatus.Poor => new TransferProtocolRecommendation
                {
                    RecommendedProtocol = TransferProtocol.HTTP,
                    ChunkSize = 256 * 1024, // 256KB
                    ConcurrentConnections = 1,
                    CompressionEnabled = true,
                    Reason = "Poor network conditions - reducing chunk size and connections"
                },
                _ => new TransferProtocolRecommendation
                {
                    RecommendedProtocol = TransferProtocol.HTTP,
                    ChunkSize = 64 * 1024, // 64KB
                    ConcurrentConnections = 1,
                    CompressionEnabled = true,
                    Reason = "Unknown/failed network conditions - conservative settings"
                }
            };
        }

        /// <summary>
        /// Initializes monitoring for all available network interfaces
        /// </summary>
        private void InitializeNetworkInterfaces()
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                           ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            foreach (var networkInterface in interfaces)
            {
                StartMonitoring(networkInterface.Id);
            }
        }

        /// <summary>
        /// Monitors network conditions for all interfaces
        /// </summary>
        private async void MonitorNetworkConditions(object state)
        {
            if (_disposed || _cancellationTokenSource.Token.IsCancellationRequested)
                return;

            var monitoringTasks = _monitoredInterfaces.Keys
                .Select(interfaceId => MonitorInterfaceConditionAsync(interfaceId))
                .ToArray();

            try
            {
                await Task.WhenAll(monitoringTasks);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error monitoring network conditions: {ex.Message}");
            }
        }

        /// <summary>
        /// Monitors condition for a specific interface
        /// </summary>
        private async Task MonitorInterfaceConditionAsync(string interfaceId)
        {
            if (!_networkConditions.TryGetValue(interfaceId, out var condition))
                return;

            try
            {
                // Test connectivity
                var testResult = await TestConnectivityAsync(interfaceId);

                // Update condition based on test result
                var previousStatus = condition.Status;
                UpdateNetworkCondition(condition, testResult);

                // Fire events if status changed
                if (condition.Status != previousStatus)
                {
                    OnNetworkConditionChanged(new NetworkConditionChangedEventArgs
                    {
                        InterfaceId = interfaceId,
                        PreviousStatus = previousStatus,
                        CurrentStatus = condition.Status,
                        Condition = condition
                    });

                    if (condition.Status == NetworkStatus.Failed && previousStatus != NetworkStatus.Failed)
                    {
                        OnNetworkFailureDetected(new NetworkFailureDetectedEventArgs
                        {
                            InterfaceId = interfaceId,
                            FailureTime = DateTime.Now,
                            Condition = condition
                        });
                    }
                    else if (condition.Status != NetworkStatus.Failed && previousStatus == NetworkStatus.Failed)
                    {
                        OnNetworkRecovered(new NetworkRecoveredEventArgs
                        {
                            InterfaceId = interfaceId,
                            RecoveryTime = DateTime.Now,
                            Condition = condition
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error monitoring interface {interfaceId}: {ex.Message}");
                condition.Status = NetworkStatus.Failed;
                condition.LastError = ex.Message;
            }

            condition.LastChecked = DateTime.Now;
        }

        /// <summary>
        /// Updates network condition based on test result
        /// </summary>
        private void UpdateNetworkCondition(NetworkCondition condition, NetworkTestResult testResult)
        {
            // Update latency history
            if (testResult.Success && testResult.Latency > 0)
            {
                condition.LatencyHistory.Enqueue(testResult.Latency);
                if (condition.LatencyHistory.Count > _config.HistorySize)
                {
                    condition.LatencyHistory.Dequeue();
                }
                condition.AverageLatency = condition.LatencyHistory.Average();
            }

            // Determine status based on latency and success
            if (!testResult.Success)
            {
                condition.Status = NetworkStatus.Failed;
                condition.LastError = testResult.ErrorMessage;
                condition.QualityScore = 0;
            }
            else
            {
                condition.Status = testResult.Latency switch
                {
                    < 50 => NetworkStatus.Excellent,
                    < 100 => NetworkStatus.Good,
                    < 200 => NetworkStatus.Fair,
                    < 500 => NetworkStatus.Poor,
                    _ => NetworkStatus.Failed
                };

                // Calculate quality score (0-100)
                condition.QualityScore = Math.Max(0, 100 - (int)(testResult.Latency / 5));
                condition.LastError = null;
            }
        }

        /// <summary>
        /// Handles network availability changes
        /// </summary>
        private void OnNetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            if (e.IsAvailable)
            {
                // Network became available - reinitialize interfaces
                InitializeNetworkInterfaces();
            }
            else
            {
                // Network became unavailable - mark all as failed
                foreach (var condition in _networkConditions.Values)
                {
                    condition.Status = NetworkStatus.Failed;
                    condition.LastError = "Network unavailable";
                }
            }
        }

        /// <summary>
        /// Handles network address changes
        /// </summary>
        private void OnNetworkAddressChanged(object sender, EventArgs e)
        {
            // Reinitialize network interfaces when addresses change
            InitializeNetworkInterfaces();
        }

        private void OnNetworkConditionChanged(NetworkConditionChangedEventArgs args)
        {
            NetworkConditionChanged?.Invoke(this, args);
        }

        private void OnNetworkInterfaceChanged(NetworkInterfaceChangedEventArgs args)
        {
            NetworkInterfaceChanged?.Invoke(this, args);
        }

        private void OnNetworkFailureDetected(NetworkFailureDetectedEventArgs args)
        {
            NetworkFailureDetected?.Invoke(this, args);
        }

        private void OnNetworkRecovered(NetworkRecoveredEventArgs args)
        {
            NetworkRecovered?.Invoke(this, args);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cancellationTokenSource.Cancel();
                _monitoringTimer?.Dispose();
                _cancellationTokenSource?.Dispose();

                // Unsubscribe from events
                NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
                NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;

                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Network monitor configuration
    /// </summary>
    public class NetworkMonitorConfiguration
    {
        #region Configuration Constants
        /// <summary>
        /// Default monitoring interval in seconds.
        /// </summary>
        private const int DefaultMonitoringIntervalSeconds = 5;

        /// <summary>
        /// Default ping timeout in milliseconds.
        /// </summary>
        private const int DefaultPingTimeoutMs = 3000;

        /// <summary>
        /// Default history size for network conditions.
        /// </summary>
        private const int DefaultHistorySize = 10;
        #endregion

        /// <summary>
        /// Gets or sets the monitoring interval in seconds
        /// </summary>
        public int MonitoringIntervalSeconds { get; set; } = DefaultMonitoringIntervalSeconds;

        /// <summary>
        /// Gets or sets the ping timeout in milliseconds
        /// </summary>
        public int PingTimeoutMs { get; set; } = DefaultPingTimeoutMs;

        /// <summary>
        /// Gets or sets the maximum number of history entries to keep
        /// </summary>
        public int HistorySize { get; set; } = DefaultHistorySize;

        /// <summary>
        /// Gets or sets the test hosts for connectivity checks
        /// </summary>
        public string[] TestHosts { get; set; } = { "8.8.8.8", "1.1.1.1", "208.67.222.222" };
    }

    /// <summary>
    /// Network condition information
    /// </summary>
    public class NetworkCondition
    {
        public string InterfaceId { get; set; }
        public string InterfaceName { get; set; }
        public NetworkStatus Status { get; set; }
        public DateTime LastChecked { get; set; }
        public double AverageLatency { get; set; }
        public double QualityScore { get; set; }
        public string LastError { get; set; }
        public Queue<double> LatencyHistory { get; set; }
        public Queue<double> BandwidthHistory { get; set; }
        public Queue<double> PacketLossHistory { get; set; }
    }

    /// <summary>
    /// Network test result
    /// </summary>
    public class NetworkTestResult
    {
        public string InterfaceId { get; set; }
        public string TargetHost { get; set; }
        public bool Success { get; set; }
        public double Latency { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Transfer protocol recommendation
    /// </summary>
    public class TransferProtocolRecommendation
    {
        public TransferProtocol RecommendedProtocol { get; set; }
        public int ChunkSize { get; set; }
        public int ConcurrentConnections { get; set; }
        public bool CompressionEnabled { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Network status enumeration
    /// </summary>
    public enum NetworkStatus
    {
        Unknown,
        Failed,
        Poor,
        Fair,
        Good,
        Excellent
    }

    /// <summary>
    /// Transfer protocol enumeration
    /// </summary>
    public enum TransferProtocol
    {
        HTTP,
        HTTPS,
        TCP,
        UDP
    }

    /// <summary>
    /// Network condition changed event arguments
    /// </summary>
    public class NetworkConditionChangedEventArgs : EventArgs
    {
        public string InterfaceId { get; set; }
        public NetworkStatus PreviousStatus { get; set; }
        public NetworkStatus CurrentStatus { get; set; }
        public NetworkCondition Condition { get; set; }
    }

    /// <summary>
    /// Network interface changed event arguments
    /// </summary>
    public class NetworkInterfaceChangedEventArgs : EventArgs
    {
        public string InterfaceId { get; set; }
        public string InterfaceName { get; set; }
        public bool IsAvailable { get; set; }
    }

    /// <summary>
    /// Network failure detected event arguments
    /// </summary>
    public class NetworkFailureDetectedEventArgs : EventArgs
    {
        public string InterfaceId { get; set; }
        public DateTime FailureTime { get; set; }
        public NetworkCondition Condition { get; set; }
    }

    /// <summary>
    /// Network recovered event arguments
    /// </summary>
    public class NetworkRecoveredEventArgs : EventArgs
    {
        public string InterfaceId { get; set; }
        public DateTime RecoveryTime { get; set; }
        public NetworkCondition Condition { get; set; }
    }
}
