using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Shared.Platform;

namespace Shared.Protocol
{
    /// <summary>
    /// Multi-threaded transfer engine supporting concurrent uploads/downloads with proper resource management
    /// </summary>
    public class ConcurrentTransferEngine : IDisposable
    {
        private static ConcurrentTransferEngine _instance;
        private readonly ConcurrentDictionary<string, ConcurrentTransferSession> _activeSessions;
        private readonly SemaphoreSlim _concurrencyLimiter;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly TransferEngineConfiguration _config;
        private readonly object _lockObject = new object();
        private bool _disposed;

        public static ConcurrentTransferEngine Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ConcurrentTransferEngine();
                }
                return _instance;
            }
        }

        public event EventHandler<ConcurrentTransferProgressEventArgs> TransferProgress;
        public event EventHandler<ConcurrentTransferCompletedEventArgs> TransferCompleted;
        public event EventHandler<TransferErrorEventArgs> TransferError;

        private ConcurrentTransferEngine()
        {
            _activeSessions = new ConcurrentDictionary<string, ConcurrentTransferSession>();
            _cancellationTokenSource = new CancellationTokenSource();
            _config = new TransferEngineConfiguration();
            
            // Limit concurrent transfers to prevent resource exhaustion
            _concurrencyLimiter = new SemaphoreSlim(_config.MaxConcurrentTransfers, _config.MaxConcurrentTransfers);
        }

        /// <summary>
        /// Starts a concurrent file transfer
        /// </summary>
        public async Task<string> StartConcurrentTransferAsync(ConcurrentTransferRequest request)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConcurrentTransferEngine));

            var sessionId = Guid.NewGuid().ToString();
            var session = new ConcurrentTransferSession
            {
                SessionId = sessionId,
                Request = request,
                StartTime = DateTime.Now,
                Status = ConcurrentTransferStatus.Initializing,
                CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token),
                WorkerTasks = new List<Task>(),
                ChunkQueue = Channel.CreateUnbounded<ChunkWorkItem>(),
                CompletedChunks = new ConcurrentBag<int>(),
                FailedChunks = new ConcurrentBag<int>(),
                Statistics = new ConcurrentTransferStatistics()
            };

            _activeSessions[sessionId] = session;

            try
            {
                // Initialize the transfer session
                await InitializeConcurrentSessionAsync(session);
                
                // Start worker tasks for parallel chunk processing
                await StartWorkerTasksAsync(session);
                
                session.Status = ConcurrentTransferStatus.Active;
                
                // Start monitoring task
                _ = Task.Run(() => MonitorTransferProgressAsync(session), session.CancellationTokenSource.Token);
                
                System.Diagnostics.Debug.WriteLine($"Started concurrent transfer {sessionId} with {_config.WorkerThreadCount} workers");
                
                return sessionId;
            }
            catch (Exception ex)
            {
                session.Status = ConcurrentTransferStatus.Failed;
                session.ErrorMessage = ex.Message;
                OnTransferError(new TransferErrorEventArgs
                {
                    SessionId = sessionId,
                    ErrorMessage = ex.Message,
                    Exception = ex
                });
                throw;
            }
        }

        /// <summary>
        /// Cancels a concurrent transfer
        /// </summary>
        public async Task CancelTransferAsync(string sessionId)
        {
            if (_activeSessions.TryGetValue(sessionId, out var session))
            {
                session.CancellationTokenSource.Cancel();
                session.Status = ConcurrentTransferStatus.Cancelled;
                
                // Wait for all worker tasks to complete
                await Task.WhenAll(session.WorkerTasks);
                
                _activeSessions.TryRemove(sessionId, out _);
                
                OnTransferCompleted(new ConcurrentTransferCompletedEventArgs
                {
                    SessionId = sessionId,
                    Success = false,
                    Cancelled = true
                });
            }
        }

        /// <summary>
        /// Gets the status of a concurrent transfer
        /// </summary>
        public ConcurrentTransferStatus GetTransferStatus(string sessionId)
        {
            return _activeSessions.TryGetValue(sessionId, out var session) 
                ? session.Status 
                : ConcurrentTransferStatus.NotFound;
        }

        /// <summary>
        /// Initializes a concurrent transfer session
        /// </summary>
        private async Task InitializeConcurrentSessionAsync(ConcurrentTransferSession session)
        {
            var request = session.Request;
            
            // Calculate optimal chunk size and count
            var chunkSize = CalculateOptimalChunkSize(request.FileSize);
            var totalChunks = (int)Math.Ceiling((double)request.FileSize / chunkSize);
            
            session.ChunkSize = chunkSize;
            session.TotalChunks = totalChunks;
            
            // Create chunk work items
            var writer = session.ChunkQueue.Writer;
            for (int i = 0; i < totalChunks; i++)
            {
                var chunkWorkItem = new ChunkWorkItem
                {
                    ChunkIndex = i,
                    Offset = i * chunkSize,
                    Size = Math.Min(chunkSize, (int)(request.FileSize - (i * chunkSize))),
                    Attempts = 0,
                    MaxAttempts = _config.MaxRetryAttempts
                };
                
                await writer.WriteAsync(chunkWorkItem);
            }
            
            writer.Complete();
        }

        /// <summary>
        /// Starts worker tasks for parallel chunk processing
        /// </summary>
        private async Task StartWorkerTasksAsync(ConcurrentTransferSession session)
        {
            var workerCount = Math.Min(_config.WorkerThreadCount, session.TotalChunks);
            
            for (int i = 0; i < workerCount; i++)
            {
                var workerId = i;
                var workerTask = Task.Run(async () =>
                {
                    await ProcessChunksAsync(session, workerId);
                }, session.CancellationTokenSource.Token);
                
                session.WorkerTasks.Add(workerTask);
            }
        }

        /// <summary>
        /// Processes chunks in a worker task
        /// </summary>
        private async Task ProcessChunksAsync(ConcurrentTransferSession session, int workerId)
        {
            var reader = session.ChunkQueue.Reader;
            var cancellationToken = session.CancellationTokenSource.Token;
            
            try
            {
                await foreach (var chunkWorkItem in reader.ReadAllAsync(cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    
                    // Acquire concurrency semaphore
                    await _concurrencyLimiter.WaitAsync(cancellationToken);
                    
                    try
                    {
                        var success = await ProcessSingleChunkAsync(session, chunkWorkItem, workerId);
                        
                        if (success)
                        {
                            session.CompletedChunks.Add(chunkWorkItem.ChunkIndex);
                            Interlocked.Increment(ref session.Statistics.CompletedChunkCount);
                        }
                        else
                        {
                            session.FailedChunks.Add(chunkWorkItem.ChunkIndex);
                            Interlocked.Increment(ref session.Statistics.FailedChunkCount);
                            
                            // Retry logic
                            if (chunkWorkItem.Attempts < chunkWorkItem.MaxAttempts)
                            {
                                chunkWorkItem.Attempts++;
                                await session.ChunkQueue.Writer.WriteAsync(chunkWorkItem, cancellationToken);
                            }
                        }
                    }
                    finally
                    {
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
                System.Diagnostics.Debug.WriteLine($"Worker {workerId} error: {ex.Message}");
                OnTransferError(new TransferErrorEventArgs
                {
                    SessionId = session.SessionId,
                    ErrorMessage = $"Worker {workerId} error: {ex.Message}",
                    Exception = ex
                });
            }
        }

        /// <summary>
        /// Processes a single chunk
        /// </summary>
        private async Task<bool> ProcessSingleChunkAsync(ConcurrentTransferSession session, ChunkWorkItem chunkWorkItem, int workerId)
        {
            try
            {
                var startTime = DateTime.Now;
                
                // Simulate chunk processing (replace with actual transfer logic)
                if (session.Request.Direction == TransferDirection.Upload)
                {
                    // Upload chunk logic
                    await SimulateChunkUploadAsync(session, chunkWorkItem);
                }
                else
                {
                    // Download chunk logic
                    await SimulateChunkDownloadAsync(session, chunkWorkItem);
                }
                
                var endTime = DateTime.Now;
                var duration = endTime - startTime;
                
                // Update statistics
                Interlocked.Add(ref session.Statistics.TotalBytesTransferred, chunkWorkItem.Size);
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Chunk {chunkWorkItem.ChunkIndex} processing error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Simulates chunk upload (replace with actual implementation)
        /// </summary>
        private async Task SimulateChunkUploadAsync(ConcurrentTransferSession session, ChunkWorkItem chunkWorkItem)
        {
            // Simulate network delay
            await Task.Delay(50 + new Random().Next(100));
            
            // TODO: Implement actual chunk upload logic
            // This would involve reading the chunk from the source file and sending it via HTTP
        }

        /// <summary>
        /// Simulates chunk download (replace with actual implementation)
        /// </summary>
        private async Task SimulateChunkDownloadAsync(ConcurrentTransferSession session, ChunkWorkItem chunkWorkItem)
        {
            // Simulate network delay
            await Task.Delay(50 + new Random().Next(100));
            
            // TODO: Implement actual chunk download logic
            // This would involve downloading the chunk via HTTP and writing it to the destination file
        }

        /// <summary>
        /// Monitors transfer progress and fires events
        /// </summary>
        private async Task MonitorTransferProgressAsync(ConcurrentTransferSession session)
        {
            var lastProgressUpdate = DateTime.Now;
            
            while (session.Status == ConcurrentTransferStatus.Active && !session.CancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, session.CancellationTokenSource.Token); // Update every second
                    
                    var now = DateTime.Now;
                    var completedCount = session.Statistics.CompletedChunkCount;
                    var failedCount = session.Statistics.FailedChunkCount;
                    var totalCount = session.TotalChunks;
                    
                    // Check if transfer is complete
                    if (completedCount + failedCount >= totalCount)
                    {
                        var success = failedCount == 0;
                        session.Status = success ? ConcurrentTransferStatus.Completed : ConcurrentTransferStatus.Failed;
                        session.EndTime = now;
                        
                        OnTransferCompleted(new ConcurrentTransferCompletedEventArgs
                        {
                            SessionId = session.SessionId,
                            Success = success,
                            Duration = session.EndTime - session.StartTime,
                            TotalBytes = session.Request.FileSize,
                            CompletedChunks = completedCount,
                            FailedChunks = failedCount
                        });
                        
                        // Cleanup
                        _activeSessions.TryRemove(session.SessionId, out _);
                        break;
                    }
                    
                    // Fire progress event
                    if (now - lastProgressUpdate >= TimeSpan.FromMilliseconds(500)) // Throttle progress updates
                    {
                        var progress = (double)completedCount / totalCount * 100;
                        var bytesTransferred = session.Statistics.TotalBytesTransferred;
                        var elapsed = now - session.StartTime;
                        var speed = elapsed.TotalSeconds > 0 ? bytesTransferred / elapsed.TotalSeconds : 0;
                        
                        OnTransferProgress(new ConcurrentTransferProgressEventArgs
                        {
                            SessionId = session.SessionId,
                            Progress = progress,
                            CompletedChunks = completedCount,
                            TotalChunks = totalCount,
                            BytesTransferred = bytesTransferred,
                            TotalBytes = session.Request.FileSize,
                            TransferSpeed = speed,
                            ElapsedTime = elapsed,
                            ActiveWorkers = session.WorkerTasks.Count(t => !t.IsCompleted)
                        });
                        
                        lastProgressUpdate = now;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Progress monitoring error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Calculates optimal chunk size based on file size
        /// </summary>
        private int CalculateOptimalChunkSize(long fileSize)
        {
            // Use larger chunks for larger files to reduce overhead
            if (fileSize < 1024 * 1024) // < 1MB
                return 64 * 1024; // 64KB
            if (fileSize < 100 * 1024 * 1024) // < 100MB
                return 1024 * 1024; // 1MB
            if (fileSize < 1024 * 1024 * 1024) // < 1GB
                return 4 * 1024 * 1024; // 4MB
            
            return 8 * 1024 * 1024; // 8MB for very large files
        }

        private void OnTransferProgress(ConcurrentTransferProgressEventArgs args)
        {
            TransferProgress?.Invoke(this, args);
        }

        private void OnTransferCompleted(ConcurrentTransferCompletedEventArgs args)
        {
            TransferCompleted?.Invoke(this, args);
        }

        private void OnTransferError(TransferErrorEventArgs args)
        {
            TransferError?.Invoke(this, args);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cancellationTokenSource.Cancel();

                // Wait for all active sessions to complete
                var allTasks = _activeSessions.Values.SelectMany(s => s.WorkerTasks).ToArray();
                Task.WaitAll(allTasks, TimeSpan.FromSeconds(10));

                _concurrencyLimiter?.Dispose();
                _cancellationTokenSource?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Configuration for the concurrent transfer engine
    /// </summary>
    public class TransferEngineConfiguration
    {
        public int MaxConcurrentTransfers { get; set; } = 10;
        public int WorkerThreadCount { get; set; } = Environment.ProcessorCount;
        public int MaxRetryAttempts { get; set; } = 3;
        public TimeSpan ProgressUpdateInterval { get; set; } = TimeSpan.FromMilliseconds(500);
    }

    /// <summary>
    /// Concurrent transfer request
    /// </summary>
    public class ConcurrentTransferRequest
    {
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public TransferDirection Direction { get; set; }
        public string SourcePath { get; set; }
        public string DestinationPath { get; set; }
        public string RemoteEndpoint { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Concurrent transfer session
    /// </summary>
    internal class ConcurrentTransferSession
    {
        public string SessionId { get; set; }
        public ConcurrentTransferRequest Request { get; set; }
        public ConcurrentTransferStatus Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string ErrorMessage { get; set; }
        public int ChunkSize { get; set; }
        public int TotalChunks { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; }
        public List<Task> WorkerTasks { get; set; }
        public Channel<ChunkWorkItem> ChunkQueue { get; set; }
        public ConcurrentBag<int> CompletedChunks { get; set; }
        public ConcurrentBag<int> FailedChunks { get; set; }
        public ConcurrentTransferStatistics Statistics { get; set; }
    }

    /// <summary>
    /// Chunk work item for processing
    /// </summary>
    internal class ChunkWorkItem
    {
        public int ChunkIndex { get; set; }
        public long Offset { get; set; }
        public int Size { get; set; }
        public int Attempts { get; set; }
        public int MaxAttempts { get; set; }
    }

    /// <summary>
    /// Concurrent transfer statistics
    /// </summary>
    internal class ConcurrentTransferStatistics
    {
        public long TotalBytesTransferred;
        public int CompletedChunkCount;
        public int FailedChunkCount;
    }

    /// <summary>
    /// Concurrent transfer status
    /// </summary>
    public enum ConcurrentTransferStatus
    {
        NotFound,
        Initializing,
        Active,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Concurrent transfer progress event arguments
    /// </summary>
    public class ConcurrentTransferProgressEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public double Progress { get; set; }
        public int CompletedChunks { get; set; }
        public int TotalChunks { get; set; }
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public double TransferSpeed { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public int ActiveWorkers { get; set; }
    }

    /// <summary>
    /// Concurrent transfer completed event arguments
    /// </summary>
    public class ConcurrentTransferCompletedEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public bool Success { get; set; }
        public bool Cancelled { get; set; }
        public TimeSpan Duration { get; set; }
        public long TotalBytes { get; set; }
        public int CompletedChunks { get; set; }
        public int FailedChunks { get; set; }
    }

    /// <summary>
    /// Transfer error event arguments
    /// </summary>
    public class TransferErrorEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
    }
}
