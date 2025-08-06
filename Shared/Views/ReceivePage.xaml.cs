using System;
using Shared.Workflows;
using Shared.Platform;

#if WINDOWS_PHONE
using System.Windows.Controls;
#else
using Windows.UI.Xaml.Controls;
#endif
using Windows.UI.Xaml.Navigation;
#endif

namespace Shared.Views
{
    public sealed partial class ReceivePage : UserControl
    {
        private ReceiveFileWorkflow _workflow;

        public ReceivePage()
        {
            this.InitializeComponent();
            Loaded += ReceivePage_Loaded;
            Unloaded += ReceivePage_Unloaded;
            _workflow = ReceiveFileWorkflow.Instance;

            // Subscribe to workflow events
            _workflow.StateChanged += OnWorkflowStateChanged;
            _workflow.TransferRequestReceived += OnTransferRequestReceived;
            _workflow.ErrorOccurred += OnWorkflowErrorOccurred;
        }

        private async void ReceivePage_Loaded(object sender, RoutedEventArgs e)
        {
            this.DeviceName.Text = Settings.DeviceName;

            // Start advertising automatically when page loads
            await _workflow.StartAdvertisingAsync();
            UpdateUIForWorkflowState(_workflow.CurrentState);
        }

        private void ReceivePage_Unloaded(object sender, RoutedEventArgs e)
        {
            // Unsubscribe from workflow events to prevent memory leaks
            if (_workflow != null)
            {
                _workflow.StateChanged -= OnWorkflowStateChanged;
                _workflow.TransferRequestReceived -= OnTransferRequestReceived;
                _workflow.ErrorOccurred -= OnWorkflowErrorOccurred;
            }
        }

        private void OnWorkflowStateChanged(object sender, ReceiveWorkflowStateChangedEventArgs e)
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                UpdateUIForWorkflowState(e.NewState);
            });
        }

        private void OnTransferRequestReceived(object sender, IncomingTransferRequestEventArgs e)
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                // Show incoming transfer request
                ShowIncomingTransferRequest(e.Request);
            });
        }

        private void OnWorkflowErrorOccurred(object sender, ReceiveWorkflowErrorEventArgs e)
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                State.Text = $"Error: {e.Message}";
            });
        }

        private void UpdateUIForWorkflowState(ReceiveWorkflowState state)
        {
            switch (state)
            {
                case ReceiveWorkflowState.Idle:
                    State.Text = "Ready to receive files";
                    FilesToReceive.Visibility = Visibility.Collapsed;
                    SenderDevice.Visibility = Visibility.Collapsed;
                    break;

                case ReceiveWorkflowState.Advertising:
                    State.Text = "Listening for incoming files...";
                    FilesToReceive.Visibility = Visibility.Collapsed;
                    SenderDevice.Visibility = Visibility.Collapsed;
                    break;

                case ReceiveWorkflowState.PendingApproval:
                    State.Text = "Incoming file transfer request";
                    FilesToReceive.Visibility = Visibility.Visible;
                    SenderDevice.Visibility = Visibility.Visible;
                    break;

                case ReceiveWorkflowState.Receiving:
                    State.Text = "Receiving files...";
                    FilesToReceive.Visibility = Visibility.Visible;
                    break;

                case ReceiveWorkflowState.Completed:
                    State.Text = "Files received successfully!";
                    break;

                case ReceiveWorkflowState.Error:
                    State.Text = "Error occurred during file transfer";
                    break;
            }
        }

        private void ShowIncomingTransferRequest(IncomingTransferRequest request)
        {
            // Update sender device info
            if (SenderDevice != null)
            {
                SenderDevice.DeviceInfo = request.SenderDevice;
            }

            // Show file list
            // In a real implementation, this would populate FilesToReceive ListView
            State.Text = $"Incoming {request.Files.Count} file(s) from {request.SenderDevice.alias}";
        }

        private async void OnAcceptAllClick(object sender, RoutedEventArgs e)
        {
            await _workflow.AcceptAllPendingRequestsAsync();
        }

        private async void OnRejectAllClick(object sender, RoutedEventArgs e)
        {
            await _workflow.RejectAllPendingRequestsAsync();
        }
    }
}
