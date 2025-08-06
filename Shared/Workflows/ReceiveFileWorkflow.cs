using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Shared.FileSystem;
using Shared.Models;
using Shared.Platform;
using Shared.Protocol;
using Shared.Http;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shared.Workflows
{
    public class ReceiveFileWorkflow : INotifyPropertyChanged
    {
        private static ReceiveFileWorkflow _instance;
        private ReceiveWorkflowState _currentState;
        private string _statusMessage;
        private Device _senderDevice;
        private bool _isAdvertising;

        public static ReceiveFileWorkflow Instance => _instance ??= new ReceiveFileWorkflow();

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<ReceiveWorkflowStateChangedEventArgs> StateChanged;
        public event EventHandler<IncomingTransferRequestEventArgs> TransferRequestReceived;
        public event EventHandler<ReceiveWorkflowErrorEventArgs> ErrorOccurred;

        public ObservableCollection<IncomingTransferRequest> PendingRequests { get; }
        public ObservableCollection<TransferSession> ActiveReceives { get; }

        public ReceiveWorkflowState CurrentState
        {
            get => _currentState;
            private set
            {
                if (_currentState != value)
                {
                    var oldState = _currentState;
                    _currentState = value;
                    OnPropertyChanged();
                    StateChanged?.Invoke(this, new ReceiveWorkflowStateChangedEventArgs(oldState, value));
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public Device SenderDevice
        {
            get => _senderDevice;
            private set
            {
                _senderDevice = value;
                OnPropertyChanged();
            }
        }

        public bool IsAdvertising
        {
            get => _isAdvertising;
            private set
            {
                _isAdvertising = value;
                OnPropertyChanged();
            }
        }

        private ReceiveFileWorkflow()
        {
            PendingRequests = new ObservableCollection<IncomingTransferRequest>();
            ActiveReceives = new ObservableCollection<TransferSession>();
            CurrentState = ReceiveWorkflowState.Idle;
            StatusMessage = "Ready to receive files";
        }

        public async Task<bool> StartAdvertisingAsync()
        {
            try
            {
                CurrentState = ReceiveWorkflowState.Advertising;
                StatusMessage = "Advertising device for file transfers...";

                // Start the HTTP server to listen for incoming requests
                if (!await StartHttpServerAsync())
                {
                    CurrentState = ReceiveWorkflowState.Error;
                    return false;
                }

                // Start advertising this device on the network
                if (!await StartDeviceAdvertisingAsync())
                {
                    CurrentState = ReceiveWorkflowState.Error;
                    return false;
                }

                IsAdvertising = true;
                StatusMessage = $"Listening for files as '{LocalSendProtocol.ThisDevice.alias}'";
                return true;
            }
            catch (Exception ex)
            {
                HandleError("Failed to start advertising", ex);
                return false;
            }
        }

        public async Task<bool> StopAdvertisingAsync()
        {
            try
            {
                CurrentState = ReceiveWorkflowState.Stopping;
                StatusMessage = "Stopping file transfer service...";

                await StopHttpServerAsync();
                await StopDeviceAdvertisingAsync();

                IsAdvertising = false;
                CurrentState = ReceiveWorkflowState.Idle;
                StatusMessage = "Ready to receive files";
                return true;
            }
            catch (Exception ex)
            {
                HandleError("Failed to stop advertising", ex);
                return false;
            }
        }

        /// <summary>
        /// Accepts an incoming transfer request and starts receiving files
        /// </summary>
        /// <param name="request">The transfer request to accept</param>
        /// <returns>True if the transfer was accepted successfully</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null</exception>
        /// <exception cref="ArgumentException">Thrown when request contains invalid data</exception>
        /// <exception cref="InvalidOperationException">Thrown when workflow is not in the correct state</exception>
        public async Task<bool> AcceptTransferRequestAsync(IncomingTransferRequest request)
        {
            // Input validation
            if (request == null)
                throw new ArgumentNullException(nameof(request), "Transfer request cannot be null");

            if (request.SenderDevice == null)
                throw new ArgumentException("Sender device information is missing", nameof(request));

            if (string.IsNullOrWhiteSpace(request.SenderDevice.alias))
                throw new ArgumentException("Sender device alias cannot be empty", nameof(request));

            if (string.IsNullOrWhiteSpace(request.SenderDevice.ip))
                throw new ArgumentException("Sender device IP address cannot be empty", nameof(request));

            if (request.SenderDevice.port <= 0 || request.SenderDevice.port > 65535)
                throw new ArgumentException($"Sender device port {request.SenderDevice.port} is not valid", nameof(request));

            if (request.Files == null || !request.Files.Any())
                throw new ArgumentException("Transfer request must contain at least one file", nameof(request));

            if (!PendingRequests.Contains(request))
                throw new InvalidOperationException("Transfer request is not in pending requests list");

            try
            {
                CurrentState = ReceiveWorkflowState.Receiving;
                SenderDevice = request.SenderDevice;
                StatusMessage = $"Accepting files from {request.SenderDevice.alias}...";

                // Remove from pending requests
                PendingRequests.Remove(request);

                // Start receiving the files
                foreach (var fileInfo in request.Files)
                {
                    var session = await ChunkedTransferProtocol.Instance.StartDownloadAsync(
                        fileInfo, request.SenderDevice.ip, request.SenderDevice.port);

                    if (session != null)
                    {
                        ActiveReceives.Add(session);
                    }
                }

                // Monitor receive progress
                MonitorReceiveProgress();
                return true;
            }
            catch (Exception ex)
            {
                HandleError("Failed to accept transfer request", ex);
                return false;
            }
        }

        public async Task<bool> RejectTransferRequestAsync(IncomingTransferRequest request)
        {
            try
            {
                // Send rejection response to sender
                await SendRejectionResponseAsync(request);

                // Remove from pending requests
                PendingRequests.Remove(request);

                StatusMessage = $"Rejected transfer from {request.SenderDevice.alias}";

                // Return to advertising state if no other pending requests
                if (!PendingRequests.Any() && !ActiveReceives.Any())
                {
                    CurrentState = ReceiveWorkflowState.Advertising;
                    StatusMessage = $"Listening for files as '{LocalSendProtocol.ThisDevice.alias}'";
                }

                return true;
            }
            catch (Exception ex)
            {
                HandleError("Failed to reject transfer request", ex);
                return false;
            }
        }

        public async Task<bool> AcceptAllPendingRequestsAsync()
        {
            try
            {
                var requests = PendingRequests.ToList();
                foreach (var request in requests)
                {
                    await AcceptTransferRequestAsync(request);
                }
                return true;
            }
            catch (Exception ex)
            {
                HandleError("Failed to accept all requests", ex);
                return false;
            }
        }

        public async Task<bool> RejectAllPendingRequestsAsync()
        {
            try
            {
                var requests = PendingRequests.ToList();
                foreach (var request in requests)
                {
                    await RejectTransferRequestAsync(request);
                }
                return true;
            }
            catch (Exception ex)
            {
                HandleError("Failed to reject all requests", ex);
                return false;
            }
        }

        private async Task<bool> StartHttpServerAsync()
        {
            try
            {
                // Start the LocalSend HTTP server
                if (LocalSendHttpServer.Instance != null)
                {
                    await LocalSendHttpServer.Instance.StartAsync();

                    // Subscribe to incoming transfer requests
                    LocalSendHttpServer.Instance.TransferRequestReceived += OnTransferRequestReceived;
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                HandleError("Failed to start HTTP server", ex);
                return false;
            }
        }

        private async Task StopHttpServerAsync()
        {
            try
            {
                if (LocalSendHttpServer.Instance != null)
                {
                    LocalSendHttpServer.Instance.TransferRequestReceived -= OnTransferRequestReceived;
                    await LocalSendHttpServer.Instance.StopAsync();
                }
            }
            catch (Exception ex)
            {
                HandleError("Failed to stop HTTP server", ex);
            }
        }

        private async Task<bool> StartDeviceAdvertisingAsync()
        {
            try
            {
                // Start UDP multicast advertising
                if (LocalSendProtocol.Instance != null)
                {
                    // In a real implementation, this would start UDP multicast
                    await Task.Delay(100); // Simulate startup time
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                HandleError("Failed to start device advertising", ex);
                return false;
            }
        }

        private async Task StopDeviceAdvertisingAsync()
        {
            try
            {
                // Stop UDP multicast advertising
                if (LocalSendProtocol.Instance != null)
                {
                    // In a real implementation, this would stop UDP multicast
                    await Task.Delay(100); // Simulate shutdown time
                }
            }
            catch (Exception ex)
            {
                HandleError("Failed to stop device advertising", ex);
            }
        }

        private void OnTransferRequestReceived(object sender, TransferRequestEventArgs e)
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                var request = new IncomingTransferRequest
                {
                    RequestId = Guid.NewGuid().ToString(),
                    SenderDevice = e.SenderDevice,
                    Files = e.Files,
                    ReceivedAt = DateTime.Now,
                    TotalSize = e.Files.Sum(f => f.Size)
                };

                PendingRequests.Add(request);
                CurrentState = ReceiveWorkflowState.PendingApproval;
                StatusMessage = $"Incoming files from {e.SenderDevice.alias}";

                TransferRequestReceived?.Invoke(this, new IncomingTransferRequestEventArgs(request));
            });
        }

        private async Task SendRejectionResponseAsync(IncomingTransferRequest request)
        {
            // Send HTTP response to reject the transfer
            // In a real implementation, this would send a rejection response
            await Task.Delay(100);
        }

        private void MonitorReceiveProgress()
        {
            // Subscribe to transfer progress events
            ChunkedTransferProtocol.Instance.ProgressUpdated += OnReceiveProgressUpdated;
            ChunkedTransferProtocol.Instance.TransferCompleted += OnReceiveCompleted;
        }

        private void OnReceiveProgressUpdated(object sender, TransferProgressEventArgs e)
        {
            // Update progress for receiving files
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                if (ActiveReceives.Any())
                {
                    var totalProgress = ActiveReceives.Average(t =>
                        (double)t.TransferredBytes / t.TotalBytes * 100);

                    StatusMessage = $"Receiving files... {totalProgress:F1}%";
                }
            });
        }

        private void OnReceiveCompleted(object sender, TransferCompletedEventArgs e)
        {
            var session = ActiveReceives.FirstOrDefault(t => t.SessionId == e.SessionId);
            if (session != null)
            {
                ActiveReceives.Remove(session);

                PlatformFactory.Current.RunOnUIThread(() =>
                {
                    if (e.Success)
                    {
                        StatusMessage = $"Received: {session.FileName}";
                    }
                    else
                    {
                        HandleError($"Failed to receive: {session.FileName}", new Exception(e.ErrorMessage));
                    }

                    // Check if all receives are complete
                    if (!ActiveReceives.Any())
                    {
                        CurrentState = ReceiveWorkflowState.Completed;
                        StatusMessage = "All files received successfully";

                        // Cleanup and return to advertising
                        Task.Run(async () =>
                        {
                            await Task.Delay(3000); // Show completion message for 3 seconds
                            if (IsAdvertising)
                            {
                                CurrentState = ReceiveWorkflowState.Advertising;
                                StatusMessage = $"Listening for files as '{LocalSendProtocol.ThisDevice.alias}'";
                            }
                        });

                        // Cleanup
                        ChunkedTransferProtocol.Instance.ProgressUpdated -= OnReceiveProgressUpdated;
                        ChunkedTransferProtocol.Instance.TransferCompleted -= OnReceiveCompleted;
                    }
                });
            }
        }

        private void HandleError(string message, Exception exception)
        {
            CurrentState = ReceiveWorkflowState.Error;
            StatusMessage = message;
            ErrorOccurred?.Invoke(this, new ReceiveWorkflowErrorEventArgs(message, exception));
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum ReceiveWorkflowState
    {
        Idle,
        Advertising,
        PendingApproval,
        Receiving,
        Completed,
        Stopping,
        Error
    }

    public class IncomingTransferRequest
    {
        public string RequestId { get; set; }
        public Device SenderDevice { get; set; }
        public List<FileInfo> Files { get; set; }
        public DateTime ReceivedAt { get; set; }
        public long TotalSize { get; set; }
    }

    public class ReceiveWorkflowStateChangedEventArgs : EventArgs
    {
        public ReceiveWorkflowState OldState { get; }
        public ReceiveWorkflowState NewState { get; }

        public ReceiveWorkflowStateChangedEventArgs(ReceiveWorkflowState oldState, ReceiveWorkflowState newState)
        {
            OldState = oldState;
            NewState = newState;
        }
    }

    public class IncomingTransferRequestEventArgs : EventArgs
    {
        public IncomingTransferRequest Request { get; }

        public IncomingTransferRequestEventArgs(IncomingTransferRequest request)
        {
            Request = request;
        }
    }

    public class ReceiveWorkflowErrorEventArgs : EventArgs
    {
        public string Message { get; }
        public Exception Exception { get; }

        public ReceiveWorkflowErrorEventArgs(string message, Exception exception)
        {
            Message = message;
            Exception = exception;
        }
    }
}
