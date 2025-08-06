using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net;

namespace Shared.Platform
{
    /// <summary>
    /// Firewall and NAT traversal manager for handling restrictive network environments
    /// </summary>
    public class FirewallManager : IDisposable
    {
        // Port range constants
        private const int MinPortNumber = 1;
        private const int MaxPortNumber = 65535;
        private const int DefaultTimeoutSeconds = 30;

        // Private IP address range constants
        private const byte PrivateClassA = 10;
        private const byte PrivateClassBFirst = 172;
        private const byte PrivateClassBSecondMin = 16;
        private const byte PrivateClassBSecondMax = 31;
        private const byte PrivateClassCFirst = 192;
        private const byte PrivateClassCSecond = 168;

        // Disposal timeout
        private const int DisposalTimeoutSeconds = 10;

        private static readonly Lazy<FirewallManager> _instance = new Lazy<FirewallManager>(() => new FirewallManager());
        private readonly List<INatTraversalProvider> _natProviders;
        private readonly ConcurrentDictionary<int, PortMappingInfo> _activeMappings;
        private readonly object _lock = new object();
        private readonly ILogger _logger;
        private bool _disposed = false;

        /// <summary>
        /// Gets the singleton instance of the FirewallManager
        /// </summary>
        public static FirewallManager Instance => _instance.Value;

        /// <summary>
        /// Event raised when a port mapping is added, removed, or modified
        /// </summary>
        public event EventHandler<PortMappingEventArgs> PortMappingChanged;

        private FirewallManager()
        {
            _logger = LogManager.GetLogger<FirewallManager>();
            _natProviders = new List<INatTraversalProvider>();
            _activeMappings = new ConcurrentDictionary<int, PortMappingInfo>();
            InitializeProviders();
            _logger.Info("FirewallManager initialized");
        }

        /// <summary>
        /// Attempts to open a port through firewall and NAT
        /// </summary>
        /// <param name="port">Port number to open (1-65535)</param>
        /// <param name="protocol">Protocol type (TCP or UDP)</param>
        /// <param name="description">Description for the port mapping</param>
        /// <returns>Result of the port opening operation</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when port is not in valid range</exception>
        /// <exception cref="ArgumentNullException">Thrown when description is null</exception>
        /// <exception cref="ObjectDisposedException">Thrown when manager is disposed</exception>
        public async Task<PortOpeningResult> OpenPortAsync(int port, PortProtocol protocol = PortProtocol.TCP, string description = "LocalTalk")
        {
            using (PerformanceManager.Time("FirewallManager.OpenPort"))
            {
                ThrowIfDisposed();
                ValidatePort(port);
                if (description == null)
                    throw new ArgumentNullException(nameof(description));

                PerformanceManager.Counter("FirewallManager.OpenPortRequests");

                var result = new PortOpeningResult
                {
                    Port = port,
                    Protocol = protocol,
                    RequestedAt = DateTime.Now
                };

                try
                {
                // Check if port is already mapped
                if (_activeMappings.TryGetValue(port, out var existingMapping))
                {
                    result.Success = true;
                    result.Method = existingMapping.Method;
                    result.ExternalEndpoint = existingMapping.ExternalEndpoint;
                    result.Message = "Port already mapped";
                    return result;
                }

                // Try each NAT traversal method in order of preference
                foreach (var provider in _natProviders.OrderBy(p => p.Priority))
                {
                    if (!await provider.IsAvailableAsync())
                        continue;

                    var mappingResult = await provider.CreatePortMappingAsync(port, protocol, description);
                    if (mappingResult.Success)
                    {
                        var mappingInfo = new PortMappingInfo
                        {
                            Port = port,
                            Protocol = protocol,
                            Method = provider.MethodName,
                            ExternalEndpoint = mappingResult.ExternalEndpoint,
                            CreatedAt = DateTime.Now,
                            Description = description
                        };

                        _activeMappings[port] = mappingInfo;

                        result.Success = true;
                        result.Method = provider.MethodName;
                        result.ExternalEndpoint = mappingResult.ExternalEndpoint;
                        result.Message = $"Port opened using {provider.MethodName}";

                        OnPortMappingChanged(new PortMappingEventArgs
                        {
                            Port = port,
                            Action = PortMappingAction.Created,
                            Method = provider.MethodName
                        });

                        _logger.Info($"Successfully opened port {port} using {provider.MethodName}");
                        PerformanceManager.Counter("FirewallManager.SuccessfulPortOpenings");
                        return result;
                    }
                }

                result.Message = "All NAT traversal methods failed";
                _logger.Warning($"Failed to open port {port} - all methods exhausted");
                PerformanceManager.Counter("FirewallManager.FailedPortOpenings");
            }
            catch (Exception ex)
            {
                result.Message = $"Error opening port: {ex.Message}";
                _logger.Error($"Error opening port {port}", ex);
            }

                return result;
            }
        }

        /// <summary>
        /// Closes a previously opened port
        /// </summary>
        /// <param name="port">Port number to close</param>
        /// <returns>True if port was successfully closed or was already closed</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when port is not in valid range</exception>
        /// <exception cref="ObjectDisposedException">Thrown when manager is disposed</exception>
        public async Task<bool> ClosePortAsync(int port)
        {
            ThrowIfDisposed();
            ValidatePort(port);

            try
            {
                if (!_activeMappings.TryGetValue(port, out var mappingInfo))
                {
                    return true; // Already closed or never opened
                }

                // Find the provider that created this mapping
                var provider = _natProviders.FirstOrDefault(p => p.MethodName == mappingInfo.Method);
                if (provider != null)
                {
                    var success = await provider.DeletePortMappingAsync(port, mappingInfo.Protocol);
                    if (success)
                    {
                        _activeMappings.TryRemove(port, out _);

                        OnPortMappingChanged(new PortMappingEventArgs
                        {
                            Port = port,
                            Action = PortMappingAction.Deleted,
                            Method = mappingInfo.Method
                        });

                        _logger.Info($"Successfully closed port {port}");
                        return true;
                    }
                }

                _logger.Warning($"Failed to close port {port}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error closing port {port}", ex);
                return false;
            }
        }

        /// <summary>
        /// Closes all active port mappings
        /// </summary>
        public async Task CloseAllPortsAsync()
        {
            List<int> portsToClose;
            lock (_lock)
            {
                portsToClose = _activeMappings.Keys.ToList();
            }

            foreach (var port in portsToClose)
            {
                await ClosePortAsync(port);
            }
        }

        /// <summary>
        /// Gets information about active port mappings
        /// </summary>
        /// <returns>List of active port mappings</returns>
        /// <exception cref="ObjectDisposedException">Thrown when manager is disposed</exception>
        public List<PortMappingInfo> GetActiveMappings()
        {
            ThrowIfDisposed();
            return _activeMappings.Values.ToList();
        }

        /// <summary>
        /// Checks if a specific port is currently mapped
        /// </summary>
        public bool IsPortMapped(int port)
        {
            lock (_lock)
            {
                return _activeMappings.ContainsKey(port);
            }
        }

        /// <summary>
        /// Gets available NAT traversal methods
        /// </summary>
        public async Task<List<NatTraversalMethodInfo>> GetAvailableMethodsAsync()
        {
            var methods = new List<NatTraversalMethodInfo>();

            foreach (var provider in _natProviders)
            {
                var isAvailable = await provider.IsAvailableAsync();
                methods.Add(new NatTraversalMethodInfo
                {
                    Name = provider.MethodName,
                    IsAvailable = isAvailable,
                    Priority = provider.Priority,
                    Description = provider.Description
                });
            }

            return methods.OrderBy(m => m.Priority).ToList();
        }

        /// <summary>
        /// Performs a comprehensive network connectivity test
        /// </summary>
        public async Task<NetworkConnectivityResult> TestConnectivityAsync(int testPort = 0)
        {
            var result = new NetworkConnectivityResult
            {
                TestTimestamp = DateTime.Now,
                TestPort = testPort
            };

            try
            {
                // Test local network connectivity
                result.LocalNetworkConnected = await TestLocalNetworkAsync();

                // Test internet connectivity
                result.InternetConnected = await TestInternetConnectivityAsync();

                // Test NAT detection
                result.BehindNat = await DetectNatAsync();

                // Test firewall restrictions
                result.FirewallRestricted = await DetectFirewallAsync();

                // If a test port is specified, test port accessibility
                if (testPort > 0)
                {
                    result.PortAccessible = await TestPortAccessibilityAsync(testPort);
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.Error("Connectivity test error", ex);
            }

            return result;
        }

        /// <summary>
        /// Initializes NAT traversal providers
        /// </summary>
        private void InitializeProviders()
        {
            // UPnP provider (highest priority)
            _natProviders.Add(new UpnpProvider());

            // NAT-PMP provider
            _natProviders.Add(new NatPmpProvider());

            // Manual configuration provider (lowest priority)
            _natProviders.Add(new ManualConfigProvider());
        }

        /// <summary>
        /// Tests local network connectivity
        /// </summary>
        private async Task<bool> TestLocalNetworkAsync()
        {
            try
            {
                // Try to ping the default gateway
                var networkInterfaces = await NetworkInterfaceManager.Instance.GetActiveInterfacesAsync();
                return networkInterfaces.Any();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Tests internet connectivity
        /// </summary>
        private async Task<bool> TestInternetConnectivityAsync()
        {
            try
            {
                // Simple connectivity test to a reliable service
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = await client.GetAsync("http://www.google.com");
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Detects if the device is behind NAT
        /// </summary>
        private async Task<bool> DetectNatAsync()
        {
            try
            {
                // This is a simplified NAT detection
                // In a real implementation, you would use STUN servers
                var localInterfaces = await NetworkInterfaceManager.Instance.GetActiveInterfacesAsync();
                var hasPrivateIp = localInterfaces.Any(i => IsPrivateIpAddress(i.IpAddress));
                return hasPrivateIp;
            }
            catch
            {
                return true; // Assume NAT if detection fails
            }
        }

        /// <summary>
        /// Detects firewall restrictions
        /// </summary>
        private async Task<bool> DetectFirewallAsync()
        {
            // This is a placeholder implementation
            // Real firewall detection would require platform-specific code
            return await Task.FromResult(false);
        }

        /// <summary>
        /// Tests if a specific port is accessible from outside
        /// </summary>
        private async Task<bool> TestPortAccessibilityAsync(int port)
        {
            // This is a placeholder implementation
            // Real port accessibility testing would require external services
            return await Task.FromResult(false);
        }

        /// <summary>
        /// Checks if an IP address is private
        /// </summary>
        private bool IsPrivateIpAddress(string ipAddress)
        {
            if (!IPAddress.TryParse(ipAddress, out var ip))
                return false;

            var bytes = ip.GetAddressBytes();

            // 10.0.0.0/8
            if (bytes[0] == PrivateClassA)
                return true;

            // 172.16.0.0/12
            if (bytes[0] == PrivateClassBFirst && bytes[1] >= PrivateClassBSecondMin && bytes[1] <= PrivateClassBSecondMax)
                return true;

            // 192.168.0.0/16
            if (bytes[0] == PrivateClassCFirst && bytes[1] == PrivateClassCSecond)
                return true;

            return false;
        }

        /// <summary>
        /// Raises the PortMappingChanged event
        /// </summary>
        private void OnPortMappingChanged(PortMappingEventArgs args)
        {
            PortMappingChanged?.Invoke(this, args);
        }

        /// <summary>
        /// Validates that a port number is in the valid range
        /// </summary>
        /// <param name="port">Port number to validate</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when port is not in valid range</exception>
        private static void ValidatePort(int port)
        {
            if (port < MinPortNumber || port > MaxPortNumber)
                throw new ArgumentOutOfRangeException(nameof(port), port, $"Port must be between {MinPortNumber} and {MaxPortNumber}");
        }

        /// <summary>
        /// Throws ObjectDisposedException if the manager has been disposed
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when manager is disposed</exception>
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FirewallManager));
        }

        /// <summary>
        /// Disposes the FirewallManager and cleans up resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected dispose method for proper disposal pattern
        /// </summary>
        /// <param name="disposing">True if disposing managed resources</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Close all active port mappings
                var closeTasks = _activeMappings.Keys.Select(ClosePortAsync);
                try
                {
                    Task.WaitAll(closeTasks.ToArray(), TimeSpan.FromSeconds(DisposalTimeoutSeconds));
                }
                catch (Exception ex)
                {
                    _logger.Error("Error closing ports during disposal", ex);
                }

                _activeMappings.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Port opening result
    /// </summary>
    public class PortOpeningResult
    {
        public bool Success { get; set; }
        public int Port { get; set; }
        public PortProtocol Protocol { get; set; }
        public string Method { get; set; }
        public string ExternalEndpoint { get; set; }
        public string Message { get; set; }
        public DateTime RequestedAt { get; set; }
    }

    /// <summary>
    /// Port mapping information
    /// </summary>
    public class PortMappingInfo
    {
        public int Port { get; set; }
        public PortProtocol Protocol { get; set; }
        public string Method { get; set; }
        public string ExternalEndpoint { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// NAT traversal method information
    /// </summary>
    public class NatTraversalMethodInfo
    {
        /// <summary>
        /// Name of the NAT traversal method
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Indicates whether this method is currently available
        /// </summary>
        public bool IsAvailable { get; set; }

        /// <summary>
        /// Priority of this method (lower values = higher priority)
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// Human-readable description of the method
        /// </summary>
        public string Description { get; set; }
    }

    /// <summary>
    /// Network connectivity test result
    /// </summary>
    public class NetworkConnectivityResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime TestTimestamp { get; set; }
        public int TestPort { get; set; }
        public bool LocalNetworkConnected { get; set; }
        public bool InternetConnected { get; set; }
        public bool BehindNat { get; set; }
        public bool FirewallRestricted { get; set; }
        public bool PortAccessible { get; set; }
    }

    /// <summary>
    /// Port mapping event arguments
    /// </summary>
    public class PortMappingEventArgs : EventArgs
    {
        public int Port { get; set; }
        public PortMappingAction Action { get; set; }
        public string Method { get; set; }
    }

    /// <summary>
    /// Port mapping actions
    /// </summary>
    public enum PortMappingAction
    {
        Created,
        Deleted,
        Failed
    }

    /// <summary>
    /// Port protocols
    /// </summary>
    public enum PortProtocol
    {
        TCP,
        UDP
    }
}
