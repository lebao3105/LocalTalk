using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using Shared.Platform;
using Shared.FileSystem;

namespace Shared.Protocol
{
    /// <summary>
    /// Chunked file transfer protocol with parallel transfers and out-of-order delivery support
    /// </summary>
    public class ChunkedTransferProtocol
    {
        private static ChunkedTransferProtocol _instance;
        private readonly ConcurrentDictionary<string, TransferSession> _activeSessions;
        private readonly ChunkManager _chunkManager;
        private readonly TransferConfiguration _config;

        public static ChunkedTransferProtocol Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ChunkedTransferProtocol();
                }
                return _instance;
            }
        }

        public event EventHandler<TransferProgressEventArgs> TransferProgress;
        public event EventHandler<ChunkTransferEventArgs> ChunkTransferred;
        public event EventHandler<TransferCompletedEventArgs> TransferCompleted;

        private ChunkedTransferProtocol()
        {
            _activeSessions = new ConcurrentDictionary<string, TransferSession>();
            _chunkManager = new ChunkManager();
            _config = new TransferConfiguration();

            // Subscribe to our own events to update the real-time tracker
            TransferProgress += OnTransferProgressForTracker;
            TransferCompleted += OnTransferCompletedForTracker;
        }

        /// <summary>
        /// Starts a new file transfer session
        /// </summary>
        /// <param name="request">The transfer request containing file and transfer details</param>
        /// <returns>A new transfer session</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null</exception>
        /// <exception cref="ArgumentException">Thrown when request contains invalid data</exception>
        /// <exception cref="InvalidOperationException">Thrown when the protocol is not properly initialized</exception>
        public async Task<TransferSession> StartTransferAsync(TransferRequest request)
        {
            // Input validation
            if (request == null)
                throw new ArgumentNullException(nameof(request), "Transfer request cannot be null");

            if (request.FileSize <= 0)
                throw new ArgumentException("File size must be greater than zero", nameof(request));

            if (request.SourceFile == null && request.Direction == TransferDirection.Upload)
                throw new ArgumentException("Source file is required for upload transfers", nameof(request));

            if (string.IsNullOrWhiteSpace(request.DestinationPath) && request.Direction == TransferDirection.Download)
                throw new ArgumentException("Destination path is required for download transfers", nameof(request));

            if (_config == null)
                throw new InvalidOperationException("ChunkedTransferProtocol is not properly initialized - configuration is null");

            var sessionId = Guid.NewGuid().ToString();
            var session = new TransferSession
            {
                SessionId = sessionId,
                Request = request,
                StartTime = DateTime.Now,
                Status = TransferStatus.Initializing,
                ChunkSize = DetermineOptimalChunkSize(request.FileSize),
                TotalChunks = CalculateTotalChunks(request.FileSize, request.ChunkSize ?? _config.DefaultChunkSize)
            };

            // Initialize chunk tracking with optimized capacity
            session.ChunkStates = new ConcurrentDictionary<int, ChunkState>(
                Environment.ProcessorCount, session.TotalChunks);

            // Use parallel initialization for large chunk counts to improve startup performance
            if (session.TotalChunks > 1000)
            {
                Parallel.For(0, session.TotalChunks, i =>
                {
                    session.ChunkStates[i] = ChunkState.Pending;
                });
            }
            else
            {
                // Use simple loop for smaller chunk counts to avoid parallel overhead
                for (int i = 0; i < session.TotalChunks; i++)
                {
                    session.ChunkStates[i] = ChunkState.Pending;
                }
            }

            _activeSessions[sessionId] = session;

            try
            {
                if (request.Direction == TransferDirection.Upload)
                {
                    await InitializeUploadSessionAsync(session);
                }
                else
                {
                    await InitializeDownloadSessionAsync(session);
                }

                session.Status = TransferStatus.Active;

                // Start real-time progress tracking
                RealTimeProgressTracker.Instance.StartTracking(sessionId, request.FileName, request.FileSize);

                System.Diagnostics.Debug.WriteLine($"Started transfer session {sessionId} for {request.FileName}");
            }
            catch (Exception ex)
            {
                session.Status = TransferStatus.Failed;
                session.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Failed to start transfer session {sessionId}: {ex.Message}");
            }

            return session;
        }

        /// <summary>
        /// Transfers a file chunk
        /// </summary>
        /// <param name="sessionId">The session identifier</param>
        /// <param name="chunkIndex">The index of the chunk to transfer</param>
        /// <param name="chunkData">The chunk data (for uploads)</param>
        /// <returns>The result of the chunk transfer operation</returns>
        /// <exception cref="ArgumentNullException">Thrown when sessionId is null or empty</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when chunkIndex is negative</exception>
        public async Task<ChunkTransferResult> TransferChunkAsync(string sessionId, int chunkIndex,
            byte[] chunkData = null)
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentNullException(nameof(sessionId), "Session ID cannot be null or empty");

            if (chunkIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(chunkIndex), "Chunk index cannot be negative");

            if (!_activeSessions.TryGetValue(sessionId, out var session))
            {
                return new ChunkTransferResult
                {
                    Success = false,
                    ErrorMessage = "Session not found",
                    SessionId = sessionId,
                    ChunkIndex = chunkIndex,
                    Timestamp = DateTime.Now
                };
            }

            // Validate chunk index is within bounds
            if (chunkIndex >= session.TotalChunks)
            {
                return new ChunkTransferResult
                {
                    Success = false,
                    ErrorMessage = $"Chunk index {chunkIndex} is out of range (total chunks: {session.TotalChunks})",
                    SessionId = sessionId,
                    ChunkIndex = chunkIndex,
                    Timestamp = DateTime.Now
                };
            }

            var result = new ChunkTransferResult
            {
                SessionId = sessionId,
                ChunkIndex = chunkIndex,
                Timestamp = DateTime.Now
            };

            var chunkStartTime = DateTime.Now;

            try
            {
                if (session.Request.Direction == TransferDirection.Upload)
                {
                    result = await UploadChunkAsync(session, chunkIndex, chunkData);
                }
                else
                {
                    result = await DownloadChunkAsync(session, chunkIndex);
                }

                var chunkEndTime = DateTime.Now;
                var chunkTransferTime = chunkEndTime - chunkStartTime;

                // Update chunk state and session statistics
                if (result.Success)
                {
                    session.ChunkStates[chunkIndex] = ChunkState.Completed;
                    session.CompletedChunks++;
                    session.LastActivity = chunkEndTime;

                    // Update transfer statistics for ETA calculation
                    UpdateTransferStatistics(session, result.ChunkData?.Length ?? 0, chunkTransferTime);

                    // Calculate and fire enhanced progress event
                    var progressArgs = CalculateEnhancedProgress(session);
                    OnTransferProgress(progressArgs);

                    // Check if transfer is complete
                    if (session.CompletedChunks == session.TotalChunks)
                    {
                        await CompleteTransferAsync(session);
                    }
                }
                else
                {
                    session.ChunkStates[chunkIndex] = ChunkState.Failed;
                    session.FailedChunks++;
                }

                OnChunkTransferred(new ChunkTransferEventArgs
                {
                    SessionId = sessionId,
                    ChunkIndex = chunkIndex,
                    Success = result.Success,
                    ChunkSize = result.ChunkData?.Length ?? 0,
                    ErrorMessage = result.ErrorMessage,
                    TransferTime = chunkTransferTime
                });
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                session.ChunkStates[chunkIndex] = ChunkState.Failed;
                session.FailedChunks++;
                System.Diagnostics.Debug.WriteLine($"Chunk transfer error: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Transfers multiple chunks in parallel
        /// </summary>
        /// <param name="sessionId">The session identifier</param>
        /// <param name="chunkRequests">The list of chunk transfer requests</param>
        /// <returns>A list of chunk transfer results</returns>
        /// <exception cref="ArgumentNullException">Thrown when sessionId or chunkRequests is null</exception>
        /// <exception cref="ArgumentException">Thrown when chunkRequests is empty or contains invalid data</exception>
        public async Task<List<ChunkTransferResult>> TransferChunksParallelAsync(string sessionId,
            List<ChunkTransferRequest> chunkRequests)
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentNullException(nameof(sessionId), "Session ID cannot be null or empty");

            if (chunkRequests == null)
                throw new ArgumentNullException(nameof(chunkRequests), "Chunk requests cannot be null");

            if (chunkRequests.Count == 0)
                throw new ArgumentException("Chunk requests cannot be empty", nameof(chunkRequests));

            // Validate all chunk requests
            for (int i = 0; i < chunkRequests.Count; i++)
            {
                var request = chunkRequests[i];
                if (request == null)
                    throw new ArgumentException($"Chunk request at index {i} cannot be null", nameof(chunkRequests));

                if (request.ChunkIndex < 0)
                    throw new ArgumentException($"Chunk index at request {i} cannot be negative", nameof(chunkRequests));
            }

            if (!_activeSessions.TryGetValue(sessionId, out var session))
            {
                return chunkRequests.Select(r => new ChunkTransferResult
                {
                    Success = false,
                    ErrorMessage = "Session not found",
                    ChunkIndex = r.ChunkIndex,
                    SessionId = sessionId,
                    Timestamp = DateTime.Now
                }).ToList();
            }

            // Validate all chunk indices are within bounds
            var invalidRequests = chunkRequests.Where(r => r.ChunkIndex >= session.TotalChunks).ToList();
            if (invalidRequests.Any())
            {
                return chunkRequests.Select(r => new ChunkTransferResult
                {
                    Success = false,
                    ErrorMessage = invalidRequests.Contains(r)
                        ? $"Chunk index {r.ChunkIndex} is out of range (total chunks: {session.TotalChunks})"
                        : "Some chunk indices are out of range",
                    ChunkIndex = r.ChunkIndex,
                    SessionId = sessionId,
                    Timestamp = DateTime.Now
                }).ToList();
            }

            var maxParallelism = Math.Min(chunkRequests.Count, _config.MaxParallelChunks);
            var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);
            var tasks = new List<Task<ChunkTransferResult>>();

            foreach (var chunkRequest in chunkRequests)
            {
                tasks.Add(TransferChunkWithSemaphoreAsync(sessionId, chunkRequest, semaphore));
            }

            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        /// <summary>
        /// Gets the status of a transfer session
        /// </summary>
        /// <param name="sessionId">The session identifier</param>
        /// <returns>The transfer session if found, null otherwise</returns>
        /// <exception cref="ArgumentNullException">Thrown when sessionId is null or empty</exception>
        public TransferSession GetTransferStatus(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentNullException(nameof(sessionId), "Session ID cannot be null or empty");

            _activeSessions.TryGetValue(sessionId, out var session);
            return session;
        }

        /// <summary>
        /// Cancels a transfer session
        /// </summary>
        /// <param name="sessionId">The session identifier</param>
        /// <returns>True if the session was successfully cancelled, false if session was not found</returns>
        /// <exception cref="ArgumentNullException">Thrown when sessionId is null or empty</exception>
        public async Task<bool> CancelTransferAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentNullException(nameof(sessionId), "Session ID cannot be null or empty");

            if (!_activeSessions.TryGetValue(sessionId, out var session))
                return false;

            try
            {
                session.Status = TransferStatus.Cancelled;
                session.CancellationToken?.Cancel();

                // Cleanup resources
                await CleanupSessionAsync(session);

                _activeSessions.TryRemove(sessionId, out _);

                System.Diagnostics.Debug.WriteLine($"Cancelled transfer session {sessionId}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error cancelling transfer {sessionId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets all active transfer sessions
        /// </summary>
        public List<TransferSession> GetActiveSessions()
        {
            return _activeSessions.Values.ToList();
        }

        /// <summary>
        /// Determines optimal chunk size based on file size and network conditions
        /// </summary>
        private int DetermineOptimalChunkSize(long fileSize)
        {
            var baseChunkSize = PlatformFactory.Features.DefaultChunkSize;

            // Adjust chunk size based on file size
            if (fileSize < 1024 * 1024) // < 1MB
            {
                return Math.Min(baseChunkSize, 16 * 1024); // Max 16KB for small files
            }
            else if (fileSize < 100 * 1024 * 1024) // < 100MB
            {
                return baseChunkSize; // Use default chunk size
            }
            else
            {
                return Math.Min(baseChunkSize * 2, 128 * 1024); // Max 128KB for large files
            }
        }

        /// <summary>
        /// Calculates total number of chunks needed
        /// </summary>
        private int CalculateTotalChunks(long fileSize, int chunkSize)
        {
            return (int)Math.Ceiling((double)fileSize / chunkSize);
        }

        /// <summary>
        /// Initializes an upload session
        /// </summary>
        private async Task InitializeUploadSessionAsync(TransferSession session)
        {
            // Create file reader for chunked reading
            var file = session.Request.SourceFile;
            session.FileReader = await LargeFileHandler.Instance.CreateReaderAsync(file);

            // Initialize chunk queue for upload
            session.PendingChunks = new ConcurrentQueue<int>();
            for (int i = 0; i < session.TotalChunks; i++)
            {
                session.PendingChunks.Enqueue(i);
            }
        }

        /// <summary>
        /// Initializes a download session
        /// </summary>
        private async Task InitializeDownloadSessionAsync(TransferSession session)
        {
            // Create file writer for chunked writing
            session.FileWriter = await LargeFileHandler.Instance.CreateWriterAsync(
                session.Request.DestinationPath);

            // Initialize received chunks buffer
            session.ReceivedChunks = new ConcurrentDictionary<int, byte[]>();
        }

        /// <summary>
        /// Uploads a single chunk
        /// </summary>
        private async Task<ChunkTransferResult> UploadChunkAsync(TransferSession session,
            int chunkIndex, byte[] chunkData)
        {
            var result = new ChunkTransferResult
            {
                SessionId = session.SessionId,
                ChunkIndex = chunkIndex
            };

            try
            {
                // If chunk data not provided, read from file
                if (chunkData == null)
                {
                    await session.FileReader.SeekAsync(chunkIndex * session.ChunkSize);
                    chunkData = await session.FileReader.ReadChunkAsync();
                }

                // Send chunk to destination (implementation would depend on transport layer)
                var success = await SendChunkToDestinationAsync(session, chunkIndex, chunkData);

                result.Success = success;
                result.ChunkData = chunkData;
                result.ChunkSize = chunkData.Length;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Downloads a single chunk
        /// </summary>
        private async Task<ChunkTransferResult> DownloadChunkAsync(TransferSession session, int chunkIndex)
        {
            var result = new ChunkTransferResult
            {
                SessionId = session.SessionId,
                ChunkIndex = chunkIndex
            };

            try
            {
                // Request chunk from source (implementation would depend on transport layer)
                var chunkData = await RequestChunkFromSourceAsync(session, chunkIndex);

                if (chunkData != null)
                {
                    // Store chunk for later assembly
                    session.ReceivedChunks[chunkIndex] = chunkData;

                    result.Success = true;
                    result.ChunkData = chunkData;
                    result.ChunkSize = chunkData.Length;
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "Failed to receive chunk data";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Transfers a chunk with semaphore control for parallel transfers
        /// </summary>
        private async Task<ChunkTransferResult> TransferChunkWithSemaphoreAsync(string sessionId,
            ChunkTransferRequest chunkRequest, SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            try
            {
                return await TransferChunkAsync(sessionId, chunkRequest.ChunkIndex, chunkRequest.ChunkData);
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Completes a transfer session
        /// </summary>
        private async Task CompleteTransferAsync(TransferSession session)
        {
            try
            {
                session.Status = TransferStatus.Completing;

                if (session.Request.Direction == TransferDirection.Download)
                {
                    // Assemble chunks in correct order for download
                    await AssembleDownloadedChunksAsync(session);
                }

                session.Status = TransferStatus.Completed;
                session.EndTime = DateTime.Now;
                session.Duration = session.EndTime - session.StartTime;

                OnTransferCompleted(new TransferCompletedEventArgs
                {
                    SessionId = session.SessionId,
                    Success = true,
                    Duration = session.Duration,
                    TotalBytes = session.Request.FileSize,
                    AverageSpeed = session.Request.FileSize / session.Duration.TotalSeconds
                });

                // Cleanup session
                await CleanupSessionAsync(session);
                _activeSessions.TryRemove(session.SessionId, out _);

                System.Diagnostics.Debug.WriteLine($"Completed transfer session {session.SessionId}");
            }
            catch (Exception ex)
            {
                session.Status = TransferStatus.Failed;
                session.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Error completing transfer {session.SessionId}: {ex}");
            }
        }

        /// <summary>
        /// Assembles downloaded chunks in correct order
        /// </summary>
        private async Task AssembleDownloadedChunksAsync(TransferSession session)
        {
            for (int i = 0; i < session.TotalChunks; i++)
            {
                if (session.ReceivedChunks.TryGetValue(i, out var chunkData))
                {
                    await session.FileWriter.WriteChunkAsync(chunkData);
                }
                else
                {
                    throw new InvalidOperationException($"Missing chunk {i} during assembly");
                }
            }

            await session.FileWriter.FlushAsync();
        }

        /// <summary>
        /// Sends a chunk to the destination (placeholder - would use actual transport)
        /// </summary>
        private async Task<bool> SendChunkToDestinationAsync(TransferSession session, int chunkIndex, byte[] chunkData)
        {
            // This is a placeholder implementation
            // In a real implementation, this would use HTTP, TCP, or other transport
            await Task.Delay(10); // Simulate network delay
            return true;
        }

        /// <summary>
        /// Requests a chunk from the source (placeholder - would use actual transport)
        /// </summary>
        private async Task<byte[]> RequestChunkFromSourceAsync(TransferSession session, int chunkIndex)
        {
            // This is a placeholder implementation
            // In a real implementation, this would use HTTP, TCP, or other transport
            await Task.Delay(10); // Simulate network delay
            return new byte[session.ChunkSize]; // Return dummy data
        }

        /// <summary>
        /// Cleans up session resources
        /// </summary>
        private async Task CleanupSessionAsync(TransferSession session)
        {
            await Task.Run(() =>
            {
                session.FileReader?.Dispose();
                session.FileWriter?.Dispose();
                session.CancellationToken?.Dispose();
            });
        }

        /// <summary>
        /// Raises the TransferProgress event
        /// </summary>
        private void OnTransferProgress(TransferProgressEventArgs args)
        {
            TransferProgress?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the ChunkTransferred event
        /// </summary>
        private void OnChunkTransferred(ChunkTransferEventArgs args)
        {
            ChunkTransferred?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the TransferCompleted event
        /// </summary>
        private void OnTransferCompleted(TransferCompletedEventArgs args)
        {
            TransferCompleted?.Invoke(this, args);
        }

        /// <summary>
        /// Updates transfer statistics for ETA calculation
        /// </summary>
        private void UpdateTransferStatistics(TransferSession session, int chunkSize, TimeSpan chunkTransferTime)
        {
            lock (session.StatisticsLock)
            {
                // Initialize statistics if needed
                if (session.TransferStatistics == null)
                {
                    session.TransferStatistics = new TransferStatistics
                    {
                        StartTime = session.StartTime,
                        ChunkTransferTimes = new List<TimeSpan>(),
                        ChunkSizes = new List<int>(),
                        SpeedSamples = new Queue<SpeedSample>()
                    };
                }

                var stats = session.TransferStatistics;

                // Add chunk transfer data
                stats.ChunkTransferTimes.Add(chunkTransferTime);
                stats.ChunkSizes.Add(chunkSize);

                // Calculate instantaneous speed
                var instantSpeed = chunkSize / chunkTransferTime.TotalSeconds;
                stats.SpeedSamples.Enqueue(new SpeedSample
                {
                    Speed = instantSpeed,
                    Timestamp = DateTime.Now
                });

                // Keep only recent speed samples (last 30 seconds)
                var cutoffTime = DateTime.Now.AddSeconds(-30);
                while (stats.SpeedSamples.Count > 0 && stats.SpeedSamples.Peek().Timestamp < cutoffTime)
                {
                    stats.SpeedSamples.Dequeue();
                }

                // Update running averages
                stats.TotalBytesTransferred += chunkSize;
                stats.LastUpdateTime = DateTime.Now;
            }
        }

        /// <summary>
        /// Calculates enhanced progress information with ETA
        /// </summary>
        private TransferProgressEventArgs CalculateEnhancedProgress(TransferSession session)
        {
            lock (session.StatisticsLock)
            {
                var stats = session.TransferStatistics;
                var now = DateTime.Now;
                var elapsed = now - session.StartTime;

                // Calculate progress percentage
                var progress = (double)session.CompletedChunks / session.TotalChunks * 100;

                // Calculate current transfer speed (average of recent samples)
                double currentSpeed = 0;
                if (stats?.SpeedSamples?.Count > 0)
                {
                    currentSpeed = stats.SpeedSamples.Average(s => s.Speed);
                }

                // Calculate overall average speed
                double averageSpeed = 0;
                if (elapsed.TotalSeconds > 0)
                {
                    averageSpeed = stats?.TotalBytesTransferred ?? 0 / elapsed.TotalSeconds;
                }

                // Calculate ETA using current speed
                TimeSpan estimatedTimeRemaining = TimeSpan.Zero;
                if (currentSpeed > 0)
                {
                    var remainingBytes = session.Request.FileSize - (stats?.TotalBytesTransferred ?? 0);
                    estimatedTimeRemaining = TimeSpan.FromSeconds(remainingBytes / currentSpeed);
                }

                return new TransferProgressEventArgs
                {
                    SessionId = session.SessionId,
                    Progress = progress,
                    CompletedChunks = session.CompletedChunks,
                    TotalChunks = session.TotalChunks,
                    BytesTransferred = stats?.TotalBytesTransferred ?? 0,
                    TotalBytes = session.Request.FileSize,
                    TransferSpeed = currentSpeed,
                    AverageSpeed = averageSpeed,
                    EstimatedTimeRemaining = estimatedTimeRemaining,
                    ElapsedTime = elapsed,
                    FailedChunks = session.FailedChunks,
                    LastUpdateTime = now
                };
            }
        }

        /// <summary>
        /// Event handler to update real-time progress tracker
        /// </summary>
        private void OnTransferProgressForTracker(object sender, TransferProgressEventArgs e)
        {
            RealTimeProgressTracker.Instance.UpdateProgress(e.SessionId, e);
        }

        /// <summary>
        /// Event handler to complete real-time progress tracking
        /// </summary>
        private void OnTransferCompletedForTracker(object sender, TransferCompletedEventArgs e)
        {
            RealTimeProgressTracker.Instance.CompleteTracking(e.SessionId, e.Success, e.ErrorMessage);
        }
    }

    /// <summary>
    /// Transfer request information
    /// </summary>
    public class TransferRequest
    {
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public TransferDirection Direction { get; set; }
        public IStorageFile SourceFile { get; set; }
        public string DestinationPath { get; set; }
        public int? ChunkSize { get; set; }
        public string RemoteEndpoint { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Transfer session information
    /// </summary>
    public class TransferSession
    {
        public string SessionId { get; set; }
        public TransferRequest Request { get; set; }
        public TransferStatus Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public DateTime LastActivity { get; set; }
        public TimeSpan Duration { get; set; }
        public string ErrorMessage { get; set; }
        public int ChunkSize { get; set; }
        public int TotalChunks { get; set; }
        public int CompletedChunks { get; set; }
        public int FailedChunks { get; set; }
        public ConcurrentDictionary<int, ChunkState> ChunkStates { get; set; }
        public ConcurrentQueue<int> PendingChunks { get; set; }
        public ConcurrentDictionary<int, byte[]> ReceivedChunks { get; set; }

        // Enhanced progress tracking properties
        public TransferStatistics TransferStatistics { get; set; }
        public readonly object StatisticsLock = new object();
        public LargeFileReader FileReader { get; set; }
        public LargeFileWriter FileWriter { get; set; }
        public CancellationTokenSource CancellationToken { get; set; }
    }

    /// <summary>
    /// Chunk transfer request
    /// </summary>
    public class ChunkTransferRequest
    {
        public int ChunkIndex { get; set; }
        public byte[] ChunkData { get; set; }
        public int Priority { get; set; } = 0;
    }

    /// <summary>
    /// Chunk transfer result
    /// </summary>
    public class ChunkTransferResult
    {
        public bool Success { get; set; }
        public string SessionId { get; set; }
        public int ChunkIndex { get; set; }
        public byte[] ChunkData { get; set; }
        public int ChunkSize { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
        public TimeSpan TransferTime { get; set; }
    }

    /// <summary>
    /// Transfer configuration
    /// </summary>
    public class TransferConfiguration
    {
        #region Constants
        /// <summary>
        /// Default chunk size in bytes (64KB).
        /// </summary>
        private const int DefaultChunkSizeBytes = 64 * 1024;

        /// <summary>
        /// Maximum number of parallel chunk transfers.
        /// </summary>
        private const int DefaultMaxParallelChunks = 4;

        /// <summary>
        /// Maximum number of retry attempts for failed chunks.
        /// </summary>
        private const int DefaultMaxRetryAttempts = 3;

        /// <summary>
        /// Default timeout for chunk operations in seconds.
        /// </summary>
        private const int DefaultChunkTimeoutSeconds = 30;
        #endregion

        public int DefaultChunkSize { get; set; } = DefaultChunkSizeBytes;
        public int MaxParallelChunks { get; set; } = DefaultMaxParallelChunks;
        public int MaxRetryAttempts { get; set; } = DefaultMaxRetryAttempts;
        public TimeSpan ChunkTimeout { get; set; } = TimeSpan.FromSeconds(DefaultChunkTimeoutSeconds);
        public bool EnableCompression { get; set; } = false;
        public bool EnableEncryption { get; set; } = true;
    }

    /// <summary>
    /// Chunk manager for handling chunk operations
    /// </summary>
    public class ChunkManager
    {
        /// <summary>
        /// Creates a chunk from source data
        /// </summary>
        /// <param name="data">Source data array</param>
        /// <param name="offset">Starting offset in the source data</param>
        /// <param name="length">Length of the chunk to create</param>
        /// <returns>A new byte array containing the chunk data</returns>
        /// <exception cref="ArgumentNullException">Thrown when data is null</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when offset or length are invalid</exception>
        public byte[] CreateChunk(byte[] data, int offset, int length)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "Source data cannot be null");

            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative");

            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than zero");

            if (offset + length > data.Length)
                throw new ArgumentOutOfRangeException(nameof(length),
                    $"Offset ({offset}) + Length ({length}) exceeds data length ({data.Length})");

            var chunk = new byte[length];
            Array.Copy(data, offset, chunk, 0, length);
            return chunk;
        }

        /// <summary>
        /// Calculates SHA256 checksum for chunk data
        /// </summary>
        /// <param name="chunkData">The chunk data to calculate checksum for</param>
        /// <returns>Base64 encoded SHA256 hash</returns>
        /// <exception cref="ArgumentNullException">Thrown when chunkData is null</exception>
        public string CalculateChunkChecksum(byte[] chunkData)
        {
            if (chunkData == null)
                throw new ArgumentNullException(nameof(chunkData), "Chunk data cannot be null");

            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha256.ComputeHash(chunkData);
                return Convert.ToBase64String(hash);
            }
        }

        /// <summary>
        /// Validates chunk data against expected checksum
        /// </summary>
        /// <param name="chunkData">The chunk data to validate</param>
        /// <param name="expectedChecksum">The expected checksum</param>
        /// <returns>True if checksums match, false otherwise</returns>
        /// <exception cref="ArgumentNullException">Thrown when chunkData or expectedChecksum is null</exception>
        public bool ValidateChunk(byte[] chunkData, string expectedChecksum)
        {
            if (chunkData == null)
                throw new ArgumentNullException(nameof(chunkData), "Chunk data cannot be null");

            if (string.IsNullOrWhiteSpace(expectedChecksum))
                throw new ArgumentNullException(nameof(expectedChecksum), "Expected checksum cannot be null or empty");

            try
            {
                var actualChecksum = CalculateChunkChecksum(chunkData);
                return actualChecksum.Equals(expectedChecksum, StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error validating chunk: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Transfer direction enumeration
    /// </summary>
    public enum TransferDirection
    {
        Upload,
        Download
    }

    /// <summary>
    /// Transfer status enumeration
    /// </summary>
    public enum TransferStatus
    {
        Initializing,
        Active,
        Paused,
        Completing,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Chunk state enumeration
    /// </summary>
    public enum ChunkState
    {
        Pending,
        InProgress,
        Completed,
        Failed,
        Retrying
    }

    /// <summary>
    /// Transfer progress event arguments
    /// </summary>
    public class TransferProgressEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public double Progress { get; set; }
        public int CompletedChunks { get; set; }
        public int TotalChunks { get; set; }
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public double TransferSpeed { get; set; }
        public double AverageSpeed { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public int FailedChunks { get; set; }
        public DateTime LastUpdateTime { get; set; }

        /// <summary>
        /// Gets the completion percentage as a formatted string
        /// </summary>
        public string ProgressText => $"{Progress:F1}%";

        /// <summary>
        /// Gets the transfer speed as a formatted string
        /// </summary>
        public string SpeedText => FormatSpeed(TransferSpeed);

        /// <summary>
        /// Gets the ETA as a formatted string
        /// </summary>
        public string EtaText => FormatTimeSpan(EstimatedTimeRemaining);

        /// <summary>
        /// Gets the elapsed time as a formatted string
        /// </summary>
        public string ElapsedText => FormatTimeSpan(ElapsedTime);

        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond < 1024)
                return $"{bytesPerSecond:F0} B/s";
            if (bytesPerSecond < 1024 * 1024)
                return $"{bytesPerSecond / 1024:F1} KB/s";
            if (bytesPerSecond < 1024 * 1024 * 1024)
                return $"{bytesPerSecond / (1024 * 1024):F1} MB/s";
            return $"{bytesPerSecond / (1024 * 1024 * 1024):F1} GB/s";
        }

        private static string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalSeconds < 60)
                return $"{timeSpan.TotalSeconds:F0}s";
            if (timeSpan.TotalMinutes < 60)
                return $"{timeSpan.TotalMinutes:F0}m {timeSpan.Seconds}s";
            return $"{timeSpan.TotalHours:F0}h {timeSpan.Minutes}m";
        }
    }

    /// <summary>
    /// Chunk transfer event arguments
    /// </summary>
    public class ChunkTransferEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public int ChunkIndex { get; set; }
        public bool Success { get; set; }
        public int ChunkSize { get; set; }
        public string ErrorMessage { get; set; }
        public TimeSpan TransferTime { get; set; }
    }

    /// <summary>
    /// Transfer completed event arguments
    /// </summary>
    public class TransferCompletedEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
        public long TotalBytes { get; set; }
        public double AverageSpeed { get; set; }
        public int TotalChunks { get; set; }
        public int FailedChunks { get; set; }
    }

    /// <summary>
    /// Transfer statistics for progress tracking and ETA calculation
    /// </summary>
    public class TransferStatistics
    {
        public DateTime StartTime { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public long TotalBytesTransferred { get; set; }
        public List<TimeSpan> ChunkTransferTimes { get; set; }
        public List<int> ChunkSizes { get; set; }
        public Queue<SpeedSample> SpeedSamples { get; set; }
    }

    /// <summary>
    /// Speed sample for calculating moving average
    /// </summary>
    public class SpeedSample
    {
        public double Speed { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
