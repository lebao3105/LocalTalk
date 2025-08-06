using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Shared.FileSystem;
using Shared.Platform;
using Shared.Workflows;

#if WINDOWS_PHONE
using System.Windows.Controls;
#else
using Windows.UI.Xaml.Controls;
#endif
#endif

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Shared.Views
{
    public sealed partial class SendPage : UserControl
    {
        private UniversalFilePicker _filePicker;
        private List<Device> _deviceControls;
        private Device _selectedDevice;
        private SendFileWorkflow _workflow;
        private DeviceDiscoveryWorkflow _discoveryWorkflow;

        public SendPage()
        {
            this.InitializeComponent();
            Loaded += SendPage_Loaded;
            Unloaded += SendPage_Unloaded;
            _filePicker = UniversalFilePicker.Instance;
            _deviceControls = new List<Device>();
            _workflow = SendFileWorkflow.Instance;
            _discoveryWorkflow = DeviceDiscoveryWorkflow.Instance;

            // Subscribe to file picker events
            _filePicker.FileSelected += OnFileSelected;
            _filePicker.FilesSelected += OnFilesSelected;
            _filePicker.ErrorOccurred += OnFilePickerError;

            // Subscribe to device collection changes
            LocalSendProtocol.Devices.CollectionChanged += OnDevicesCollectionChanged;

            // Subscribe to workflow events
            _workflow.StateChanged += OnWorkflowStateChanged;
            _workflow.ProgressUpdated += OnWorkflowProgressUpdated;
            _workflow.ErrorOccurred += OnWorkflowErrorOccurred;

            // Subscribe to discovery workflow events
            _discoveryWorkflow.DeviceDiscovered += OnDeviceDiscovered;
            _discoveryWorkflow.DeviceLost += OnDeviceLost;
            _discoveryWorkflow.ConnectionEstablished += OnConnectionEstablished;
            _discoveryWorkflow.ErrorOccurred += OnDiscoveryErrorOccurred;
        }

        private async void SendPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize LocalSend protocol if not already started
            if (LocalSendProtocol.Instance == null)
            {
                var protocol = new LocalSendProtocol();
                _ = protocol.Start();
            }

            // Start device discovery
            await _discoveryWorkflow.StartDiscoveryAsync();

            // Initialize device list
            RefreshDeviceList();
        }

        private void SendPage_Unloaded(object sender, RoutedEventArgs e)
        {
            // Unsubscribe from all events to prevent memory leaks
            if (_filePicker != null)
            {
                _filePicker.FileSelected -= OnFileSelected;
                _filePicker.FilesSelected -= OnFilesSelected;
                _filePicker.ErrorOccurred -= OnFilePickerError;
            }

            if (LocalSendProtocol.Devices != null)
            {
                LocalSendProtocol.Devices.CollectionChanged -= OnDevicesCollectionChanged;
            }

            if (_workflow != null)
            {
                _workflow.StateChanged -= OnWorkflowStateChanged;
                _workflow.ProgressUpdated -= OnWorkflowProgressUpdated;
                _workflow.ErrorOccurred -= OnWorkflowErrorOccurred;
            }

            if (_discoveryWorkflow != null)
            {
                _discoveryWorkflow.DeviceDiscovered -= OnDeviceDiscovered;
                _discoveryWorkflow.DeviceLost -= OnDeviceLost;
                _discoveryWorkflow.ConnectionEstablished -= OnConnectionEstablished;
                _discoveryWorkflow.ErrorOccurred -= OnDiscoveryErrorOccurred;
            }

            // Clean up device controls
            foreach (var deviceControl in _deviceControls)
            {
                deviceControl.DeviceSelected -= OnDeviceSelected;
                deviceControl.ConnectionRequested -= OnDeviceConnectionRequested;
            }
            _deviceControls.Clear();
        }

        private async void OnFileButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var options = new FilePickerOptions
                {
                    AllowMultipleSelection = true,
                    Title = "Select files to send"
                };

                var result = await _filePicker.PickMultipleFilesAsync(options);

                if (result.Success && result.SelectedFiles?.Any() == true)
                {
                    // Add selected files to pending uploads
                    foreach (var fileInfo in result.SelectedFiles)
                    {
                        AddFileToPendingUploads(fileInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError($"Failed to pick files: {ex.Message}");
            }
        }

        private async void OnMediaButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var options = new FilePickerOptions
                {
                    FileTypeFilters = new List<string> { ".jpg", ".jpeg", ".png", ".gif", ".mp4", ".mov", ".avi" },
                    AllowMultipleSelection = true,
                    Title = "Select media files to send"
                };

                var result = await _filePicker.PickMultipleFilesAsync(options);

                if (result.Success && result.SelectedFiles?.Any() == true)
                {
                    foreach (var fileInfo in result.SelectedFiles)
                    {
                        AddFileToPendingUploads(fileInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError($"Failed to pick media files: {ex.Message}");
            }
        }

        private async void OnFolderButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = await _filePicker.PickFolderAsync();

                if (result.Success && result.SelectedFolder != null)
                {
                    // Add folder to pending uploads (implementation would need folder handling)
                    ShowInfo($"Selected folder: {result.SelectedFolder.Folder.Name}");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Failed to pick folder: {ex.Message}");
            }
        }

        private void OnFileSelected(object sender, FilePickerEventArgs e)
        {
            if (e.Result.Success && e.Result.SelectedFile != null)
            {
                AddFileToPendingUploads(e.Result.SelectedFile);
            }
        }

        private void OnFilesSelected(object sender, FilePickerEventArgs e)
        {
            if (e.Result.Success && e.Result.SelectedFiles?.Any() == true)
            {
                foreach (var fileInfo in e.Result.SelectedFiles)
                {
                    AddFileToPendingUploads(fileInfo);
                }
            }
        }

        private void OnFilePickerError(object sender, FilePickerErrorEventArgs e)
        {
            ShowError($"File picker error: {e.Message}");
        }

        private async void AddFileToPendingUploads(SecureFileInfo fileInfo)
        {
            await _workflow.AddFilesAsync(new[] { fileInfo });
            UpdatePendingUploadsDisplay();
            System.Diagnostics.Debug.WriteLine($"Added file to workflow: {fileInfo.File.Name}");
        }

        private void ShowError(string message)
        {
            // TODO: Implement proper error display
            System.Diagnostics.Debug.WriteLine($"Error: {message}");
        }

        private void ShowInfo(string message)
        {
            // TODO: Implement proper info display
            System.Diagnostics.Debug.WriteLine($"Info: {message}");
        }

        private void OnDevicesCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                RefreshDeviceList();
            });
        }

        private void RefreshDeviceList()
        {
            // Clear existing device controls
            foreach (var deviceControl in _deviceControls)
            {
                deviceControl.DeviceSelected -= OnDeviceSelected;
                deviceControl.ConnectionRequested -= OnDeviceConnectionRequested;
            }
            _deviceControls.Clear();
            DeviceListContainer.Children.Clear();

            // Add devices from discovery workflow
            if (_discoveryWorkflow.DiscoveredDevices.Count == 0)
            {
                DeviceListContainer.Children.Add(NoDevicesMessage);
            }
            else
            {
                foreach (var discoveredDevice in _discoveryWorkflow.DiscoveredDevices)
                {
                    var deviceControl = new Device
                    {
                        DeviceInfo = discoveredDevice.Device,
                        ConnectionStatus = discoveredDevice.ConnectionStatus
                    };

                    deviceControl.DeviceSelected += OnDeviceSelected;
                    deviceControl.ConnectionRequested += OnDeviceConnectionRequested;

                    _deviceControls.Add(deviceControl);
                    DeviceListContainer.Children.Add(deviceControl);
                }
            }
        }

        private void OnDeviceSelected(object sender, DeviceSelectionEventArgs e)
        {
            // Handle single selection
            foreach (var deviceControl in _deviceControls)
            {
                if (deviceControl != sender)
                {
                    deviceControl.IsSelected = false;
                }
            }

            _selectedDevice = e.IsSelected ? sender as Device : null;

            System.Diagnostics.Debug.WriteLine($"Device {e.Device.alias} {(e.IsSelected ? "selected" : "deselected")}");
        }

        private async void OnDeviceConnectionRequested(object sender, DeviceConnectionEventArgs e)
        {
            var deviceControl = sender as Device;

            try
            {
                switch (e.Action)
                {
                    case ConnectionAction.Connect:
                        await AttemptDeviceConnection(deviceControl, e.Device);
                        break;

                    case ConnectionAction.Cancel:
                        deviceControl.ConnectionStatus = DeviceConnectionStatus.Available;
                        await _workflow.CancelWorkflowAsync();
                        break;

                    case ConnectionAction.Send:
                        await InitiateWorkflowTransfer(deviceControl, e.Device);
                        break;

                    case ConnectionAction.Retry:
                        await AttemptDeviceConnection(deviceControl, e.Device);
                        break;
                }
            }
            catch (Exception ex)
            {
                ShowError($"Connection error: {ex.Message}");
                deviceControl.ConnectionStatus = DeviceConnectionStatus.Error;
            }
        }

        private async Task AttemptDeviceConnection(Device deviceControl, Models.Device deviceInfo)
        {
            try
            {
                deviceControl.ConnectionStatus = DeviceConnectionStatus.Connecting;

                // Find the discovered device
                var discoveredDevice = _discoveryWorkflow.DiscoveredDevices
                    .FirstOrDefault(d => d.Device.ip == deviceInfo.ip);

                if (discoveredDevice != null)
                {
                    // Use discovery workflow to establish connection
                    var connection = await _discoveryWorkflow.EstablishConnectionAsync(discoveredDevice);

                    if (connection != null)
                    {
                        deviceControl.ConnectionStatus = DeviceConnectionStatus.Connected;
                        System.Diagnostics.Debug.WriteLine($"Connected to device: {deviceInfo.alias}");
                    }
                    else
                    {
                        deviceControl.ConnectionStatus = DeviceConnectionStatus.Error;
                    }
                }
                else
                {
                    deviceControl.ConnectionStatus = DeviceConnectionStatus.Error;
                }
            }
            catch (Exception ex)
            {
                deviceControl.ConnectionStatus = DeviceConnectionStatus.Error;
                throw;
            }
        }

        private async Task InitiateWorkflowTransfer(Device deviceControl, Models.Device targetDevice)
        {
            try
            {
                deviceControl.ConnectionStatus = DeviceConnectionStatus.Connecting;

                // Start the workflow if not already started
                if (_workflow.CurrentState == WorkflowState.Idle)
                {
                    var started = await _workflow.StartWorkflowAsync();
                    if (!started)
                    {
                        deviceControl.ConnectionStatus = DeviceConnectionStatus.Error;
                        return;
                    }
                }

                // Select device and continue workflow
                var success = await _workflow.SelectDeviceAndContinueAsync(targetDevice);
                if (success)
                {
                    deviceControl.ConnectionStatus = DeviceConnectionStatus.Connected;
                    ShowInfo($"File transfer started to {targetDevice.alias}");
                }
                else
                {
                    deviceControl.ConnectionStatus = DeviceConnectionStatus.Error;
                }
            }
            catch (Exception ex)
            {
                ShowError($"Failed to initiate transfer: {ex.Message}");
                deviceControl.ConnectionStatus = DeviceConnectionStatus.Error;
            }
        }

        private async void OnRefreshDevicesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                DiscoveryProgress.IsActive = true;
                RefreshButton.IsEnabled = false;

                // Trigger device discovery refresh
                await _discoveryWorkflow.RefreshDiscoveryAsync();

                RefreshDeviceList();
            }
            catch (Exception ex)
            {
                ShowError($"Failed to refresh devices: {ex.Message}");
            }
            finally
            {
                DiscoveryProgress.IsActive = false;
                RefreshButton.IsEnabled = true;
            }
        }

        private void OnWorkflowStateChanged(object sender, WorkflowStateChangedEventArgs e)
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                UpdateUIForWorkflowState(e.NewState);
            });
        }

        private void OnWorkflowProgressUpdated(object sender, WorkflowProgressEventArgs e)
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                WorkflowProgressBar.Value = e.Progress;
                WorkflowStatusText.Text = e.Message;
                System.Diagnostics.Debug.WriteLine($"Workflow progress: {e.Progress:F1}% - {e.Message}");
            });
        }

        private void OnWorkflowErrorOccurred(object sender, WorkflowErrorEventArgs e)
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                ShowError($"Workflow error: {e.Message}");
            });
        }

        private void UpdateUIForWorkflowState(WorkflowState state)
        {
            switch (state)
            {
                case WorkflowState.Idle:
                    WorkflowStatusText.Text = "Ready to send files";
                    WorkflowProgressBar.Value = 0;
                    StartWorkflowButton.IsEnabled = true;
                    CancelWorkflowButton.IsEnabled = false;
                    break;

                case WorkflowState.Initializing:
                    WorkflowStatusText.Text = "Initializing file transfer...";
                    WorkflowProgressBar.Value = 10;
                    StartWorkflowButton.IsEnabled = false;
                    CancelWorkflowButton.IsEnabled = true;
                    break;

                case WorkflowState.WaitingForDeviceSelection:
                    WorkflowStatusText.Text = "Select a device to send files to";
                    WorkflowProgressBar.Value = 25;
                    break;

                case WorkflowState.ConnectingToDevice:
                    WorkflowStatusText.Text = $"Connecting to {_workflow.SelectedDevice?.alias ?? "device"}...";
                    WorkflowProgressBar.Value = 50;
                    break;

                case WorkflowState.Transferring:
                    WorkflowStatusText.Text = "Transferring files...";
                    WorkflowProgressBar.Value = 75;
                    break;

                case WorkflowState.Completed:
                    WorkflowStatusText.Text = "All files transferred successfully!";
                    WorkflowProgressBar.Value = 100;
                    StartWorkflowButton.IsEnabled = true;
                    CancelWorkflowButton.IsEnabled = false;
                    ShowInfo("All files transferred successfully!");
                    break;

                case WorkflowState.Cancelled:
                    WorkflowStatusText.Text = "Transfer cancelled";
                    WorkflowProgressBar.Value = 0;
                    StartWorkflowButton.IsEnabled = true;
                    CancelWorkflowButton.IsEnabled = false;
                    break;

                case WorkflowState.Error:
                    WorkflowStatusText.Text = "Transfer failed";
                    WorkflowProgressBar.Value = 0;
                    StartWorkflowButton.IsEnabled = true;
                    CancelWorkflowButton.IsEnabled = false;
                    break;
            }
        }

        private void UpdatePendingUploadsDisplay()
        {
            // Update the pending uploads list to show workflow files
            // In a real implementation, this would bind to _workflow.SelectedFiles
            System.Diagnostics.Debug.WriteLine($"Pending uploads: {_workflow.SelectedFiles.Count} files");
        }

        private async void OnStartWorkflowClick(object sender, RoutedEventArgs e)
        {
            try
            {
                StartWorkflowButton.IsEnabled = false;
                CancelWorkflowButton.IsEnabled = true;

                var success = await _workflow.StartWorkflowAsync();
                if (!success)
                {
                    StartWorkflowButton.IsEnabled = true;
                    CancelWorkflowButton.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                ShowError($"Failed to start workflow: {ex.Message}");
                StartWorkflowButton.IsEnabled = true;
                CancelWorkflowButton.IsEnabled = false;
            }
        }

        private async void OnCancelWorkflowClick(object sender, RoutedEventArgs e)
        {
            try
            {
                CancelWorkflowButton.IsEnabled = false;
                await _workflow.CancelWorkflowAsync();
                StartWorkflowButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                ShowError($"Failed to cancel workflow: {ex.Message}");
                CancelWorkflowButton.IsEnabled = true;
            }
        }

        private void OnDeviceDiscovered(object sender, DeviceDiscoveredEventArgs e)
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                RefreshDeviceList();
                System.Diagnostics.Debug.WriteLine($"Device discovered: {e.Device.Device.alias}");
            });
        }

        private void OnDeviceLost(object sender, DeviceLostEventArgs e)
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                RefreshDeviceList();
                System.Diagnostics.Debug.WriteLine($"Device lost: {e.Device.Device.alias}");
            });
        }

        private void OnConnectionEstablished(object sender, ConnectionEstablishedEventArgs e)
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                // Update device status in UI
                var deviceControl = _deviceControls.FirstOrDefault(d =>
                    d.DeviceInfo.ip == e.Connection.Device.ip);
                if (deviceControl != null)
                {
                    deviceControl.ConnectionStatus = DeviceConnectionStatus.Connected;
                }

                System.Diagnostics.Debug.WriteLine($"Connection established: {e.Connection.Device.alias}");
            });
        }

        private void OnDiscoveryErrorOccurred(object sender, DiscoveryErrorEventArgs e)
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                ShowError($"Discovery error: {e.Message}");
            });
        }
    }
}
