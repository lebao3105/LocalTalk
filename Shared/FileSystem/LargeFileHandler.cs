using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using Shared.Platform;

namespace Shared.FileSystem
{
    /// <summary>
    /// Memory-efficient large file handling system with streaming and chunking
    /// </summary>
    public class LargeFileHandler
    {
        // File handling constants
        private const int DefaultChunkSize = 64 * 1024; // 64KB
        private const int MaxChunkSize = 1024 * 1024; // 1MB
        private const int DefaultBufferSize = 8192; // 8KB
        private const long LargeFileThreshold = 100 * 1024 * 1024; // 100MB
        private const int MaxConcurrentStreams = 10;
        private const int SmallFileMaxChunkSize = 32 * 1024; // 32KB for small files
        private const int MinChunkSize = 8 * 1024; // 8KB minimum
        private const double MaxMemoryUsageRatio = 0.1; // Use max 10% of available memory
        private const int ChunkSizeMultiplier = 4; // Max 4x base chunk size
        private const int CleanupIntervalMinutes = 1;

        private static LargeFileHandler _instance;
        private readonly MemoryManager _memoryManager;
        private readonly Dictionary<string, FileStreamContext> _activeStreams;
        private readonly object _lock = new object();

        // Memory pool for buffer reuse to reduce GC pressure
        internal readonly System.Buffers.ArrayPool<byte> _bufferPool;
        internal readonly ConcurrentDictionary<int, ConcurrentQueue<byte[]>> _sizedBufferPools;

        /// <summary>
        /// Gets the singleton instance of the LargeFileHandler
        /// </summary>
        public static LargeFileHandler Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LargeFileHandler();
                }
                return _instance;
            }
        }

        /// <summary>
        /// Event raised to report file processing progress
        /// </summary>
        public event EventHandler<FileProcessingEventArgs> FileProcessingProgress;

        /// <summary>
        /// Event raised when memory pressure is detected during file operations
        /// </summary>
        public event EventHandler<MemoryPressureEventArgs> MemoryPressureDetected;

        private LargeFileHandler()
        {
            _memoryManager = new MemoryManager();
            _activeStreams = new Dictionary<string, FileStreamContext>();

            // Initialize buffer pools for performance optimization
            _bufferPool = System.Buffers.ArrayPool<byte>.Shared;
            _sizedBufferPools = new ConcurrentDictionary<int, ConcurrentQueue<byte[]>>();

            // Pre-populate common buffer sizes
            var commonSizes = new[] { 4096, 8192, 16384, 32768, 65536 };
            foreach (var size in commonSizes)
            {
                _sizedBufferPools[size] = new ConcurrentQueue<byte[]>();
            }

            // Monitor memory pressure
            _memoryManager.MemoryPressureDetected += OnMemoryPressureDetected;
        }

        /// <summary>
        /// Creates a streaming reader for large files
        /// </summary>
        /// <param name="file">Storage file to read from</param>
        /// <param name="options">Optional read configuration options</param>
        /// <returns>Large file reader instance for streaming operations</returns>
        public async Task<LargeFileReader> CreateReaderAsync(IStorageFile file, FileReadOptions options = null)
        {
            options = options ?? new FileReadOptions();

            // Check if file qualifies as "large"
            var isLargeFile = file.Size > options.LargeFileThreshold;

            // Determine optimal chunk size based on file size and available memory
            var chunkSize = await DetermineOptimalChunkSizeAsync(file.Size, isLargeFile);

            var reader = new LargeFileReader(file, chunkSize, options);

            // Register the stream for memory management
            var streamId = Guid.NewGuid().ToString();
            lock (_lock)
            {
                _activeStreams[streamId] = new FileStreamContext
                {
                    StreamId = streamId,
                    File = file,
                    ChunkSize = chunkSize,
                    CreatedAt = DateTime.Now,
                    LastAccessedAt = DateTime.Now
                };
            }

            reader.StreamClosed += (s, e) => UnregisterStream(streamId);

            return reader;
        }

        /// <summary>
        /// Creates a streaming writer for large files
        /// </summary>
        public async Task<LargeFileWriter> CreateWriterAsync(string filePath, FileWriteOptions options = null)
        {
            options = options ?? new FileWriteOptions();

            // Determine optimal chunk size for writing
            var chunkSize = await DetermineOptimalChunkSizeAsync(options.EstimatedFileSize,
                options.EstimatedFileSize > options.LargeFileThreshold);

            var writer = new LargeFileWriter(filePath, chunkSize, options);

            // Register the stream for memory management
            var streamId = Guid.NewGuid().ToString();
            lock (_lock)
            {
                _activeStreams[streamId] = new FileStreamContext
                {
                    StreamId = streamId,
                    FilePath = filePath,
                    ChunkSize = chunkSize,
                    CreatedAt = DateTime.Now,
                    LastAccessedAt = DateTime.Now,
                    IsWriter = true
                };
            }

            writer.StreamClosed += (s, e) => UnregisterStream(streamId);

            return writer;
        }

        /// <summary>
        /// Processes a large file in chunks with progress reporting
        /// </summary>
        public async Task<FileProcessingResult> ProcessFileAsync<T>(
            IStorageFile file,
            Func<byte[], int, Task<T>> chunkProcessor,
            FileProcessingOptions options = null,
            CancellationToken cancellationToken = default)
        {
            var result = new FileProcessingResult
            {
                FileName = file.Name,
                FileSize = file.Size,
                StartTime = DateTime.Now
            };

            try
            {
                options = options ?? new FileProcessingOptions();

                using (var reader = await CreateReaderAsync(file, new FileReadOptions
                {
                    BufferSize = options.ChunkSize,
                    LargeFileThreshold = options.LargeFileThreshold
                }))
                {
                    var totalBytesProcessed = 0L;
                    var chunkIndex = 0;

                    while (!reader.EndOfFile && !cancellationToken.IsCancellationRequested)
                    {
                        // Check memory pressure before processing next chunk
                        if (_memoryManager.IsUnderMemoryPressure())
                        {
                            await _memoryManager.RelieveMemoryPressureAsync();
                        }

                        var chunk = await reader.ReadChunkAsync();
                        if (chunk.Length == 0)
                            break;

                        // Process the chunk
                        var chunkResult = await chunkProcessor(chunk, chunkIndex);
                        result.ChunkResults.Add(chunkResult);

                        totalBytesProcessed += chunk.Length;
                        chunkIndex++;

                        // Report progress
                        var progress = (double)totalBytesProcessed / file.Size * 100;
                        OnFileProcessingProgress(new FileProcessingEventArgs
                        {
                            FileName = file.Name,
                            Progress = progress,
                            BytesProcessed = totalBytesProcessed,
                            TotalBytes = file.Size,
                            ChunkIndex = chunkIndex
                        });

                        // Yield control to prevent UI blocking
                        if (chunkIndex % MaxConcurrentStreams == 0)
                        {
                            await Task.Yield();
                        }
                    }

                    result.Success = !cancellationToken.IsCancellationRequested;
                    result.BytesProcessed = totalBytesProcessed;
                    result.ChunksProcessed = chunkIndex;
                    result.EndTime = DateTime.Now;
                    result.ProcessingDuration = result.EndTime - result.StartTime;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Large file processing error: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Gets memory usage statistics
        /// </summary>
        public MemoryUsageInfo GetMemoryUsage()
        {
            return _memoryManager.GetMemoryUsage();
        }

        /// <summary>
        /// Gets information about active file streams
        /// </summary>
        public List<FileStreamInfo> GetActiveStreams()
        {
            lock (_lock)
            {
                var streams = new List<FileStreamInfo>();
                foreach (var context in _activeStreams.Values)
                {
                    streams.Add(new FileStreamInfo
                    {
                        StreamId = context.StreamId,
                        FileName = context.File?.Name ?? Path.GetFileName(context.FilePath),
                        FilePath = context.FilePath ?? context.File?.Path,
                        ChunkSize = context.ChunkSize,
                        CreatedAt = context.CreatedAt,
                        LastAccessedAt = context.LastAccessedAt,
                        IsWriter = context.IsWriter
                    });
                }
                return streams;
            }
        }

        /// <summary>
        /// Forces cleanup of inactive streams
        /// </summary>
        public async Task CleanupInactiveStreamsAsync(TimeSpan inactivityThreshold = default)
        {
            if (inactivityThreshold == default)
                inactivityThreshold = TimeSpan.FromMinutes(5);

            var now = DateTime.Now;
            var streamsToRemove = new List<string>();

            lock (_lock)
            {
                foreach (var kvp in _activeStreams)
                {
                    if (now - kvp.Value.LastAccessedAt > inactivityThreshold)
                    {
                        streamsToRemove.Add(kvp.Key);
                    }
                }

                foreach (var streamId in streamsToRemove)
                {
                    _activeStreams.Remove(streamId);
                }
            }

            if (streamsToRemove.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Cleaned up {streamsToRemove.Count} inactive file streams");

                // Force garbage collection after cleanup
                await Task.Run(() =>
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                });
            }
        }

        /// <summary>
        /// Determines optimal chunk size based on file size and system resources
        /// </summary>
        private async Task<int> DetermineOptimalChunkSizeAsync(long fileSize, bool isLargeFile)
        {
            return await Task.Run(() =>
            {
                var memoryInfo = _memoryManager.GetMemoryUsage();
                var availableMemory = memoryInfo.AvailableMemory;

                // Base chunk sizes from platform features
                var baseChunkSize = PlatformFactory.Features.DefaultChunkSize;

                if (!isLargeFile)
                {
                    // For smaller files, use a smaller chunk size
                    return Math.Min(baseChunkSize, (int)Math.Min(fileSize, SmallFileMaxChunkSize));
                }

                // For large files, calculate based on available memory
                var targetMemoryUsage = availableMemory * MaxMemoryUsageRatio;
                var optimalChunkSize = (int)Math.Min(targetMemoryUsage, baseChunkSize * ChunkSizeMultiplier);

                // Ensure chunk size is within reasonable bounds
                optimalChunkSize = Math.Max(optimalChunkSize, MinChunkSize);
                optimalChunkSize = Math.Min(optimalChunkSize, MaxChunkSize);

                System.Diagnostics.Debug.WriteLine(
                    $"Determined optimal chunk size: {optimalChunkSize} bytes for file size: {fileSize} bytes");

                return optimalChunkSize;
            });
        }

        /// <summary>
        /// Unregisters a file stream from memory management
        /// </summary>
        private void UnregisterStream(string streamId)
        {
            lock (_lock)
            {
                if (_activeStreams.ContainsKey(streamId))
                {
                    _activeStreams.Remove(streamId);
                    System.Diagnostics.Debug.WriteLine($"Unregistered file stream: {streamId}");
                }
            }
        }

        /// <summary>
        /// Handles memory pressure events
        /// </summary>
        private async void OnMemoryPressureDetected(object sender, MemoryPressureEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Memory pressure detected: {e.PressureLevel}");

            // Cleanup inactive streams
            await CleanupInactiveStreamsAsync(TimeSpan.FromMinutes(CleanupIntervalMinutes));

            // Notify listeners
            MemoryPressureDetected?.Invoke(this, e);
        }

        /// <summary>
        /// Raises the FileProcessingProgress event
        /// </summary>
        private void OnFileProcessingProgress(FileProcessingEventArgs args)
        {
            FileProcessingProgress?.Invoke(this, args);
        }
    }

    /// <summary>
    /// File read options
    /// </summary>
    public class FileReadOptions
    {
        /// <summary>
        /// Buffer size for read operations
        /// </summary>
        public int BufferSize { get; set; } = 64 * 1024; // DefaultChunkSize

        /// <summary>
        /// Threshold for considering a file as large
        /// </summary>
        public long LargeFileThreshold { get; set; } = 100 * 1024 * 1024; // LargeFileThreshold

        /// <summary>
        /// Whether to use memory mapping for file access
        /// </summary>
        public bool UseMemoryMapping { get; set; } = false;

        /// <summary>
        /// Whether to enable caching for read operations
        /// </summary>
        public bool EnableCaching { get; set; } = true;
    }

    /// <summary>
    /// File write options
    /// </summary>
    public class FileWriteOptions
    {
        /// <summary>
        /// Buffer size for write operations
        /// </summary>
        public int BufferSize { get; set; } = 64 * 1024; // DefaultChunkSize

        /// <summary>
        /// Threshold for considering a file as large
        /// </summary>
        public long LargeFileThreshold { get; set; } = 100 * 1024 * 1024; // LargeFileThreshold

        /// <summary>
        /// Estimated final size of the file being written
        /// </summary>
        public long EstimatedFileSize { get; set; } = 0;

        /// <summary>
        /// Whether to enable compression during write operations
        /// </summary>
        public bool EnableCompression { get; set; } = false;

        /// <summary>
        /// Whether to sync data to disk after each write
        /// </summary>
        public bool SyncToDisk { get; set; } = false;
    }

    /// <summary>
    /// File processing options
    /// </summary>
    public class FileProcessingOptions
    {
        /// <summary>
        /// Chunk size for processing operations
        /// </summary>
        public int ChunkSize { get; set; } = 64 * 1024; // DefaultChunkSize

        /// <summary>
        /// Threshold for considering a file as large
        /// </summary>
        public long LargeFileThreshold { get; set; } = 100 * 1024 * 1024; // LargeFileThreshold

        /// <summary>
        /// Whether to report progress during processing
        /// </summary>
        public bool ReportProgress { get; set; } = true;

        /// <summary>
        /// Interval between progress reports
        /// </summary>
        public TimeSpan ProgressReportInterval { get; set; } = TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// File processing result
    /// </summary>
    public class FileProcessingResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public long BytesProcessed { get; set; }
        public int ChunksProcessed { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan ProcessingDuration { get; set; }
        public List<object> ChunkResults { get; set; } = new List<object>();
    }

    /// <summary>
    /// File processing event arguments
    /// </summary>
    public class FileProcessingEventArgs : EventArgs
    {
        public string FileName { get; set; }
        public double Progress { get; set; }
        public long BytesProcessed { get; set; }
        public long TotalBytes { get; set; }
        public int ChunkIndex { get; set; }
    }

    /// <summary>
    /// File stream context for memory management
    /// </summary>
    internal class FileStreamContext
    {
        public string StreamId { get; set; }
        public IStorageFile File { get; set; }
        public string FilePath { get; set; }
        public int ChunkSize { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastAccessedAt { get; set; }
        public bool IsWriter { get; set; }
    }

    /// <summary>
    /// File stream information
    /// </summary>
    public class FileStreamInfo
    {
        public string StreamId { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public int ChunkSize { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastAccessedAt { get; set; }
        public bool IsWriter { get; set; }
    }

    /// <summary>
    /// Large file reader with streaming capabilities
    /// </summary>
    public class LargeFileReader : IDisposable
    {
        private readonly IStorageFile _file;
        private readonly int _chunkSize;
        private readonly FileReadOptions _options;
        private Stream _stream;
        private long _position;
        private bool _disposed;

        public event EventHandler StreamClosed;

        public bool EndOfFile => _position >= _file.Size;
        public long Position => _position;
        public long FileSize => _file.Size;

        internal LargeFileReader(IStorageFile file, int chunkSize, FileReadOptions options)
        {
            _file = file;
            _chunkSize = chunkSize;
            _options = options;
        }

        /// <summary>
        /// Reads the next chunk of data
        /// </summary>
        public async Task<byte[]> ReadChunkAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LargeFileReader));

            if (_stream == null)
            {
                _stream = await _file.OpenReadAsync();
            }

            var buffer = new byte[_chunkSize];
            var bytesRead = await _stream.ReadAsync(buffer, 0, _chunkSize);

            if (bytesRead < _chunkSize)
            {
                // Resize buffer to actual bytes read
                var actualBuffer = new byte[bytesRead];
                Array.Copy(buffer, actualBuffer, bytesRead);
                buffer = actualBuffer;
            }

            _position += bytesRead;
            return buffer;
        }

        /// <summary>
        /// Seeks to a specific position in the file
        /// </summary>
        public async Task SeekAsync(long position)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LargeFileReader));

            if (_stream == null)
            {
                _stream = await _file.OpenReadAsync();
            }

            _stream.Seek(position, SeekOrigin.Begin);
            _position = position;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _stream?.Dispose();
                StreamClosed?.Invoke(this, EventArgs.Empty);
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Large file writer with streaming capabilities
    /// </summary>
    public class LargeFileWriter : IDisposable
    {
        private readonly string _filePath;
        private readonly int _chunkSize;
        private readonly FileWriteOptions _options;
        private Stream _stream;
        private long _position;
        private bool _disposed;

        public event EventHandler StreamClosed;

        public long Position => _position;

        internal LargeFileWriter(string filePath, int chunkSize, FileWriteOptions options)
        {
            _filePath = filePath;
            _chunkSize = chunkSize;
            _options = options;
        }

        /// <summary>
        /// Writes a chunk of data
        /// </summary>
        public async Task WriteChunkAsync(byte[] data)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LargeFileWriter));

            if (_stream == null)
            {
                _stream = File.Create(_filePath);
            }

            await _stream.WriteAsync(data, 0, data.Length);
            _position += data.Length;

            if (_options.SyncToDisk)
            {
                await _stream.FlushAsync();
            }
        }

        /// <summary>
        /// Flushes any pending writes
        /// </summary>
        public async Task FlushAsync()
        {
            if (_stream != null)
            {
                await _stream.FlushAsync();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _stream?.Dispose();
                StreamClosed?.Invoke(this, EventArgs.Empty);
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Buffer management extensions for LargeFileHandler
    /// </summary>
    public static class BufferManagerExtensions
    {
        /// <summary>
        /// Gets a buffer from the pool with the specified size
        /// </summary>
        public static byte[] GetBuffer(this LargeFileHandler handler, int size)
        {
            // Try to get from sized pools first for exact matches
            if (handler._sizedBufferPools.TryGetValue(size, out var pool) && pool.TryDequeue(out var buffer))
            {
                return buffer;
            }

            // Fall back to shared array pool
            return handler._bufferPool.Rent(size);
        }

        /// <summary>
        /// Returns a buffer to the pool
        /// </summary>
        public static void ReturnBuffer(this LargeFileHandler handler, byte[] buffer, int originalSize)
        {
            if (buffer == null) return;

            // Return to sized pool if it matches exactly and pool isn't too full
            if (buffer.Length == originalSize &&
                handler._sizedBufferPools.TryGetValue(originalSize, out var pool) &&
                pool.Count < 10) // Limit pool size to prevent memory bloat
            {
                // Clear the buffer for security
                Array.Clear(buffer, 0, buffer.Length);
                pool.Enqueue(buffer);
            }
            else
            {
                // Return to shared pool
                handler._bufferPool.Return(buffer, clearArray: true);
            }
        }
    }
}
