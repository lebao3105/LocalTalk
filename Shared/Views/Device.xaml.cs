using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Shared.Models;
using Shared.Platform;
using Shared.Http;

#if WINDOWS_PHONE
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
#else
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
#endif

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Shared.Views
{
    /// <summary>
    /// Represents a device user control that displays device information and provides interaction capabilities.
    /// This control shows device details, connection status, signal strength, and allows users to connect to or select devices.
    /// </summary>
    public sealed partial class Device : UserControl, INotifyPropertyChanged
    {
        /// <summary>
        /// The device information associated with this control.
        /// </summary>
        private Models.Device _deviceInfo;

        /// <summary>
        /// Indicates whether this device is currently selected.
        /// </summary>
        private bool _isSelected;

        /// <summary>
        /// Indicates whether a connection attempt is in progress.
        /// </summary>
        private bool _isConnecting;

        /// <summary>
        /// The current connection status of the device.
        /// </summary>
        private DeviceConnectionStatus _connectionStatus;

        /// <summary>
        /// Event raised when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Event raised when the device is selected or deselected.
        /// </summary>
        public event EventHandler<DeviceSelectionEventArgs> DeviceSelected;

        /// <summary>
        /// Event raised when a connection action is requested (connect, cancel, send, retry).
        /// </summary>
        public event EventHandler<DeviceConnectionEventArgs> ConnectionRequested;

        /// <summary>
        /// Initializes a new instance of the Device user control.
        /// Sets up the data context and initial connection status display.
        /// </summary>
        public Device()
        {
            this.InitializeComponent();
            this.DataContext = this;
            _connectionStatus = DeviceConnectionStatus.Available;
            UpdateConnectionDisplay();
        }

        /// <summary>
        /// Gets or sets the device information displayed by this control.
        /// When set, updates the device display to reflect the new information.
        /// </summary>
        public Models.Device DeviceInfo
        {
            get => _deviceInfo;
            set
            {
                _deviceInfo = value;
                OnPropertyChanged();
                UpdateDeviceDisplay();
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this device is currently selected.
        /// When changed, updates the visual selection display.
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
                UpdateSelectionDisplay();
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether a connection attempt is currently in progress.
        /// When changed, updates the connection display to reflect the connecting state.
        /// </summary>
        public bool IsConnecting
        {
            get => _isConnecting;
            set
            {
                _isConnecting = value;
                OnPropertyChanged();
                UpdateConnectionDisplay();
            }
        }

        /// <summary>
        /// Gets or sets the current connection status of the device.
        /// When changed, updates the visual connection status display and available actions.
        /// </summary>
        public DeviceConnectionStatus ConnectionStatus
        {
            get => _connectionStatus;
            set
            {
                _connectionStatus = value;
                OnPropertyChanged();
                UpdateConnectionDisplay();
            }
        }

        private void UpdateDeviceDisplay()
        {
            if (_deviceInfo.Equals(default(Models.Device))) return;

            PlatformFactory.Current.RunOnUIThread(() =>
            {
                // Device info is bound via DataContext, but we can add additional logic here
                UpdateSignalStrength();
            });
        }

        private void UpdateSelectionDisplay()
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                if (_isSelected)
                {
#if WINDOWS_UWP
                    DeviceBorder.Background = new SolidColorBrush(Colors.LightBlue);
                    DeviceBorder.BorderBrush = new SolidColorBrush(Colors.Blue);
                    DeviceBorder.BorderThickness = new Thickness(2);
#elif WINDOWS_PHONE
                    DeviceBorder.Background = new SolidColorBrush(System.Windows.Media.Colors.LightBlue);
                    DeviceBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Colors.Blue);
                    DeviceBorder.BorderThickness = new System.Windows.Thickness(2);
#endif
                }
                else
                {
#if WINDOWS_UWP
                    DeviceBorder.Background = new SolidColorBrush(Colors.Transparent);
                    DeviceBorder.BorderBrush = new SolidColorBrush(Colors.Gray);
                    DeviceBorder.BorderThickness = new Thickness(1);
#elif WINDOWS_PHONE
                    DeviceBorder.Background = new SolidColorBrush(System.Windows.Media.Colors.Transparent);
                    DeviceBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Colors.Gray);
                    DeviceBorder.BorderThickness = new System.Windows.Thickness(1);
#endif
                }
            });
        }

        private void UpdateConnectionDisplay()
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                switch (_connectionStatus)
                {
                    case DeviceConnectionStatus.Available:
#if WINDOWS_UWP
                        StatusIndicator.Fill = new SolidColorBrush(Colors.Green);
#elif WINDOWS_PHONE
                        StatusIndicator.Fill = new SolidColorBrush(System.Windows.Media.Colors.Green);
#endif
                        ConnectionStatus.Text = "Available";
                        ActionButton.Content = "Connect";
                        ActionButton.IsEnabled = !_isConnecting;
                        break;

                    case DeviceConnectionStatus.Connecting:
#if WINDOWS_UWP
                        StatusIndicator.Fill = new SolidColorBrush(Colors.Orange);
#elif WINDOWS_PHONE
                        StatusIndicator.Fill = new SolidColorBrush(System.Windows.Media.Colors.Orange);
#endif
                        ConnectionStatus.Text = "Connecting...";
                        ActionButton.Content = "Cancel";
                        ActionButton.IsEnabled = true;
                        break;

                    case DeviceConnectionStatus.Connected:
#if WINDOWS_UWP
                        StatusIndicator.Fill = new SolidColorBrush(Colors.Blue);
#elif WINDOWS_PHONE
                        StatusIndicator.Fill = new SolidColorBrush(System.Windows.Media.Colors.Blue);
#endif
                        ConnectionStatus.Text = "Connected";
                        ActionButton.Content = "Send";
                        ActionButton.IsEnabled = true;
                        break;

                    case DeviceConnectionStatus.Unavailable:
#if WINDOWS_UWP
                        StatusIndicator.Fill = new SolidColorBrush(Colors.Red);
#elif WINDOWS_PHONE
                        StatusIndicator.Fill = new SolidColorBrush(System.Windows.Media.Colors.Red);
#endif
                        ConnectionStatus.Text = "Unavailable";
                        ActionButton.Content = "Retry";
                        ActionButton.IsEnabled = true;
                        break;

                    case DeviceConnectionStatus.Error:
#if WINDOWS_UWP
                        StatusIndicator.Fill = new SolidColorBrush(Colors.Red);
#elif WINDOWS_PHONE
                        StatusIndicator.Fill = new SolidColorBrush(System.Windows.Media.Colors.Red);
#endif
                        ConnectionStatus.Text = "Error";
                        ActionButton.Content = "Retry";
                        ActionButton.IsEnabled = true;
                        break;
                }
            });
        }

        private void UpdateSignalStrength()
        {
            // Simulate signal strength based on device type and other factors
            // In a real implementation, this could be based on network latency or other metrics
            var strength = GetSignalStrength();

            var signals = new[] { Signal1, Signal2, Signal3, Signal4 };
#if WINDOWS_UWP
            var activeColor = new SolidColorBrush(Colors.Green);
            var inactiveColor = new SolidColorBrush(Colors.Gray);
#elif WINDOWS_PHONE
            var activeColor = new SolidColorBrush(System.Windows.Media.Colors.Green);
            var inactiveColor = new SolidColorBrush(System.Windows.Media.Colors.Gray);
#endif

            for (int i = 0; i < signals.Length; i++)
            {
                signals[i].Fill = i < strength ? activeColor : inactiveColor;
            }
        }

        private int GetSignalStrength()
        {
            // Simple heuristic for signal strength
            if (_deviceInfo.deviceType == "mobile") return 3;
            if (_deviceInfo.deviceType == "desktop") return 4;
            if (_deviceInfo.deviceType == "web") return 2;
            return 2;
        }

        private void OnDeviceTapped(object sender, TappedRoutedEventArgs e)
        {
            IsSelected = !IsSelected;
            DeviceSelected?.Invoke(this, new DeviceSelectionEventArgs
            {
                Device = _deviceInfo,
                IsSelected = _isSelected
            });
        }

        private async void OnActionButtonClick(object sender, RoutedEventArgs e)
        {
            switch (_connectionStatus)
            {
                case DeviceConnectionStatus.Available:
                    ConnectionStatus = DeviceConnectionStatus.Connecting;
                    ConnectionRequested?.Invoke(this, new DeviceConnectionEventArgs
                    {
                        Device = _deviceInfo,
                        Action = ConnectionAction.Connect
                    });
                    break;

                case DeviceConnectionStatus.Connecting:
                    ConnectionStatus = DeviceConnectionStatus.Available;
                    ConnectionRequested?.Invoke(this, new DeviceConnectionEventArgs
                    {
                        Device = _deviceInfo,
                        Action = ConnectionAction.Cancel
                    });
                    break;

                case DeviceConnectionStatus.Connected:
                    ConnectionRequested?.Invoke(this, new DeviceConnectionEventArgs
                    {
                        Device = _deviceInfo,
                        Action = ConnectionAction.Send
                    });
                    break;

                case DeviceConnectionStatus.Unavailable:
                case DeviceConnectionStatus.Error:
                    ConnectionStatus = DeviceConnectionStatus.Connecting;
                    ConnectionRequested?.Invoke(this, new DeviceConnectionEventArgs
                    {
                        Device = _deviceInfo,
                        Action = ConnectionAction.Retry
                    });
                    break;
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Defines the possible connection states for a device.
    /// </summary>
    public enum DeviceConnectionStatus
    {
        /// <summary>
        /// Device is available and ready for connection.
        /// </summary>
        Available,

        /// <summary>
        /// Connection attempt is currently in progress.
        /// </summary>
        Connecting,

        /// <summary>
        /// Device is successfully connected and ready for file transfer.
        /// </summary>
        Connected,

        /// <summary>
        /// Device is currently unavailable for connection.
        /// </summary>
        Unavailable,

        /// <summary>
        /// An error occurred during connection or communication.
        /// </summary>
        Error
    }

    /// <summary>
    /// Defines the possible connection actions that can be performed on a device.
    /// </summary>
    public enum ConnectionAction
    {
        /// <summary>
        /// Initiate a new connection to the device.
        /// </summary>
        Connect,

        /// <summary>
        /// Cancel an ongoing connection attempt.
        /// </summary>
        Cancel,

        /// <summary>
        /// Send files to the connected device.
        /// </summary>
        Send,

        /// <summary>
        /// Retry a failed connection attempt.
        /// </summary>
        Retry
    }

    /// <summary>
    /// Provides data for device selection events.
    /// </summary>
    public class DeviceSelectionEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the device that was selected or deselected.
        /// </summary>
        public Models.Device Device { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the device is now selected.
        /// </summary>
        public bool IsSelected { get; set; }
    }

    /// <summary>
    /// Provides data for device connection events.
    /// </summary>
    public class DeviceConnectionEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the device for which the connection action is requested.
        /// </summary>
        public Models.Device Device { get; set; }

        /// <summary>
        /// Gets or sets the connection action being requested.
        /// </summary>
        public ConnectionAction Action { get; set; }
    }
}
