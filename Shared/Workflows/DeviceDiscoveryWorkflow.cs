using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Shared.Models;
using Shared.Platform;
using Shared.Http;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shared.Workflows
{
    /// <summary>
    /// Manages device discovery and connection workflows for the LocalTalk network.
    /// This class handles automatic device discovery, connection management, and maintains
    /// a list of available devices for file transfer operations.
    /// </summary>
    public class DeviceDiscoveryWorkflow : INotifyPropertyChanged
    {
        /// <summary>
        /// Singleton instance of the DeviceDiscoveryWorkflow.
        /// </summary>
        private static DeviceDiscoveryWorkflow _instance;

        /// <summary>
        /// Current state of the discovery process.
        /// </summary>
        private DiscoveryState _currentState;

        /// <summary>
        /// Current status message describing the discovery state.
        /// </summary>
        private string _statusMessage;

        /// <summary>
        /// Indicates whether device discovery is currently active.
        /// </summary>
        private bool _isDiscovering;

        /// <summary>
        /// Timer for periodic device discovery operations.
        /// </summary>
        private Timer _discoveryTimer;

        /// <summary>
        /// Timer for sending heartbeat messages to maintain connections.
        /// </summary>
        private Timer _heartbeatTimer;

        /// <summary>
        /// Gets the singleton instance of the DeviceDiscoveryWorkflow.
        /// </summary>
        public static DeviceDiscoveryWorkflow Instance => _instance ??= new DeviceDiscoveryWorkflow();

        /// <summary>
        /// Event raised when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Event raised when the discovery state changes.
        /// </summary>
        public event EventHandler<DiscoveryStateChangedEventArgs> StateChanged;

        /// <summary>
        /// Event raised when a new device is discovered on the network.
        /// </summary>
        public event EventHandler<DeviceDiscoveredEventArgs> DeviceDiscovered;

        /// <summary>
        /// Event raised when a previously discovered device is no longer available.
        /// </summary>
        public event EventHandler<DeviceLostEventArgs> DeviceLost;

        /// <summary>
        /// Event raised when a connection is successfully established with a device.
        /// </summary>
        public event EventHandler<ConnectionEstablishedEventArgs> ConnectionEstablished;

        /// <summary>
        /// Event raised when an error occurs during the discovery process.
        /// </summary>
        public event EventHandler<DiscoveryErrorEventArgs> ErrorOccurred;

        /// <summary>
        /// Gets the collection of currently discovered devices on the network.
        /// </summary>
        public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; }

        /// <summary>
        /// Gets the collection of active connections to other devices.
        /// </summary>
        public ObservableCollection<DeviceConnection> ActiveConnections { get; }

        /// <summary>
        /// Gets the current state of the device discovery process.
        /// </summary>
        public DiscoveryState CurrentState
        {
            get => _currentState;
            private set
            {
                if (_currentState != value)
                {
                    var oldState = _currentState;
                    _currentState = value;
                    OnPropertyChanged();
                    StateChanged?.Invoke(this, new DiscoveryStateChangedEventArgs(oldState, value));
                }
            }
        }

        /// <summary>
        /// Gets the current status message describing the discovery process state.
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Gets a value indicating whether device discovery is currently active.
        /// </summary>
        public bool IsDiscovering
        {
            get => _isDiscovering;
            private set
            {
                _isDiscovering = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Initializes a new instance of the DeviceDiscoveryWorkflow class.
        /// Sets up the collections and initial state for device discovery operations.
        /// </summary>
        private DeviceDiscoveryWorkflow()
        {
            DiscoveredDevices = new ObservableCollection<DiscoveredDevice>();
            ActiveConnections = new ObservableCollection<DeviceConnection>();
            CurrentState = DiscoveryState.Idle;
            StatusMessage = "Device discovery ready";
        }

        /// <summary>
        /// Starts the device discovery process asynchronously.
        /// Initializes network discovery, begins scanning for devices, and sets up periodic discovery operations.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation. The task result contains true if discovery started successfully; otherwise, false.</returns>
        public async Task<bool> StartDiscoveryAsync()
        {
            try
            {
                if (IsDiscovering)
                {
                    return true; // Already discovering
                }

                CurrentState = DiscoveryState.Starting;
                StatusMessage = "Starting device discovery...";

                // Initialize network discovery
                if (!await InitializeNetworkDiscoveryAsync())
                {
                    CurrentState = DiscoveryState.Error;
                    return false;
                }

                // Start periodic discovery
                StartPeriodicDiscovery();

                // Start device heartbeat monitoring
                StartHeartbeatMonitoring();

                IsDiscovering = true;
                CurrentState = DiscoveryState.Discovering;
                StatusMessage = "Discovering devices on network...";

                return true;
            }
            catch (Exception ex)
            {
                HandleError("Failed to start discovery", ex);
                return false;
            }
        }

        public async Task<bool> StopDiscoveryAsync()
        {
            try
            {
                CurrentState = DiscoveryState.Stopping;
                StatusMessage = "Stopping device discovery...";

                // Stop timers
                _discoveryTimer?.Dispose();
                _heartbeatTimer?.Dispose();

                // Clear discovered devices
                DiscoveredDevices.Clear();

                // Close active connections
                await CloseAllConnectionsAsync();

                IsDiscovering = false;
                CurrentState = DiscoveryState.Idle;
                StatusMessage = "Device discovery stopped";

                return true;
            }
            catch (Exception ex)
            {
                HandleError("Failed to stop discovery", ex);
                return false;
            }
        }

        public async Task<DeviceConnection> EstablishConnectionAsync(DiscoveredDevice device)
        {
            try
            {
                StatusMessage = $"Connecting to {device.Device.alias}...";

                // Check if already connected
                var existingConnection = ActiveConnections.FirstOrDefault(c => c.Device.ip == device.Device.ip);
                if (existingConnection != null)
                {
                    return existingConnection;
                }

                // Attempt connection
                var connection = await AttemptConnectionAsync(device);
                if (connection != null)
                {
                    ActiveConnections.Add(connection);
                    device.ConnectionStatus = DeviceConnectionStatus.Connected;
                    device.LastSeen = DateTime.Now;

                    StatusMessage = $"Connected to {device.Device.alias}";
                    ConnectionEstablished?.Invoke(this, new ConnectionEstablishedEventArgs(connection));

                    return connection;
                }
                else
                {
                    device.ConnectionStatus = DeviceConnectionStatus.Failed;
                    StatusMessage = $"Failed to connect to {device.Device.alias}";
                    return null;
                }
            }
            catch (Exception ex)
            {
                device.ConnectionStatus = DeviceConnectionStatus.Failed;
                HandleError($"Connection failed to {device.Device.alias}", ex);
                return null;
            }
        }

        public async Task<bool> DisconnectDeviceAsync(DeviceConnection connection)
        {
            try
            {
                StatusMessage = $"Disconnecting from {connection.Device.alias}...";

                // Close the connection
                await connection.CloseAsync();

                // Remove from active connections
                ActiveConnections.Remove(connection);

                // Update device status
                var device = DiscoveredDevices.FirstOrDefault(d => d.Device.ip == connection.Device.ip);
                if (device != null)
                {
                    device.ConnectionStatus = DeviceConnectionStatus.Available;
                }

                StatusMessage = $"Disconnected from {connection.Device.alias}";
                return true;
            }
            catch (Exception ex)
            {
                HandleError($"Failed to disconnect from {connection.Device.alias}", ex);
                return false;
            }
        }

        public async Task<bool> RefreshDiscoveryAsync()
        {
            try
            {
                StatusMessage = "Refreshing device discovery...";

                // Perform immediate discovery
                await PerformDiscoveryAsync();

                StatusMessage = "Discovery refreshed";
                return true;
            }
            catch (Exception ex)
            {
                HandleError("Failed to refresh discovery", ex);
                return false;
            }
        }

        private async Task<bool> InitializeNetworkDiscoveryAsync()
        {
            try
            {
                // Initialize LocalSend protocol if not already started
                if (LocalSendProtocol.Instance == null)
                {
                    var protocol = new LocalSendProtocol();
                    await protocol.Start();
                }

                return true;
            }
            catch (Exception ex)
            {
                HandleError("Failed to initialize network discovery", ex);
                return false;
            }
        }

        private void StartPeriodicDiscovery()
        {
            // Discover devices every 10 seconds
            _discoveryTimer = new Timer(async _ => await PerformDiscoveryAsync(),
                null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        }

        private void StartHeartbeatMonitoring()
        {
            // Check device heartbeats every 30 seconds
            _heartbeatTimer = new Timer(CheckDeviceHeartbeats,
                null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        private async Task PerformDiscoveryAsync()
        {
            try
            {
                // Send UDP multicast discovery
                await SendDiscoveryBroadcastAsync();

                // Scan for HTTP servers on common ports
                await ScanForHttpServersAsync();
            }
            catch (Exception ex)
            {
                HandleError("Discovery scan failed", ex);
            }
        }

        private async Task SendDiscoveryBroadcastAsync()
        {
            try
            {
                // In a real implementation, this would send UDP multicast
                // For now, simulate discovery by adding some test devices
                await Task.Delay(100);

                // Simulate discovering devices (in real implementation, this would parse UDP responses)
                var simulatedDevices = new[]
                {
                    new Device { alias = "Test Device 1", deviceType = "mobile", ip = "192.168.1.100", port = 53317 },
                    new Device { alias = "Test Device 2", deviceType = "desktop", ip = "192.168.1.101", port = 53317 }
                };

                foreach (var device in simulatedDevices)
                {
                    await ProcessDiscoveredDeviceAsync(device);
                }
            }
            catch (Exception ex)
            {
                HandleError("UDP broadcast failed", ex);
            }
        }

        private async Task ScanForHttpServersAsync()
        {
            try
            {
                // In a real implementation, this would scan IP ranges for LocalSend HTTP servers
                await Task.Delay(50);
            }
            catch (Exception ex)
            {
                HandleError("HTTP scan failed", ex);
            }
        }

        private async Task ProcessDiscoveredDeviceAsync(Device device)
        {
            try
            {
                var existingDevice = DiscoveredDevices.FirstOrDefault(d => d.Device.ip == device.ip);

                if (existingDevice == null)
                {
                    // New device discovered
                    var discoveredDevice = new DiscoveredDevice
                    {
                        Device = device,
                        FirstSeen = DateTime.Now,
                        LastSeen = DateTime.Now,
                        ConnectionStatus = DeviceConnectionStatus.Available,
                        SignalStrength = CalculateSignalStrength(device)
                    };

                    PlatformFactory.Current.RunOnUIThread(() =>
                    {
                        DiscoveredDevices.Add(discoveredDevice);
                    });

                    DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(discoveredDevice));
                }
                else
                {
                    // Update existing device
                    existingDevice.LastSeen = DateTime.Now;
                    existingDevice.SignalStrength = CalculateSignalStrength(device);
                }
            }
            catch (Exception ex)
            {
                HandleError($"Failed to process discovered device {device.alias}", ex);
            }
        }

        private void CheckDeviceHeartbeats(object state)
        {
            try
            {
                var now = DateTime.Now;
                var timeout = TimeSpan.FromMinutes(2); // Consider devices lost after 2 minutes

                var lostDevices = DiscoveredDevices
                    .Where(d => now - d.LastSeen > timeout)
                    .ToList();

                foreach (var lostDevice in lostDevices)
                {
                    PlatformFactory.Current.RunOnUIThread(() =>
                    {
                        DiscoveredDevices.Remove(lostDevice);
                    });

                    DeviceLost?.Invoke(this, new DeviceLostEventArgs(lostDevice));
                }
            }
            catch (Exception ex)
            {
                HandleError("Heartbeat check failed", ex);
            }
        }

        private async Task<DeviceConnection> AttemptConnectionAsync(DiscoveredDevice device)
        {
            try
            {
                // Test HTTP connection to device
                var httpClient = new LocalSendHttpClient();
                var isReachable = await httpClient.TestConnectionAsync(device.Device.ip, device.Device.port);

                if (isReachable)
                {
                    return new DeviceConnection
                    {
                        Device = device.Device,
                        EstablishedAt = DateTime.Now,
                        IsActive = true,
                        HttpClient = httpClient
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                HandleError($"Connection attempt failed to {device.Device.alias}", ex);
                return null;
            }
        }

        private async Task CloseAllConnectionsAsync()
        {
            var connections = ActiveConnections.ToList();
            foreach (var connection in connections)
            {
                await DisconnectDeviceAsync(connection);
            }
        }

        private int CalculateSignalStrength(Device device)
        {
            // Simple heuristic for signal strength (1-4 bars)
            // In a real implementation, this could be based on network latency
            return device.deviceType switch
            {
                "mobile" => 3,
                "desktop" => 4,
                "web" => 2,
                _ => 2
            };
        }

        private void HandleError(string message, Exception exception)
        {
            CurrentState = DiscoveryState.Error;
            StatusMessage = message;
            ErrorOccurred?.Invoke(this, new DiscoveryErrorEventArgs(message, exception));
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum DiscoveryState
    {
        Idle,
        Starting,
        Discovering,
        Stopping,
        Error
    }

    public class DiscoveredDevice
    {
        public Device Device { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public DeviceConnectionStatus ConnectionStatus { get; set; }
        public int SignalStrength { get; set; } // 1-4 bars
    }

    public class DeviceConnection
    {
        public Device Device { get; set; }
        public DateTime EstablishedAt { get; set; }
        public bool IsActive { get; set; }
        public LocalSendHttpClient HttpClient { get; set; }

        public async Task CloseAsync()
        {
            IsActive = false;
            HttpClient?.Dispose();
        }
    }

    // Event argument classes
    public class DiscoveryStateChangedEventArgs : EventArgs
    {
        public DiscoveryState OldState { get; }
        public DiscoveryState NewState { get; }

        public DiscoveryStateChangedEventArgs(DiscoveryState oldState, DiscoveryState newState)
        {
            OldState = oldState;
            NewState = newState;
        }
    }

    public class DeviceDiscoveredEventArgs : EventArgs
    {
        public DiscoveredDevice Device { get; }

        public DeviceDiscoveredEventArgs(DiscoveredDevice device)
        {
            Device = device;
        }
    }

    public class DeviceLostEventArgs : EventArgs
    {
        public DiscoveredDevice Device { get; }

        public DeviceLostEventArgs(DiscoveredDevice device)
        {
            Device = device;
        }
    }

    public class ConnectionEstablishedEventArgs : EventArgs
    {
        public DeviceConnection Connection { get; }

        public ConnectionEstablishedEventArgs(DeviceConnection connection)
        {
            Connection = connection;
        }
    }

    public class DiscoveryErrorEventArgs : EventArgs
    {
        public string Message { get; }
        public Exception Exception { get; }

        public DiscoveryErrorEventArgs(string message, Exception exception)
        {
            Message = message;
            Exception = exception;
        }
    }
}
