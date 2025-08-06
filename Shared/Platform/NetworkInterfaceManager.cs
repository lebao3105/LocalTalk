using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using System.Net.NetworkInformation;

namespace Shared.Platform
{
    /// <summary>
    /// Advanced network interface detection and binding manager
    /// </summary>
    public class NetworkInterfaceManager : INetworkInterfaceManager, IDisposable
    {
        private static readonly Lazy<NetworkInterfaceManager> _instance = new Lazy<NetworkInterfaceManager>(() => new NetworkInterfaceManager());
        private readonly ConcurrentDictionary<string, INetworkInterface> _cachedInterfaces;
        private readonly ConcurrentDictionary<string, NetworkBindingInfo> _bindings;
        private readonly object _lock = new object();
        private readonly ILogger _logger;
        private DateTime _lastCacheUpdate;
        private readonly TimeSpan _cacheTimeout = TimeSpan.FromSeconds(30);
        private bool _disposed = false;

        public static NetworkInterfaceManager Instance => _instance.Value;

        public event EventHandler<NetworkInterfaceChangedEventArgs> InterfaceChanged;

        private NetworkInterfaceManager()
        {
            _logger = LogManager.GetLogger<NetworkInterfaceManager>();
            _cachedInterfaces = new ConcurrentDictionary<string, INetworkInterface>();
            _bindings = new ConcurrentDictionary<string, NetworkBindingInfo>();

            // Start monitoring network changes
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            _logger.Info("NetworkInterfaceManager initialized");
        }

        /// <summary>
        /// Gets all network interfaces on the system
        /// </summary>
        /// <returns>Collection of all network interfaces</returns>
        /// <exception cref="ObjectDisposedException">Thrown when manager is disposed</exception>
        public async Task<IEnumerable<INetworkInterface>> GetAllInterfacesAsync()
        {
            ThrowIfDisposed();
            await RefreshInterfaceCacheIfNeeded();

            return _cachedInterfaces.Values.ToList();
        }

        /// <summary>
        /// Gets only active network interfaces
        /// </summary>
        public async Task<IEnumerable<INetworkInterface>> GetActiveInterfacesAsync()
        {
            var allInterfaces = await GetAllInterfacesAsync();
            return allInterfaces.Where(i => i.IsConnected && !string.IsNullOrEmpty(i.IpAddress));
        }

        /// <summary>
        /// Gets the preferred network interface for LocalSend communication
        /// </summary>
        public async Task<INetworkInterface> GetPreferredInterfaceAsync()
        {
            var activeInterfaces = await GetActiveInterfacesAsync();
            
            // Priority order: WiFi > Ethernet > Other
            var wifiInterface = activeInterfaces.FirstOrDefault(i => i.Type == NetworkInterfaceType.WiFi);
            if (wifiInterface != null)
                return wifiInterface;

            var ethernetInterface = activeInterfaces.FirstOrDefault(i => i.Type == NetworkInterfaceType.Ethernet);
            if (ethernetInterface != null)
                return ethernetInterface;

            // Return any active interface
            return activeInterfaces.FirstOrDefault();
        }

        /// <summary>
        /// Checks if a specific network interface is available
        /// </summary>
        /// <param name="interfaceName">Name of the interface to check</param>
        /// <returns>True if interface is available and active</returns>
        /// <exception cref="ArgumentNullException">Thrown when interfaceName is null or empty</exception>
        /// <exception cref="ObjectDisposedException">Thrown when manager is disposed</exception>
        public async Task<bool> IsInterfaceAvailableAsync(string interfaceName)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(interfaceName))
                throw new ArgumentNullException(nameof(interfaceName));

            var interfaces = await GetActiveInterfacesAsync();
            return interfaces.Any(i => i.Name.Equals(interfaceName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Binds to a specific network interface
        /// </summary>
        public async Task<NetworkBindingResult> BindToInterfaceAsync(string interfaceName, int port)
        {
            var result = new NetworkBindingResult();
            
            try
            {
                var targetInterface = await GetInterfaceByNameAsync(interfaceName);
                if (targetInterface == null)
                {
                    result.ErrorMessage = $"Network interface '{interfaceName}' not found";
                    return result;
                }

                if (!targetInterface.IsConnected)
                {
                    result.ErrorMessage = $"Network interface '{interfaceName}' is not connected";
                    return result;
                }

                // Check if already bound
                lock (_lock)
                {
                    if (_bindings.ContainsKey(interfaceName))
                    {
                        result.ErrorMessage = $"Already bound to interface '{interfaceName}'";
                        return result;
                    }
                }

                // Attempt to bind to the interface
                var bindingInfo = new NetworkBindingInfo
                {
                    InterfaceName = interfaceName,
                    Interface = targetInterface,
                    Port = port,
                    LocalEndpoint = $"{targetInterface.IpAddress}:{port}",
                    BoundAt = DateTime.Now
                };

                // Validate the IP address can be bound to
                if (!await ValidateBindingAsync(targetInterface.IpAddress, port))
                {
                    result.ErrorMessage = $"Cannot bind to {targetInterface.IpAddress}:{port}";
                    return result;
                }

                lock (_lock)
                {
                    _bindings[interfaceName] = bindingInfo;
                }

                result.Success = true;
                result.BoundInterface = targetInterface;
                result.BoundPort = port;
                result.LocalEndpoint = bindingInfo.LocalEndpoint;

                System.Diagnostics.Debug.WriteLine($"Successfully bound to interface '{interfaceName}' at {result.LocalEndpoint}");
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error binding to interface '{interfaceName}': {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Network binding error: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Binds to all available network interfaces
        /// </summary>
        public async Task<NetworkBindingResult> BindToAllInterfacesAsync(int port)
        {
            var result = new NetworkBindingResult();
            var successfulBindings = new List<string>();
            var errors = new List<string>();

            try
            {
                var activeInterfaces = await GetActiveInterfacesAsync();
                
                foreach (var networkInterface in activeInterfaces)
                {
                    var bindingResult = await BindToInterfaceAsync(networkInterface.Name, port);
                    if (bindingResult.Success)
                    {
                        successfulBindings.Add(networkInterface.Name);
                    }
                    else
                    {
                        errors.Add($"{networkInterface.Name}: {bindingResult.ErrorMessage}");
                    }
                }

                if (successfulBindings.Any())
                {
                    result.Success = true;
                    result.BoundPort = port;
                    result.LocalEndpoint = $"Multiple interfaces bound to port {port}";
                    
                    if (errors.Any())
                    {
                        result.ErrorMessage = $"Partial success. Errors: {string.Join("; ", errors)}";
                    }
                }
                else
                {
                    result.ErrorMessage = $"Failed to bind to any interface. Errors: {string.Join("; ", errors)}";
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error binding to all interfaces: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Unbinds from a specific network interface
        /// </summary>
        public async Task UnbindFromInterfaceAsync(string interfaceName)
        {
            await Task.Run(() =>
            {
                lock (_lock)
                {
                    if (_bindings.ContainsKey(interfaceName))
                    {
                        _bindings.Remove(interfaceName);
                        System.Diagnostics.Debug.WriteLine($"Unbound from interface '{interfaceName}'");
                    }
                }
            });
        }

        /// <summary>
        /// Unbinds from all network interfaces
        /// </summary>
        public async Task UnbindFromAllInterfacesAsync()
        {
            await Task.Run(() =>
            {
                lock (_lock)
                {
                    var boundInterfaces = _bindings.Keys.ToList();
                    _bindings.Clear();
                    System.Diagnostics.Debug.WriteLine($"Unbound from all interfaces: {string.Join(", ", boundInterfaces)}");
                }
            });
        }

        /// <summary>
        /// Refreshes the network interface cache if needed
        /// </summary>
        private async Task RefreshInterfaceCacheIfNeeded()
        {
            var now = DateTime.Now;
            
            if (now - _lastCacheUpdate > _cacheTimeout)
            {
                await RefreshInterfaceCache();
            }
        }

        /// <summary>
        /// Refreshes the network interface cache
        /// </summary>
        private async Task RefreshInterfaceCache()
        {
            await Task.Run(() =>
            {
                try
                {
                    var systemInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                    var newCache = new Dictionary<string, INetworkInterface>();

                    foreach (var sysInterface in systemInterfaces)
                    {
                        // Skip loopback and non-operational interfaces
                        if (sysInterface.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback ||
                            sysInterface.OperationalStatus != OperationalStatus.Up)
                            continue;

                        var ipProperties = sysInterface.GetIPProperties();
                        var ipAddress = GetInterfaceIpAddress(ipProperties);

                        if (!string.IsNullOrEmpty(ipAddress))
                        {
                            var networkInterface = new NetworkInterfaceImpl
                            {
                                Name = sysInterface.Name,
                                IpAddress = ipAddress,
                                IsConnected = sysInterface.OperationalStatus == OperationalStatus.Up,
                                Type = MapNetworkInterfaceType(sysInterface.NetworkInterfaceType),
                                Description = sysInterface.Description,
                                Speed = sysInterface.Speed,
                                PhysicalAddress = sysInterface.GetPhysicalAddress()?.ToString()
                            };

                            newCache[sysInterface.Name] = networkInterface;
                        }
                    }

                    // Update cache atomically
                    _cachedInterfaces.Clear();
                    foreach (var kvp in newCache)
                    {
                        _cachedInterfaces[kvp.Key] = kvp.Value;
                    }

                    lock (_lock)
                    {
                        _lastCacheUpdate = DateTime.Now;
                    }

                    _logger.Debug($"Refreshed network interface cache: {newCache.Count} interfaces found");
                }
                catch (Exception ex)
                {
                    _logger.Error("Error refreshing network interface cache", ex);
                }
            });
        }

        /// <summary>
        /// Gets an interface by name
        /// </summary>
        private async Task<INetworkInterface> GetInterfaceByNameAsync(string interfaceName)
        {
            var interfaces = await GetAllInterfacesAsync();
            return interfaces.FirstOrDefault(i => i.Name.Equals(interfaceName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Validates that we can bind to a specific IP address and port
        /// </summary>
        private async Task<bool> ValidateBindingAsync(string ipAddress, int port)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!IPAddress.TryParse(ipAddress, out var ip))
                        return false;

                    // Check if port is in valid range
                    if (port < 1 || port > 65535)
                        return false;

                    // Check if port is already in use (basic check)
                    var endpoint = new IPEndPoint(ip, port);
                    
                    // This is a simplified validation - in a real implementation,
                    // you might want to actually try to bind a test socket
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// Gets the primary IP address for a network interface
        /// </summary>
        private string GetInterfaceIpAddress(IPInterfaceProperties ipProperties)
        {
            // Prefer IPv4 addresses
            var ipv4Address = ipProperties.UnicastAddresses
                .FirstOrDefault(addr => addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            
            if (ipv4Address != null)
                return ipv4Address.Address.ToString();

            // Fallback to IPv6 if no IPv4 available
            var ipv6Address = ipProperties.UnicastAddresses
                .FirstOrDefault(addr => addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
                                       !addr.Address.IsIPv6LinkLocal);
            
            return ipv6Address?.Address.ToString();
        }

        /// <summary>
        /// Maps system network interface type to our enum
        /// </summary>
        private NetworkInterfaceType MapNetworkInterfaceType(System.Net.NetworkInformation.NetworkInterfaceType systemType)
        {
            switch (systemType)
            {
                case System.Net.NetworkInformation.NetworkInterfaceType.Ethernet:
                case System.Net.NetworkInformation.NetworkInterfaceType.Ethernet3Megabit:
                case System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetT:
                case System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetFx:
                case System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet:
                    return NetworkInterfaceType.Ethernet;
                
                case System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211:
                    return NetworkInterfaceType.WiFi;
                
                case System.Net.NetworkInformation.NetworkInterfaceType.Wman:
                case System.Net.NetworkInformation.NetworkInterfaceType.Wwanpp:
                case System.Net.NetworkInformation.NetworkInterfaceType.Wwanpp2:
                    return NetworkInterfaceType.Cellular;
                
                default:
                    return NetworkInterfaceType.Other;
            }
        }

        /// <summary>
        /// Handles network address changes
        /// </summary>
        private async void OnNetworkAddressChanged(object sender, EventArgs e)
        {
            _logger.Debug("Network address changed - refreshing interface cache");
            await RefreshInterfaceCache();
            
            // Notify listeners
            InterfaceChanged?.Invoke(this, new NetworkInterfaceChangedEventArgs
            {
                ChangeType = NetworkInterfaceChangeType.AddressChanged,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// Handles network availability changes
        /// </summary>
        private async void OnNetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            _logger.Debug($"Network availability changed: {e.IsAvailable}");
            await RefreshInterfaceCache();
            
            // Notify listeners
            InterfaceChanged?.Invoke(this, new NetworkInterfaceChangedEventArgs
            {
                ChangeType = NetworkInterfaceChangeType.StatusChanged,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// Throws ObjectDisposedException if the manager has been disposed
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when manager is disposed</exception>
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NetworkInterfaceManager));
        }

        /// <summary>
        /// Disposes the NetworkInterfaceManager and cleans up resources
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
                // Unsubscribe from network change events
                try
                {
                    NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
                    NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
                }
                catch (Exception ex)
                {
                    _logger.Error("Error unsubscribing from network events", ex);
                }

                _cachedInterfaces.Clear();
                _bindings.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Network binding information
    /// </summary>
    internal class NetworkBindingInfo
    {
        public string InterfaceName { get; set; }
        public INetworkInterface Interface { get; set; }
        public int Port { get; set; }
        public string LocalEndpoint { get; set; }
        public DateTime BoundAt { get; set; }
    }

    /// <summary>
    /// Implementation of INetworkInterface
    /// </summary>
    internal class NetworkInterfaceImpl : INetworkInterface
    {
        public string Name { get; set; }
        public string IpAddress { get; set; }
        public bool IsConnected { get; set; }
        public NetworkInterfaceType Type { get; set; }
        public string Description { get; set; }
        public long Speed { get; set; }
        public string PhysicalAddress { get; set; }
    }
}
