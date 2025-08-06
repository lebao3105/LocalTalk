using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Shared.Platform;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shared.Workflows
{
    /// <summary>
    /// Centralized progress tracking system for monitoring operations across the application
    /// </summary>
    public class ProgressTracker : INotifyPropertyChanged, IDisposable
    {
        private static readonly Lazy<ProgressTracker> _instance = new Lazy<ProgressTracker>(() => new ProgressTracker());
        private double _overallProgress;
        private string _currentOperation;
        private bool _hasErrors;
        private bool _disposed = false;

        /// <summary>
        /// Gets the singleton instance of the ProgressTracker
        /// </summary>
        public static ProgressTracker Instance => _instance.Value;

        /// <summary>
        /// Event raised when a property value changes
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Event raised when progress is updated
        /// </summary>
        public event EventHandler<ProgressUpdatedEventArgs> ProgressUpdated;

        /// <summary>
        /// Event raised when an operation completes
        /// </summary>
        public event EventHandler<OperationCompletedEventArgs> OperationCompleted;

        /// <summary>
        /// Event raised when an error occurs during an operation
        /// </summary>
        public event EventHandler<ErrorOccurredEventArgs> ErrorOccurred;

        /// <summary>
        /// Gets the collection of currently active operations
        /// </summary>
        public ObservableCollection<ProgressOperation> ActiveOperations { get; }

        /// <summary>
        /// Gets the collection of error reports from completed operations
        /// </summary>
        public ObservableCollection<ErrorReport> ErrorHistory { get; }

        /// <summary>
        /// Gets the overall progress percentage (0.0 to 100.0) across all active operations
        /// </summary>
        public double OverallProgress
        {
            get => _overallProgress;
            private set
            {
                _overallProgress = value;
                OnPropertyChanged();
                ProgressUpdated?.Invoke(this, new ProgressUpdatedEventArgs(value, CurrentOperation));
            }
        }

        /// <summary>
        /// Gets the description of the currently executing operation
        /// </summary>
        public string CurrentOperation
        {
            get => _currentOperation;
            private set
            {
                _currentOperation = value;
                OnPropertyChanged();
            }
        }

        public bool HasErrors
        {
            get => _hasErrors;
            private set
            {
                _hasErrors = value;
                OnPropertyChanged();
            }
        }

        private ProgressTracker()
        {
            ActiveOperations = new ObservableCollection<ProgressOperation>();
            ErrorHistory = new ObservableCollection<ErrorReport>();
            CurrentOperation = "Ready";
        }

        public ProgressOperation StartOperation(string operationId, string description, double weight = 1.0)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(operationId))
                throw new ArgumentNullException(nameof(operationId));
            if (string.IsNullOrEmpty(description))
                throw new ArgumentNullException(nameof(description));
            if (weight <= 0)
                throw new ArgumentOutOfRangeException(nameof(weight), "Weight must be greater than 0");

            var operation = new ProgressOperation
            {
                Id = operationId,
                Description = description,
                Weight = weight,
                StartTime = DateTime.Now,
                Status = OperationStatus.InProgress,
                Progress = 0
            };

            PlatformFactory.Current.RunOnUIThread(() =>
            {
                ActiveOperations.Add(operation);
                UpdateOverallProgress();
                CurrentOperation = description;
            });

            return operation;
        }

        public void UpdateOperationProgress(string operationId, double progress, string statusMessage = null)
        {
            var operation = ActiveOperations.FirstOrDefault(o => o.Id == operationId);
            if (operation != null)
            {
                operation.Progress = Math.Max(0, Math.Min(100, progress));
                if (!string.IsNullOrEmpty(statusMessage))
                {
                    operation.StatusMessage = statusMessage;
                }

                PlatformFactory.Current.RunOnUIThread(() =>
                {
                    UpdateOverallProgress();
                    if (operation.Progress > 0)
                    {
                        CurrentOperation = $"{operation.Description} ({operation.Progress:F1}%)";
                    }
                });
            }
        }

        public void CompleteOperation(string operationId, bool success = true, string message = null)
        {
            var operation = ActiveOperations.FirstOrDefault(o => o.Id == operationId);
            if (operation != null)
            {
                operation.Progress = success ? 100 : operation.Progress;
                operation.Status = success ? OperationStatus.Completed : OperationStatus.Failed;
                operation.EndTime = DateTime.Now;
                operation.StatusMessage = message ?? (success ? "Completed" : "Failed");

                PlatformFactory.Current.RunOnUIThread(() =>
                {
                    ActiveOperations.Remove(operation);
                    UpdateOverallProgress();
                    
                    OperationCompleted?.Invoke(this, new OperationCompletedEventArgs(operation, success));
                    
                    if (!success)
                    {
                        ReportError(operation.Id, message ?? "Operation failed", null, ErrorSeverity.Warning);
                    }
                });
            }
        }

        public void ReportError(string source, string message, Exception exception = null, ErrorSeverity severity = ErrorSeverity.Error)
        {
            var errorReport = new ErrorReport
            {
                Id = Guid.NewGuid().ToString(),
                Source = source,
                Message = message,
                Exception = exception,
                Severity = severity,
                Timestamp = DateTime.Now,
                IsResolved = false
            };

            PlatformFactory.Current.RunOnUIThread(() =>
            {
                ErrorHistory.Add(errorReport);
                HasErrors = ErrorHistory.Any(e => !e.IsResolved && e.Severity >= ErrorSeverity.Error);
                
                ErrorOccurred?.Invoke(this, new ErrorOccurredEventArgs(errorReport));
            });
        }

        public void ResolveError(string errorId)
        {
            var error = ErrorHistory.FirstOrDefault(e => e.Id == errorId);
            if (error != null)
            {
                error.IsResolved = true;
                error.ResolvedAt = DateTime.Now;
                
                PlatformFactory.Current.RunOnUIThread(() =>
                {
                    HasErrors = ErrorHistory.Any(e => !e.IsResolved && e.Severity >= ErrorSeverity.Error);
                });
            }
        }

        public void ClearErrorHistory()
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                ErrorHistory.Clear();
                HasErrors = false;
            });
        }

        public async Task<bool> RetryOperationAsync(string operationId, Func<Task<bool>> retryAction)
        {
            try
            {
                var retryOperation = StartOperation($"{operationId}_retry", $"Retrying operation {operationId}");
                
                var success = await retryAction();
                
                CompleteOperation(retryOperation.Id, success, success ? "Retry successful" : "Retry failed");
                
                if (success)
                {
                    // Resolve related errors
                    var relatedErrors = ErrorHistory.Where(e => e.Source == operationId && !e.IsResolved).ToList();
                    foreach (var error in relatedErrors)
                    {
                        ResolveError(error.Id);
                    }
                }
                
                return success;
            }
            catch (Exception ex)
            {
                ReportError(operationId, $"Retry failed: {ex.Message}", ex);
                return false;
            }
        }

        private void UpdateOverallProgress()
        {
            if (!ActiveOperations.Any())
            {
                OverallProgress = 0;
                CurrentOperation = "Ready";
                return;
            }

            var totalWeight = ActiveOperations.Sum(o => o.Weight);
            var weightedProgress = ActiveOperations.Sum(o => (o.Progress / 100.0) * o.Weight);
            
            OverallProgress = totalWeight > 0 ? (weightedProgress / totalWeight) * 100 : 0;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ProgressOperation : INotifyPropertyChanged
    {
        private double _progress;
        private string _statusMessage;
        private OperationStatus _status;

        public string Id { get; set; }
        public string Description { get; set; }
        public double Weight { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        public double Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public OperationStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
            }
        }

        public TimeSpan Duration => (EndTime ?? DateTime.Now) - StartTime;

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ErrorReport
    {
        public string Id { get; set; }
        public string Source { get; set; }
        public string Message { get; set; }
        public Exception Exception { get; set; }
        public ErrorSeverity Severity { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsResolved { get; set; }
        public DateTime? ResolvedAt { get; set; }

        public string SeverityText => Severity.ToString();
        public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss");
        public bool HasException => Exception != null;
        public string ExceptionDetails => Exception?.ToString();
    }

    public enum OperationStatus
    {
        InProgress,
        Completed,
        Failed,
        Cancelled
    }

    public enum ErrorSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2,
        Critical = 3
    }

    // Event argument classes
    public class ProgressUpdatedEventArgs : EventArgs
    {
        public double Progress { get; }
        public string Operation { get; }

        public ProgressUpdatedEventArgs(double progress, string operation)
        {
            Progress = progress;
            Operation = operation;
        }
    }

    public class OperationCompletedEventArgs : EventArgs
    {
        public ProgressOperation Operation { get; }
        public bool Success { get; }

        public OperationCompletedEventArgs(ProgressOperation operation, bool success)
        {
            Operation = operation;
            Success = success;
        }
    }

    public class ErrorOccurredEventArgs : EventArgs
    {
        public ErrorReport Error { get; }

        public ErrorOccurredEventArgs(ErrorReport error)
        {
            Error = error;
        }
    }

    public static class ErrorRecoveryManager
    {
        private static readonly Dictionary<string, Func<ErrorReport, Task<bool>>> _recoveryStrategies = new();

        static ErrorRecoveryManager()
        {
            RegisterDefaultRecoveryStrategies();
        }

        public static void RegisterRecoveryStrategy(string errorType, Func<ErrorReport, Task<bool>> strategy)
        {
            _recoveryStrategies[errorType] = strategy;
        }

        public static async Task<bool> AttemptRecoveryAsync(ErrorReport error)
        {
            try
            {
                // Try specific recovery strategy first
                if (_recoveryStrategies.TryGetValue(error.Source, out var strategy))
                {
                    return await strategy(error);
                }

                // Try generic recovery based on error type
                return await AttemptGenericRecoveryAsync(error);
            }
            catch (Exception ex)
            {
                ProgressTracker.Instance.ReportError("ErrorRecovery",
                    $"Recovery attempt failed: {ex.Message}", ex, ErrorSeverity.Warning);
                return false;
            }
        }

        private static void RegisterDefaultRecoveryStrategies()
        {
            // Network connection recovery
            RegisterRecoveryStrategy("NetworkConnection", async error =>
            {
                await Task.Delay(1000); // Wait before retry
                // In real implementation, would test network connectivity
                return true;
            });

            // File access recovery
            RegisterRecoveryStrategy("FileAccess", async error =>
            {
                await Task.Delay(500);
                // In real implementation, would check file permissions and availability
                return true;
            });

            // Device discovery recovery
            RegisterRecoveryStrategy("DeviceDiscovery", async error =>
            {
                // Restart discovery process
                return await DeviceDiscoveryWorkflow.Instance.RefreshDiscoveryAsync();
            });
        }

        private static async Task<bool> AttemptGenericRecoveryAsync(ErrorReport error)
        {
            // Generic recovery strategies based on error severity
            switch (error.Severity)
            {
                case ErrorSeverity.Warning:
                    // For warnings, just mark as resolved
                    ProgressTracker.Instance.ResolveError(error.Id);
                    return true;

                case ErrorSeverity.Error:
                    // For errors, wait and retry
                    await Task.Delay(2000);
                    return false; // Let caller decide on retry

                case ErrorSeverity.Critical:
                    // For critical errors, require manual intervention
                    return false;

                default:
                    return true;
            }
        }

        /// <summary>
        /// Throws ObjectDisposedException if the tracker has been disposed
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when tracker is disposed</exception>
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ProgressTracker));
        }

        /// <summary>
        /// Disposes the ProgressTracker and cleans up resources
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
                ActiveOperations.Clear();
                ErrorHistory.Clear();
                _disposed = true;
            }
        }
    }
}
