using Shared.Models;
using Shared.Platform;
using Shared.Http;
using Shared.Security;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#if WINDOWS_UWP
using Windows.UI.Xaml.Controls;
#endif

namespace Shared
{
    public class LocalSendProtocol
    {
        #region Constants
        /// <summary>
        /// LocalSend protocol version.
        /// </summary>
        private const double ProtocolVersion = 2.0;

        /// <summary>
        /// Length of the device fingerprint string.
        /// </summary>
        private const int FingerprintLength = 30;
        #endregion

        public static LocalSendProtocol Instance { get; private set; }
        private static Device _thisDevice;

        public static Device ThisDevice
        {
            get
            {
                if (_thisDevice.Equals(default(Device)))
                {
                    var platform = PlatformFactory.Current;
                    var crypto = platform.GetCryptographyProvider();

                    _thisDevice = new Device
                    {
                        alias = Settings.DeviceName,
                        version = ProtocolVersion,
                        deviceModel = platform.GetDeviceModel(),
                        deviceType = platform.GetDeviceType(),
                        fingerprint = crypto.GenerateRandomString(FingerprintLength),
                            // TODO: Cert hash w/ HTTPS enabled
                        port = Settings.Port,
                        protocol = "https",
                        download = true,
                        announce = true
                    };
                }
                return _thisDevice;
            }
        }

        public static readonly ObservableCollection<Device> Devices
            = new ObservableCollection<Device>();

        private IUdpSocket _udpSocket;
        private LocalSendHttpServer _httpServer;

        // Performance optimization: Use HashSet for O(1) device fingerprint lookups
        private readonly HashSet<string> _knownDeviceFingerprints = new HashSet<string>();

        public LocalSendProtocol()
        {
            Instance = this;
        }

        /// <summary>
        /// Starts the LocalSend protocol services
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the protocol is already started or platform requirements are not met</exception>
        /// <exception cref="NotSupportedException">Thrown when required platform features are not available</exception>
        public async Task Start()
        {
            if (_httpServer != null || _udpSocket != null)
                throw new InvalidOperationException("LocalSend protocol is already started");

            if (Settings.Port <= 0 || Settings.Port > 65535)
                throw new ArgumentOutOfRangeException(nameof(Settings.Port),
                    $"Port {Settings.Port} is not valid. Port must be between 1 and 65535");

            try
            {
                // Start HTTP server if supported
                if (PlatformFactory.Features.SupportsHttpServer)
                {
                    _httpServer = new LocalSendHttpServer();
                    await _httpServer.StartAsync(Settings.Port, true);
                    System.Diagnostics.Debug.WriteLine($"HTTP server started on port {Settings.Port}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("HTTP server not supported on this platform");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start HTTP server: {ex.Message}");
                // Clean up any partially initialized resources
                if (_httpServer != null)
                {
                    try
                    {
                        await _httpServer.StopAsync();
                        _httpServer.Dispose();
                    }
                    catch (Exception cleanupEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error during HTTP server cleanup: {cleanupEx.Message}");
                    }
                    _httpServer = null;
                }
                throw new InvalidOperationException($"Failed to start HTTP server: {ex.Message}", ex);
            }

            try
            {
                // Start UDP multicast discovery if supported
                if (PlatformFactory.Features.SupportsMulticastUdp)
                {
                    if (string.IsNullOrWhiteSpace(Settings.Address))
                        throw new InvalidOperationException("Multicast address is not configured");

                    var platform = PlatformFactory.Current;
                    if (platform == null)
                        throw new InvalidOperationException("Platform factory is not initialized");

                    _udpSocket = platform.CreateUdpSocket();
                    if (_udpSocket == null)
                        throw new InvalidOperationException("Failed to create UDP socket");

                    _udpSocket.MessageReceived += OnUdpMessageReceived;

                    await _udpSocket.BindAsync(Settings.Port);
                    await _udpSocket.JoinMulticastGroupAsync(Settings.Address);

                    // Announce this device
                    var deviceJson = Internet.SerializeObject(ThisDevice);
                    if (string.IsNullOrEmpty(deviceJson))
                        throw new InvalidOperationException("Failed to serialize device information");

                    var data = System.Text.Encoding.UTF8.GetBytes(deviceJson);
                    await _udpSocket.SendAsync(data, Settings.Address, Settings.Port);

                    System.Diagnostics.Debug.WriteLine("UDP multicast discovery started");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("UDP multicast not supported on this platform");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start UDP multicast discovery: {ex.Message}");

                // Clean up UDP socket if initialization failed
                if (_udpSocket != null)
                {
                    try
                    {
                        _udpSocket.Dispose();
                    }
                    catch (Exception cleanupEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error during UDP socket cleanup: {cleanupEx.Message}");
                    }
                    _udpSocket = null;
                }

                // If HTTP server also failed, this is a critical error
                if (_httpServer == null)
                {
                    throw new InvalidOperationException($"Failed to start both HTTP server and UDP discovery: {ex.Message}", ex);
                }

                // Otherwise, continue with just HTTP server
                System.Diagnostics.Debug.WriteLine("Continuing with HTTP server only");
            }

            // Update device fingerprint with certificate if HTTPS is enabled
            if (_httpServer != null && _httpServer.UseHttps)
            {
                try
                {
                    var fingerprint = await CertificateManager.Instance.GetServerFingerprintAsync();
                    _thisDevice.fingerprint = fingerprint;
                    System.Diagnostics.Debug.WriteLine($"Updated device fingerprint: {fingerprint}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to get certificate fingerprint: {ex.Message}");
                }
            }
        }

        private async void OnUdpMessageReceived(object sender, UdpMessageReceivedEventArgs args)
        {
            // Process UDP messages asynchronously to avoid blocking the UDP socket
            _ = Task.Run(async () =>
            {
                try
                {
                    // Validate message data
                    if (args?.Data == null || args.Data.Length == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("Received empty UDP message");
                        return;
                    }

                    // Use more efficient string conversion with span
                    var message = System.Text.Encoding.UTF8.GetString(args.Data.AsSpan());
                    System.Diagnostics.Debug.WriteLine($"Received UDP message: {message}");

                    // Early validation to avoid expensive deserialization
                    if (string.IsNullOrWhiteSpace(message) || !message.Contains("alias"))
                    {
                        System.Diagnostics.Debug.WriteLine("Invalid UDP message format");
                        return;
                    }

                    var device = Internet.DeserializeObject<Device>(message);
                    if (device == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to deserialize device from UDP message");
                        return;
                    }

                    // Use more efficient device comparison with HashSet for O(1) lookup
                    var deviceExists = _knownDeviceFingerprints.Contains(device.fingerprint);
                    var isSelfDevice = device.fingerprint == ThisDevice.fingerprint;

                    if (!deviceExists && !isSelfDevice)
                    {
                        // Cache the device fingerprint for faster future lookups
                        _knownDeviceFingerprints.Add(device.fingerprint);

                        // Add device to collection on UI thread with lower priority to avoid blocking
#if WINDOWS_UWP
                        await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                            Windows.UI.Core.CoreDispatcherPriority.Normal, () => Devices.Add(device));
#elif WINDOWS_PHONE
                        System.Windows.Deployment.Current.Dispatcher.BeginInvoke(() => Devices.Add(device));
#else
                        // For other platforms, add directly (assuming thread-safe collection)
                        Devices.Add(device);
#endif
                        System.Diagnostics.Debug.WriteLine($"Added new device: {device.alias}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error processing UDP message: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Generates a unique key of specified size
        /// </summary>
        /// <param name="size">The size of the key to generate</param>
        /// <returns>A unique random string</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when size is not positive</exception>
        /// <exception cref="InvalidOperationException">Thrown when platform or crypto provider is not available</exception>
        public static string GetUniqueKey(int size)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size), "Size must be greater than zero");

            if (size > 1024)
                throw new ArgumentOutOfRangeException(nameof(size), "Size cannot exceed 1024 characters for security reasons");

            try
            {
                var platform = PlatformFactory.Current;
                if (platform == null)
                    throw new InvalidOperationException("Platform factory is not initialized");

                var crypto = platform.GetCryptographyProvider();
                if (crypto == null)
                    throw new InvalidOperationException("Cryptography provider is not available");

                var result = crypto.GenerateRandomString(size);
                if (string.IsNullOrEmpty(result))
                    throw new InvalidOperationException("Failed to generate random string");

                return result;
            }
            catch (Exception ex) when (!(ex is ArgumentOutOfRangeException || ex is InvalidOperationException))
            {
                throw new InvalidOperationException($"Failed to generate unique key: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Stops the LocalSend protocol services
        /// </summary>
        public async Task Stop()
        {
            var exceptions = new List<Exception>();

            // Stop HTTP server
            if (_httpServer != null)
            {
                try
                {
                    await _httpServer.StopAsync();
                    _httpServer.Dispose();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    System.Diagnostics.Debug.WriteLine($"Error stopping HTTP server: {ex.Message}");
                }
                finally
                {
                    _httpServer = null;
                }
            }

            // Stop UDP socket
            if (_udpSocket != null)
            {
                try
                {
                    _udpSocket.Dispose();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    System.Diagnostics.Debug.WriteLine($"Error disposing UDP socket: {ex.Message}");
                }
                finally
                {
                    _udpSocket = null;
                }
            }

            // If there were any exceptions during shutdown, throw an aggregate exception
            if (exceptions.Count > 0)
            {
                throw new AggregateException("One or more errors occurred while stopping LocalSend protocol", exceptions);
            }
        }

        public void Dispose()
        {
            try
            {
                // Use ConfigureAwait(false) to avoid deadlocks
                Stop().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during LocalSendProtocol disposal: {ex.Message}");
            }
        }
    }
}
