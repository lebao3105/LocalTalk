using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Shared.FileSystem;
using Shared.Models;
using Shared.Platform;
using Shared.Protocol;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shared.Workflows
{
    public class SendFileWorkflow : INotifyPropertyChanged
    {
        private static SendFileWorkflow _instance;
        private WorkflowState _currentState;
        private string _statusMessage;
        private double _overallProgress;
        private Device _selectedDevice;
        private ProgressTracker _progressTracker;

        public static SendFileWorkflow Instance => _instance ??= new SendFileWorkflow();

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<WorkflowStateChangedEventArgs> StateChanged;
        public event EventHandler<WorkflowProgressEventArgs> ProgressUpdated;
        public event EventHandler<WorkflowErrorEventArgs> ErrorOccurred;

        public ObservableCollection<SecureFileInfo> SelectedFiles { get; }
        public ObservableCollection<TransferSession> ActiveTransfers { get; }

        public WorkflowState CurrentState
        {
            get => _currentState;
            private set
            {
                if (_currentState != value)
                {
                    var oldState = _currentState;
                    _currentState = value;
                    OnPropertyChanged();
                    StateChanged?.Invoke(this, new WorkflowStateChangedEventArgs(oldState, value));
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

        public double OverallProgress
        {
            get => _overallProgress;
            private set
            {
                _overallProgress = value;
                OnPropertyChanged();
                ProgressUpdated?.Invoke(this, new WorkflowProgressEventArgs(value, StatusMessage));
            }
        }

        public Device SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                _selectedDevice = value;
                OnPropertyChanged();
            }
        }

        private SendFileWorkflow()
        {
            SelectedFiles = new ObservableCollection<SecureFileInfo>();
            ActiveTransfers = new ObservableCollection<TransferSession>();
            CurrentState = WorkflowState.Idle;
            StatusMessage = "Ready to send files";
            _progressTracker = ProgressTracker.Instance;
        }

        public async Task<bool> StartWorkflowAsync()
        {
            var operation = _progressTracker.StartOperation("SendWorkflow", "Starting file transfer workflow", 1.0);

            try
            {
                CurrentState = WorkflowState.Initializing;
                StatusMessage = "Initializing file transfer workflow...";
                OverallProgress = 0;

                _progressTracker.UpdateOperationProgress(operation.Id, 10, "Validating prerequisites...");

                // Step 1: Validate prerequisites
                if (!await ValidatePrerequisitesAsync())
                {
                    CurrentState = WorkflowState.Error;
                    _progressTracker.CompleteOperation(operation.Id, false, "Prerequisites validation failed");
                    return false;
                }

                _progressTracker.UpdateOperationProgress(operation.Id, 40, "Preparing files...");

                // Step 2: Prepare files for transfer
                if (!await PrepareFilesAsync())
                {
                    CurrentState = WorkflowState.Error;
                    _progressTracker.CompleteOperation(operation.Id, false, "File preparation failed");
                    return false;
                }

                _progressTracker.UpdateOperationProgress(operation.Id, 70, "Discovering devices...");

                // Step 3: Discover and select device
                if (!await DiscoverDevicesAsync())
                {
                    CurrentState = WorkflowState.Error;
                    _progressTracker.CompleteOperation(operation.Id, false, "Device discovery failed");
                    return false;
                }

                CurrentState = WorkflowState.WaitingForDeviceSelection;
                StatusMessage = "Select a device to send files to";
                OverallProgress = 25;

                _progressTracker.CompleteOperation(operation.Id, true, "Workflow initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                _progressTracker.CompleteOperation(operation.Id, false, $"Workflow initialization failed: {ex.Message}");
                HandleError("Failed to start workflow", ex);
                return false;
            }
        }

        /// <summary>
        /// Selects a device and continues with the file transfer workflow
        /// </summary>
        /// <param name="device">The device to send files to</param>
        /// <returns>True if the device selection and connection was successful</returns>
        /// <exception cref="ArgumentNullException">Thrown when device is null</exception>
        /// <exception cref="InvalidOperationException">Thrown when workflow is not in the correct state</exception>
        public async Task<bool> SelectDeviceAndContinueAsync(Device device)
        {
            // Input validation
            if (device == null)
                throw new ArgumentNullException(nameof(device), "Device cannot be null");

            if (string.IsNullOrWhiteSpace(device.alias))
                throw new ArgumentException("Device alias cannot be null or empty", nameof(device));

            if (string.IsNullOrWhiteSpace(device.ip))
                throw new ArgumentException("Device IP address cannot be null or empty", nameof(device));

            if (device.port <= 0 || device.port > 65535)
                throw new ArgumentException($"Device port {device.port} is not valid", nameof(device));

            if (CurrentState != WorkflowState.WaitingForDeviceSelection)
                throw new InvalidOperationException($"Cannot select device in current state: {CurrentState}");

            try
            {
                SelectedDevice = device;
                CurrentState = WorkflowState.ConnectingToDevice;
                StatusMessage = $"Connecting to {device.alias}...";
                OverallProgress = 50;

                // Step 4: Establish connection
                if (!await EstablishConnectionAsync(device))
                {
                    CurrentState = WorkflowState.Error;
                    return false;
                }

                // Step 5: Initiate file transfers
                if (!await InitiateTransfersAsync())
                {
                    CurrentState = WorkflowState.Error;
                    return false;
                }

                CurrentState = WorkflowState.Transferring;
                StatusMessage = "Transferring files...";
                OverallProgress = 75;

                return true;
            }
            catch (Exception ex)
            {
                HandleError("Failed to connect to device", ex);
                return false;
            }
        }

        /// <summary>
        /// Adds files to the transfer selection
        /// </summary>
        /// <param name="files">The files to add</param>
        /// <returns>True if files were added successfully</returns>
        /// <exception cref="ArgumentNullException">Thrown when files is null</exception>
        public async Task<bool> AddFilesAsync(IEnumerable<SecureFileInfo> files)
        {
            if (files == null)
                throw new ArgumentNullException(nameof(files), "Files collection cannot be null");

            try
            {
                var filesList = files.ToList();
                if (filesList.Count == 0)
                {
                    StatusMessage = "No files provided to add";
                    return true;
                }

                // Validate each file
                foreach (var file in filesList)
                {
                    if (file == null)
                    {
                        HandleError("Cannot add null file to selection", null);
                        return false;
                    }

                    if (file.File == null)
                    {
                        HandleError("File information is missing", null);
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(file.File.Path))
                    {
                        HandleError("File path cannot be empty", null);
                        return false;
                    }

                    // Check if file is accessible
                    if (!await file.ValidateAccessAsync())
                    {
                        HandleError($"Cannot access file: {file.File.Name}", null);
                        return false;
                    }

                    // Add file if not already selected
                    if (!SelectedFiles.Any(f => f.File.Path == file.File.Path))
                    {
                        SelectedFiles.Add(file);
                    }
                }

                StatusMessage = $"{SelectedFiles.Count} file(s) selected for transfer";
                return true;
            }
            catch (Exception ex)
            {
                HandleError("Failed to add files", ex);
                return false;
            }
        }

        /// <summary>
        /// Removes a file from the transfer selection
        /// </summary>
        /// <param name="file">The file to remove</param>
        /// <exception cref="ArgumentNullException">Thrown when file is null</exception>
        public void RemoveFile(SecureFileInfo file)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file), "File cannot be null");

            SelectedFiles.Remove(file);
            StatusMessage = $"{SelectedFiles.Count} file(s) selected for transfer";
        }

        public void ClearFiles()
        {
            SelectedFiles.Clear();
            StatusMessage = "No files selected";
        }

        public async Task<bool> CancelWorkflowAsync()
        {
            try
            {
                CurrentState = WorkflowState.Cancelling;
                StatusMessage = "Cancelling transfers...";

                // Cancel all active transfers
                var cancelTasks = ActiveTransfers.Select(transfer =>
                    ChunkedTransferProtocol.Instance.CancelTransferAsync(transfer.SessionId));

                await Task.WhenAll(cancelTasks);

                ActiveTransfers.Clear();
                CurrentState = WorkflowState.Cancelled;
                StatusMessage = "Transfer cancelled";
                OverallProgress = 0;

                return true;
            }
            catch (Exception ex)
            {
                HandleError("Failed to cancel workflow", ex);
                return false;
            }
        }

        private async Task<bool> ValidatePrerequisitesAsync()
        {
            // Check if LocalSend protocol is running
            if (LocalSendProtocol.Instance == null)
            {
                HandleError("LocalSend protocol not initialized", null);
                return false;
            }

            // Check if files are selected
            if (!SelectedFiles.Any())
            {
                HandleError("No files selected for transfer", null);
                return false;
            }

            // Validate file access
            foreach (var file in SelectedFiles)
            {
                if (!await file.ValidateAccessAsync())
                {
                    HandleError($"Cannot access file: {file.File.Name}", null);
                    return false;
                }
            }

            return true;
        }

        private async Task<bool> PrepareFilesAsync()
        {
            StatusMessage = "Preparing files for transfer...";

            // Perform security analysis on all files
            foreach (var file in SelectedFiles)
            {
                var analysisResult = await file.PerformSecurityAnalysisAsync();
                if (!analysisResult.IsSecure)
                {
                    HandleError($"Security check failed for {file.File.Name}: {analysisResult.Issues.FirstOrDefault()}", null);
                    return false;
                }
            }

            return true;
        }

        private async Task<bool> DiscoverDevicesAsync()
        {
            StatusMessage = "Discovering devices...";

            // Trigger device discovery
            if (LocalSendProtocol.Instance != null)
            {
                // In a real implementation, this would trigger UDP multicast discovery
                await Task.Delay(1000); // Simulate discovery time
            }

            return true;
        }

        private async Task<bool> EstablishConnectionAsync(Device device)
        {
            // Test connection to device
            try
            {
                // In a real implementation, this would ping the device HTTP server
                await Task.Delay(500); // Simulate connection time
                return true;
            }
            catch (Exception ex)
            {
                HandleError($"Failed to connect to {device.alias}", ex);
                return false;
            }
        }

        private async Task<bool> InitiateTransfersAsync()
        {
            try
            {
                foreach (var file in SelectedFiles)
                {
                    var session = await ChunkedTransferProtocol.Instance.StartUploadAsync(
                        file, SelectedDevice.ip, SelectedDevice.port);

                    if (session != null)
                    {
                        ActiveTransfers.Add(session);
                    }
                }

                // Monitor transfer progress
                MonitorTransferProgress();
                return true;
            }
            catch (Exception ex)
            {
                HandleError("Failed to initiate transfers", ex);
                return false;
            }
        }

        private void MonitorTransferProgress()
        {
            // Subscribe to transfer progress events
            ChunkedTransferProtocol.Instance.ProgressUpdated += OnTransferProgressUpdated;
            ChunkedTransferProtocol.Instance.TransferCompleted += OnTransferCompleted;
        }

        private void OnTransferProgressUpdated(object sender, TransferProgressEventArgs e)
        {
            // Calculate overall progress based on all active transfers
            if (ActiveTransfers.Any())
            {
                var totalProgress = ActiveTransfers.Average(t =>
                    (double)t.TransferredBytes / t.TotalBytes * 100);

                OverallProgress = 75 + (totalProgress * 0.25); // 75-100% range for transfers
                StatusMessage = $"Transferring files... {totalProgress:F1}%";
            }
        }

        private void OnTransferCompleted(object sender, TransferCompletedEventArgs e)
        {
            var session = ActiveTransfers.FirstOrDefault(t => t.SessionId == e.SessionId);
            if (session != null)
            {
                ActiveTransfers.Remove(session);

                if (e.Success)
                {
                    StatusMessage = $"Transfer completed: {session.FileName}";
                }
                else
                {
                    HandleError($"Transfer failed: {session.FileName}", new Exception(e.ErrorMessage));
                }

                // Check if all transfers are complete
                if (!ActiveTransfers.Any())
                {
                    CurrentState = WorkflowState.Completed;
                    StatusMessage = "All files transferred successfully";
                    OverallProgress = 100;

                    // Cleanup
                    ChunkedTransferProtocol.Instance.ProgressUpdated -= OnTransferProgressUpdated;
                    ChunkedTransferProtocol.Instance.TransferCompleted -= OnTransferCompleted;
                }
            }
        }

        private void HandleError(string message, Exception exception)
        {
            CurrentState = WorkflowState.Error;
            StatusMessage = message;

            // Report error to progress tracker
            _progressTracker.ReportError("SendWorkflow", message, exception, ErrorSeverity.Error);

            ErrorOccurred?.Invoke(this, new WorkflowErrorEventArgs(message, exception));
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum WorkflowState
    {
        Idle,
        Initializing,
        WaitingForDeviceSelection,
        ConnectingToDevice,
        Transferring,
        Completed,
        Cancelled,
        Cancelling,
        Error
    }

    public class WorkflowStateChangedEventArgs : EventArgs
    {
        public WorkflowState OldState { get; }
        public WorkflowState NewState { get; }

        public WorkflowStateChangedEventArgs(WorkflowState oldState, WorkflowState newState)
        {
            OldState = oldState;
            NewState = newState;
        }
    }

    public class WorkflowProgressEventArgs : EventArgs
    {
        public double Progress { get; }
        public string Message { get; }

        public WorkflowProgressEventArgs(double progress, string message)
        {
            Progress = progress;
            Message = message;
        }
    }

    public class WorkflowErrorEventArgs : EventArgs
    {
        public string Message { get; }
        public Exception Exception { get; }

        public WorkflowErrorEventArgs(string message, Exception exception)
        {
            Message = message;
            Exception = exception;
        }
    }
}
