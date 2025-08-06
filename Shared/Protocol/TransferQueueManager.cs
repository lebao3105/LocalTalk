using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace Shared.Protocol
{
    /// <summary>
    /// Transfer queue management with prioritization, scheduling, dependency handling, and resource allocation optimization
    /// </summary>
    public class TransferQueueManager : IDisposable
    {
        private static TransferQueueManager _instance;
        private readonly ConcurrentDictionary<string, QueuedTransfer> _queuedTransfers;
        private readonly ConcurrentDictionary<string, ActiveTransfer> _activeTransfers;
        private readonly Channel<QueuedTransfer> _transferQueue;
        private readonly SemaphoreSlim _concurrencyLimiter;
        private readonly Timer _schedulerTimer;
        private readonly TransferQueueConfiguration _config;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private bool _disposed;

        public static TransferQueueManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TransferQueueManager();
                }
                return _instance;
            }
        }

        public event EventHandler<TransferQueuedEventArgs> TransferQueued;
        public event EventHandler<TransferStartedEventArgs> TransferStarted;
        public event EventHandler<TransferCompletedEventArgs> TransferCompleted;
        public event EventHandler<QueueStatusChangedEventArgs> QueueStatusChanged;

        private TransferQueueManager()
        {
            _queuedTransfers = new ConcurrentDictionary<string, QueuedTransfer>();
            _activeTransfers = new ConcurrentDictionary<string, ActiveTransfer>();
            _config = new TransferQueueConfiguration();
            _cancellationTokenSource = new CancellationTokenSource();
            
            // Create unbounded channel for transfer queue
            _transferQueue = Channel.CreateUnbounded<QueuedTransfer>();
            
            // Limit concurrent transfers
            _concurrencyLimiter = new SemaphoreSlim(_config.MaxConcurrentTransfers, _config.MaxConcurrentTransfers);
            
            // Start scheduler timer
            _schedulerTimer = new Timer(ProcessScheduledTransfers, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            
            // Start queue processor
            _ = Task.Run(ProcessTransferQueueAsync, _cancellationTokenSource.Token);
        }

        /// <summary>
        /// Queues a transfer for execution
        /// </summary>
        public async Task<string> QueueTransferAsync(TransferQueueRequest request)
        {
            var transferId = Guid.NewGuid().ToString();
            var queuedTransfer = new QueuedTransfer
            {
                TransferId = transferId,
                Request = request,
                Priority = request.Priority,
                QueuedAt = DateTime.Now,
                ScheduledFor = request.ScheduledFor ?? DateTime.Now,
                Status = TransferQueueStatus.Queued,
                Dependencies = request.Dependencies?.ToList() ?? new List<string>(),
                ResourceRequirements = request.ResourceRequirements ?? new ResourceRequirements()
            };

            _queuedTransfers[transferId] = queuedTransfer;

            // Check if transfer can be executed immediately
            if (CanExecuteImmediately(queuedTransfer))
            {
                await _transferQueue.Writer.WriteAsync(queuedTransfer, _cancellationTokenSource.Token);
            }

            OnTransferQueued(new TransferQueuedEventArgs
            {
                TransferId = transferId,
                Priority = request.Priority,
                ScheduledFor = queuedTransfer.ScheduledFor,
                QueuePosition = GetQueuePosition(transferId)
            });

            return transferId;
        }

        /// <summary>
        /// Cancels a queued or active transfer
        /// </summary>
        public async Task<bool> CancelTransferAsync(string transferId)
        {
            // Try to cancel queued transfer
            if (_queuedTransfers.TryGetValue(transferId, out var queuedTransfer))
            {
                queuedTransfer.Status = TransferQueueStatus.Cancelled;
                _queuedTransfers.TryRemove(transferId, out _);
                return true;
            }

            // Try to cancel active transfer
            if (_activeTransfers.TryGetValue(transferId, out var activeTransfer))
            {
                activeTransfer.CancellationTokenSource.Cancel();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Updates transfer priority
        /// </summary>
        public bool UpdateTransferPriority(string transferId, TransferPriority newPriority)
        {
            if (_queuedTransfers.TryGetValue(transferId, out var queuedTransfer))
            {
                queuedTransfer.Priority = newPriority;
                
                // Re-queue with new priority
                _ = Task.Run(async () =>
                {
                    await _transferQueue.Writer.WriteAsync(queuedTransfer, _cancellationTokenSource.Token);
                });
                
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets queue status information
        /// </summary>
        public QueueStatus GetQueueStatus()
        {
            var queuedCount = _queuedTransfers.Count(kvp => kvp.Value.Status == TransferQueueStatus.Queued);
            var activeCount = _activeTransfers.Count;
            var scheduledCount = _queuedTransfers.Count(kvp => kvp.Value.Status == TransferQueueStatus.Scheduled);

            return new QueueStatus
            {
                QueuedTransfers = queuedCount,
                ActiveTransfers = activeCount,
                ScheduledTransfers = scheduledCount,
                AvailableSlots = _config.MaxConcurrentTransfers - activeCount,
                TotalCapacity = _config.MaxConcurrentTransfers
            };
        }

        /// <summary>
        /// Processes the transfer queue
        /// </summary>
        private async Task ProcessTransferQueueAsync()
        {
            var reader = _transferQueue.Reader;
            
            try
            {
                await foreach (var queuedTransfer in reader.ReadAllAsync(_cancellationTokenSource.Token))
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        break;

                    // Check if transfer is still valid
                    if (!_queuedTransfers.ContainsKey(queuedTransfer.TransferId) ||
                        queuedTransfer.Status == TransferQueueStatus.Cancelled)
                    {
                        continue;
                    }

                    // Check dependencies
                    if (!AreDependenciesSatisfied(queuedTransfer))
                    {
                        // Re-queue for later
                        await Task.Delay(1000, _cancellationTokenSource.Token);
                        await _transferQueue.Writer.WriteAsync(queuedTransfer, _cancellationTokenSource.Token);
                        continue;
                    }

                    // Check resource availability
                    if (!AreResourcesAvailable(queuedTransfer))
                    {
                        // Re-queue for later
                        await Task.Delay(2000, _cancellationTokenSource.Token);
                        await _transferQueue.Writer.WriteAsync(queuedTransfer, _cancellationTokenSource.Token);
                        continue;
                    }

                    // Wait for available slot
                    await _concurrencyLimiter.WaitAsync(_cancellationTokenSource.Token);

                    try
                    {
                        await StartTransferAsync(queuedTransfer);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error starting transfer {queuedTransfer.TransferId}: {ex.Message}");
                        _concurrencyLimiter.Release();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in transfer queue processor: {ex.Message}");
            }
        }

        /// <summary>
        /// Processes scheduled transfers
        /// </summary>
        private void ProcessScheduledTransfers(object state)
        {
            try
            {
                var now = DateTime.Now;
                var readyTransfers = _queuedTransfers.Values
                    .Where(t => t.Status == TransferQueueStatus.Scheduled && t.ScheduledFor <= now)
                    .OrderBy(t => t.Priority)
                    .ThenBy(t => t.ScheduledFor)
                    .ToList();

                foreach (var transfer in readyTransfers)
                {
                    transfer.Status = TransferQueueStatus.Queued;
                    _ = Task.Run(async () =>
                    {
                        await _transferQueue.Writer.WriteAsync(transfer, _cancellationTokenSource.Token);
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing scheduled transfers: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts a transfer
        /// </summary>
        private async Task StartTransferAsync(QueuedTransfer queuedTransfer)
        {
            var activeTransfer = new ActiveTransfer
            {
                TransferId = queuedTransfer.TransferId,
                QueuedTransfer = queuedTransfer,
                StartedAt = DateTime.Now,
                CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token)
            };

            _activeTransfers[queuedTransfer.TransferId] = activeTransfer;
            _queuedTransfers.TryRemove(queuedTransfer.TransferId, out _);

            OnTransferStarted(new TransferStartedEventArgs
            {
                TransferId = queuedTransfer.TransferId,
                StartedAt = activeTransfer.StartedAt,
                Priority = queuedTransfer.Priority
            });

            // Start the actual transfer (this would integrate with the transfer engines)
            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteTransferAsync(activeTransfer);
                }
                finally
                {
                    _concurrencyLimiter.Release();
                    _activeTransfers.TryRemove(queuedTransfer.TransferId, out _);
                }
            }, activeTransfer.CancellationTokenSource.Token);
        }

        /// <summary>
        /// Executes the actual transfer
        /// </summary>
        private async Task ExecuteTransferAsync(ActiveTransfer activeTransfer)
        {
            var success = false;
            Exception exception = null;

            try
            {
                // This would integrate with the actual transfer engines
                // For now, simulate transfer execution
                var request = activeTransfer.QueuedTransfer.Request;
                
                if (request.TransferType == TransferType.Upload)
                {
                    success = await SimulateUploadAsync(activeTransfer);
                }
                else
                {
                    success = await SimulateDownloadAsync(activeTransfer);
                }
            }
            catch (Exception ex)
            {
                exception = ex;
                success = false;
            }

            OnTransferCompleted(new TransferCompletedEventArgs
            {
                TransferId = activeTransfer.TransferId,
                Success = success,
                CompletedAt = DateTime.Now,
                Duration = DateTime.Now - activeTransfer.StartedAt,
                Exception = exception
            });
        }

        /// <summary>
        /// Simulates upload execution
        /// </summary>
        private async Task<bool> SimulateUploadAsync(ActiveTransfer activeTransfer)
        {
            // Simulate upload process
            await Task.Delay(5000, activeTransfer.CancellationTokenSource.Token);
            return true;
        }

        /// <summary>
        /// Simulates download execution
        /// </summary>
        private async Task<bool> SimulateDownloadAsync(ActiveTransfer activeTransfer)
        {
            // Simulate download process
            await Task.Delay(3000, activeTransfer.CancellationTokenSource.Token);
            return true;
        }

        /// <summary>
        /// Checks if transfer can be executed immediately
        /// </summary>
        private bool CanExecuteImmediately(QueuedTransfer transfer)
        {
            return transfer.ScheduledFor <= DateTime.Now &&
                   AreDependenciesSatisfied(transfer) &&
                   AreResourcesAvailable(transfer);
        }

        /// <summary>
        /// Checks if transfer dependencies are satisfied
        /// </summary>
        private bool AreDependenciesSatisfied(QueuedTransfer transfer)
        {
            if (transfer.Dependencies == null || !transfer.Dependencies.Any())
                return true;

            // Check if all dependencies are completed
            foreach (var dependencyId in transfer.Dependencies)
            {
                if (_queuedTransfers.ContainsKey(dependencyId) || _activeTransfers.ContainsKey(dependencyId))
                {
                    return false; // Dependency still pending or active
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if required resources are available
        /// </summary>
        private bool AreResourcesAvailable(QueuedTransfer transfer)
        {
            var requirements = transfer.ResourceRequirements;
            
            // Check bandwidth requirements
            var currentBandwidthUsage = _activeTransfers.Values
                .Sum(t => t.QueuedTransfer.ResourceRequirements.BandwidthRequirement);
            
            if (currentBandwidthUsage + requirements.BandwidthRequirement > _config.MaxTotalBandwidth)
            {
                return false;
            }

            // Check memory requirements
            var currentMemoryUsage = _activeTransfers.Values
                .Sum(t => t.QueuedTransfer.ResourceRequirements.MemoryRequirement);
            
            if (currentMemoryUsage + requirements.MemoryRequirement > _config.MaxTotalMemory)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets the position of a transfer in the queue
        /// </summary>
        private int GetQueuePosition(string transferId)
        {
            var queuedTransfers = _queuedTransfers.Values
                .Where(t => t.Status == TransferQueueStatus.Queued)
                .OrderBy(t => t.Priority)
                .ThenBy(t => t.QueuedAt)
                .ToList();

            for (int i = 0; i < queuedTransfers.Count; i++)
            {
                if (queuedTransfers[i].TransferId == transferId)
                {
                    return i + 1;
                }
            }

            return -1;
        }

        private void OnTransferQueued(TransferQueuedEventArgs args)
        {
            TransferQueued?.Invoke(this, args);
        }

        private void OnTransferStarted(TransferStartedEventArgs args)
        {
            TransferStarted?.Invoke(this, args);
        }

        private void OnTransferCompleted(TransferCompletedEventArgs args)
        {
            TransferCompleted?.Invoke(this, args);
        }

        private void OnQueueStatusChanged(QueueStatusChangedEventArgs args)
        {
            QueueStatusChanged?.Invoke(this, args);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cancellationTokenSource.Cancel();
                _schedulerTimer?.Dispose();
                _concurrencyLimiter?.Dispose();
                _cancellationTokenSource?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Transfer queue configuration
    /// </summary>
    public class TransferQueueConfiguration
    {
        public int MaxConcurrentTransfers { get; set; } = 5;
        public long MaxTotalBandwidth { get; set; } = 100 * 1024 * 1024; // 100 MB/s
        public long MaxTotalMemory { get; set; } = 1024 * 1024 * 1024; // 1 GB
        public TimeSpan DefaultScheduleDelay { get; set; } = TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// Transfer queue request
    /// </summary>
    public class TransferQueueRequest
    {
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public TransferType TransferType { get; set; }
        public TransferPriority Priority { get; set; } = TransferPriority.Normal;
        public DateTime? ScheduledFor { get; set; }
        public List<string> Dependencies { get; set; }
        public ResourceRequirements ResourceRequirements { get; set; }
        public string SourcePath { get; set; }
        public string DestinationPath { get; set; }
        public string RemoteEndpoint { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Resource requirements for a transfer
    /// </summary>
    public class ResourceRequirements
    {
        public long BandwidthRequirement { get; set; } = 10 * 1024 * 1024; // 10 MB/s default
        public long MemoryRequirement { get; set; } = 64 * 1024 * 1024; // 64 MB default
        public int CpuCores { get; set; } = 1;
    }

    /// <summary>
    /// Queued transfer information
    /// </summary>
    internal class QueuedTransfer
    {
        public string TransferId { get; set; }
        public TransferQueueRequest Request { get; set; }
        public TransferPriority Priority { get; set; }
        public DateTime QueuedAt { get; set; }
        public DateTime ScheduledFor { get; set; }
        public TransferQueueStatus Status { get; set; }
        public List<string> Dependencies { get; set; }
        public ResourceRequirements ResourceRequirements { get; set; }
    }

    /// <summary>
    /// Active transfer information
    /// </summary>
    internal class ActiveTransfer
    {
        public string TransferId { get; set; }
        public QueuedTransfer QueuedTransfer { get; set; }
        public DateTime StartedAt { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; }
    }

    /// <summary>
    /// Queue status information
    /// </summary>
    public class QueueStatus
    {
        public int QueuedTransfers { get; set; }
        public int ActiveTransfers { get; set; }
        public int ScheduledTransfers { get; set; }
        public int AvailableSlots { get; set; }
        public int TotalCapacity { get; set; }
    }

    /// <summary>
    /// Transfer type enumeration
    /// </summary>
    public enum TransferType
    {
        Upload,
        Download
    }

    /// <summary>
    /// Transfer priority enumeration
    /// </summary>
    public enum TransferPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    /// <summary>
    /// Transfer queue status enumeration
    /// </summary>
    public enum TransferQueueStatus
    {
        Queued,
        Scheduled,
        Active,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Transfer queued event arguments
    /// </summary>
    public class TransferQueuedEventArgs : EventArgs
    {
        public string TransferId { get; set; }
        public TransferPriority Priority { get; set; }
        public DateTime ScheduledFor { get; set; }
        public int QueuePosition { get; set; }
    }

    /// <summary>
    /// Transfer started event arguments
    /// </summary>
    public class TransferStartedEventArgs : EventArgs
    {
        public string TransferId { get; set; }
        public DateTime StartedAt { get; set; }
        public TransferPriority Priority { get; set; }
    }

    /// <summary>
    /// Transfer completed event arguments
    /// </summary>
    public class TransferCompletedEventArgs : EventArgs
    {
        public string TransferId { get; set; }
        public bool Success { get; set; }
        public DateTime CompletedAt { get; set; }
        public TimeSpan Duration { get; set; }
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// Queue status changed event arguments
    /// </summary>
    public class QueueStatusChangedEventArgs : EventArgs
    {
        public QueueStatus Status { get; set; }
    }
}
