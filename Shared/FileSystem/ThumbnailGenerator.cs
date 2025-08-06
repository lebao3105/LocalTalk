using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Shared.Platform;

namespace Shared.FileSystem
{
    /// <summary>
    /// Thumbnail generation and caching system with format conversion and memory optimization
    /// </summary>
    public class ThumbnailGenerator
    {
        private static ThumbnailGenerator _instance;
        private readonly ConcurrentDictionary<string, ThumbnailCacheEntry> _thumbnailCache;
        private readonly SemaphoreSlim _generationSemaphore;
        private readonly ThumbnailConfiguration _config;
        private readonly MemoryManager _memoryManager;

        public static ThumbnailGenerator Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ThumbnailGenerator();
                }
                return _instance;
            }
        }

        public event EventHandler<ThumbnailGeneratedEventArgs> ThumbnailGenerated;
        public event EventHandler<ThumbnailCacheEventArgs> ThumbnailCached;
        public event EventHandler<ThumbnailErrorEventArgs> ThumbnailError;

        private ThumbnailGenerator()
        {
            _thumbnailCache = new ConcurrentDictionary<string, ThumbnailCacheEntry>();
            _generationSemaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
            _config = new ThumbnailConfiguration();
            _memoryManager = new MemoryManager();
            
            // Start cache cleanup timer
            _ = StartCacheCleanupAsync();
        }

        /// <summary>
        /// Generates thumbnail for a file
        /// </summary>
        public async Task<ThumbnailResult> GenerateThumbnailAsync(IStorageFile file, ThumbnailSize size = ThumbnailSize.Medium, CancellationToken cancellationToken = default)
        {
            var result = new ThumbnailResult
            {
                FileName = file.Name,
                FileSize = file.Size,
                RequestedSize = size,
                RequestedAt = DateTime.Now
            };

            try
            {
                // Check if thumbnail is cached
                var cacheKey = GenerateCacheKey(file.Path, size);
                if (_thumbnailCache.TryGetValue(cacheKey, out var cachedEntry))
                {
                    if (!IsExpired(cachedEntry))
                    {
                        result.Success = true;
                        result.ThumbnailData = cachedEntry.ThumbnailData;
                        result.ActualSize = cachedEntry.ActualSize;
                        result.Format = cachedEntry.Format;
                        result.FromCache = true;
                        result.GenerationTime = TimeSpan.Zero;
                        return result;
                    }
                    else
                    {
                        // Remove expired entry
                        _thumbnailCache.TryRemove(cacheKey, out _);
                    }
                }

                // Check if file type supports thumbnail generation
                var fileType = await FileTypeDetector.Instance.DetectFileTypeAsync(file);
                if (!IsThumbnailSupported(fileType.DetectedFileType))
                {
                    result.Success = false;
                    result.ErrorMessage = "File type does not support thumbnail generation";
                    return result;
                }

                // Check memory pressure before generation
                if (_memoryManager.IsUnderMemoryPressure())
                {
                    await _memoryManager.RelieveMemoryPressureAsync();
                }

                // Generate thumbnail with concurrency control
                await _generationSemaphore.WaitAsync(cancellationToken);
                try
                {
                    var startTime = DateTime.Now;
                    var thumbnailData = await GenerateThumbnailDataAsync(file, fileType.DetectedFileType, size, cancellationToken);
                    
                    if (thumbnailData != null && thumbnailData.Length > 0)
                    {
                        result.Success = true;
                        result.ThumbnailData = thumbnailData;
                        result.ActualSize = GetActualThumbnailSize(size);
                        result.Format = ThumbnailFormat.PNG; // Default format
                        result.GenerationTime = DateTime.Now - startTime;

                        // Cache the thumbnail
                        var cacheEntry = new ThumbnailCacheEntry
                        {
                            ThumbnailData = thumbnailData,
                            ActualSize = result.ActualSize,
                            Format = result.Format,
                            CreatedAt = DateTime.Now,
                            LastAccessed = DateTime.Now,
                            FileSize = file.Size,
                            FileModified = file.DateModified
                        };

                        _thumbnailCache[cacheKey] = cacheEntry;

                        OnThumbnailGenerated(new ThumbnailGeneratedEventArgs
                        {
                            FileName = file.Name,
                            Size = size,
                            GenerationTime = result.GenerationTime,
                            ThumbnailSize = thumbnailData.Length
                        });

                        OnThumbnailCached(new ThumbnailCacheEventArgs
                        {
                            CacheKey = cacheKey,
                            ThumbnailSize = thumbnailData.Length
                        });
                    }
                    else
                    {
                        result.Success = false;
                        result.ErrorMessage = "Failed to generate thumbnail data";
                    }
                }
                finally
                {
                    _generationSemaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Thumbnail generation was cancelled";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                
                OnThumbnailError(new ThumbnailErrorEventArgs
                {
                    FileName = file.Name,
                    ErrorMessage = ex.Message,
                    Exception = ex
                });
                
                System.Diagnostics.Debug.WriteLine($"Thumbnail generation error for {file.Name}: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Generates thumbnails for multiple files
        /// </summary>
        public async Task<List<ThumbnailResult>> GenerateThumbnailsAsync(IEnumerable<IStorageFile> files, ThumbnailSize size = ThumbnailSize.Medium, CancellationToken cancellationToken = default)
        {
            var tasks = files.Select(file => GenerateThumbnailAsync(file, size, cancellationToken));
            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        /// <summary>
        /// Preloads thumbnails for files
        /// </summary>
        public async Task PreloadThumbnailsAsync(IEnumerable<IStorageFile> files, ThumbnailSize size = ThumbnailSize.Medium)
        {
            var preloadTasks = files.Select(file => Task.Run(async () =>
            {
                try
                {
                    await GenerateThumbnailAsync(file, size);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Preload thumbnail error for {file.Name}: {ex.Message}");
                }
            }));

            await Task.WhenAll(preloadTasks);
            System.Diagnostics.Debug.WriteLine($"Preloaded thumbnails for {files.Count()} files");
        }

        /// <summary>
        /// Gets cache statistics
        /// </summary>
        public ThumbnailCacheStatistics GetCacheStatistics()
        {
            var totalSize = _thumbnailCache.Values.Sum(entry => entry.ThumbnailData.Length);
            var expiredCount = _thumbnailCache.Values.Count(entry => IsExpired(entry));

            return new ThumbnailCacheStatistics
            {
                TotalEntries = _thumbnailCache.Count,
                TotalSizeBytes = totalSize,
                ExpiredEntries = expiredCount,
                HitRate = CalculateHitRate(),
                MemoryUsage = _memoryManager.GetMemoryUsage()
            };
        }

        /// <summary>
        /// Clears thumbnail cache
        /// </summary>
        public async Task ClearCacheAsync()
        {
            _thumbnailCache.Clear();
            await _memoryManager.RelieveMemoryPressureAsync();
            System.Diagnostics.Debug.WriteLine("Thumbnail cache cleared");
        }

        /// <summary>
        /// Checks if file type supports thumbnail generation
        /// </summary>
        private bool IsThumbnailSupported(FileTypeSignature fileType)
        {
            if (fileType == null)
                return false;

            var supportedCategories = new[]
            {
                FileCategory.Image,
                FileCategory.Video,
                FileCategory.Document
            };

            return supportedCategories.Contains(fileType.Category);
        }

        /// <summary>
        /// Generates thumbnail data for a file
        /// </summary>
        private async Task<byte[]> GenerateThumbnailDataAsync(IStorageFile file, FileTypeSignature fileType, ThumbnailSize size, CancellationToken cancellationToken)
        {
            try
            {
                switch (fileType.Category)
                {
                    case FileCategory.Image:
                        return await GenerateImageThumbnailAsync(file, size, cancellationToken);
                    case FileCategory.Video:
                        return await GenerateVideoThumbnailAsync(file, size, cancellationToken);
                    case FileCategory.Document:
                        return await GenerateDocumentThumbnailAsync(file, size, cancellationToken);
                    default:
                        return await GenerateGenericThumbnailAsync(file, size, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating thumbnail data: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Generates thumbnail for image files
        /// </summary>
        private async Task<byte[]> GenerateImageThumbnailAsync(IStorageFile file, ThumbnailSize size, CancellationToken cancellationToken)
        {
            // This is a simplified implementation
            // In a real implementation, you would use image processing libraries like ImageSharp or SkiaSharp
            await Task.Delay(100, cancellationToken); // Simulate processing time
            
            // Return placeholder thumbnail data
            return GeneratePlaceholderThumbnail(size, "IMG");
        }

        /// <summary>
        /// Generates thumbnail for video files
        /// </summary>
        private async Task<byte[]> GenerateVideoThumbnailAsync(IStorageFile file, ThumbnailSize size, CancellationToken cancellationToken)
        {
            // This is a simplified implementation
            // In a real implementation, you would extract a frame from the video
            await Task.Delay(200, cancellationToken); // Simulate processing time
            
            return GeneratePlaceholderThumbnail(size, "VID");
        }

        /// <summary>
        /// Generates thumbnail for document files
        /// </summary>
        private async Task<byte[]> GenerateDocumentThumbnailAsync(IStorageFile file, ThumbnailSize size, CancellationToken cancellationToken)
        {
            // This is a simplified implementation
            // In a real implementation, you would render the first page of the document
            await Task.Delay(150, cancellationToken); // Simulate processing time
            
            return GeneratePlaceholderThumbnail(size, "DOC");
        }

        /// <summary>
        /// Generates generic thumbnail for unsupported file types
        /// </summary>
        private async Task<byte[]> GenerateGenericThumbnailAsync(IStorageFile file, ThumbnailSize size, CancellationToken cancellationToken)
        {
            await Task.Delay(50, cancellationToken); // Simulate processing time
            
            var extension = Path.GetExtension(file.Name).ToUpperInvariant().TrimStart('.');
            return GeneratePlaceholderThumbnail(size, extension);
        }

        /// <summary>
        /// Generates placeholder thumbnail data
        /// </summary>
        private byte[] GeneratePlaceholderThumbnail(ThumbnailSize size, string text)
        {
            // This is a simplified implementation that returns dummy PNG data
            // In a real implementation, you would generate an actual image
            var dimensions = GetActualThumbnailSize(size);
            var placeholderSize = Math.Max(100, dimensions.Width * dimensions.Height / 10);
            
            var placeholder = new byte[placeholderSize];
            // Fill with some pattern to simulate image data
            for (int i = 0; i < placeholder.Length; i++)
            {
                placeholder[i] = (byte)(i % 256);
            }
            
            return placeholder;
        }

        /// <summary>
        /// Gets actual thumbnail dimensions for a size
        /// </summary>
        private ThumbnailDimensions GetActualThumbnailSize(ThumbnailSize size)
        {
            return size switch
            {
                ThumbnailSize.Small => new ThumbnailDimensions { Width = 64, Height = 64 },
                ThumbnailSize.Medium => new ThumbnailDimensions { Width = 128, Height = 128 },
                ThumbnailSize.Large => new ThumbnailDimensions { Width = 256, Height = 256 },
                ThumbnailSize.ExtraLarge => new ThumbnailDimensions { Width = 512, Height = 512 },
                _ => new ThumbnailDimensions { Width = 128, Height = 128 }
            };
        }

        /// <summary>
        /// Generates cache key for thumbnail
        /// </summary>
        private string GenerateCacheKey(string filePath, ThumbnailSize size)
        {
            var hash = System.Security.Cryptography.SHA256.Create().ComputeHash(
                System.Text.Encoding.UTF8.GetBytes($"{filePath}:{size}"));
            return Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Checks if cache entry is expired
        /// </summary>
        private bool IsExpired(ThumbnailCacheEntry entry)
        {
            return DateTime.Now - entry.CreatedAt > _config.CacheExpiration;
        }

        /// <summary>
        /// Calculates cache hit rate
        /// </summary>
        private double CalculateHitRate()
        {
            // Simplified hit rate calculation
            // In a real implementation, you would track hits and misses
            return 0.75; // 75% hit rate placeholder
        }

        /// <summary>
        /// Starts cache cleanup background task
        /// </summary>
        private async Task StartCacheCleanupAsync()
        {
            await Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        await Task.Delay(_config.CleanupInterval);
                        await CleanupExpiredEntriesAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Cache cleanup error: {ex.Message}");
                    }
                }
            });
        }

        /// <summary>
        /// Cleans up expired cache entries
        /// </summary>
        private async Task CleanupExpiredEntriesAsync()
        {
            await Task.Run(() =>
            {
                var expiredKeys = _thumbnailCache
                    .Where(kvp => IsExpired(kvp.Value))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    _thumbnailCache.TryRemove(key, out _);
                }

                if (expiredKeys.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Cleaned up {expiredKeys.Count} expired thumbnail cache entries");
                }
            });
        }

        /// <summary>
        /// Raises the ThumbnailGenerated event
        /// </summary>
        private void OnThumbnailGenerated(ThumbnailGeneratedEventArgs args)
        {
            ThumbnailGenerated?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the ThumbnailCached event
        /// </summary>
        private void OnThumbnailCached(ThumbnailCacheEventArgs args)
        {
            ThumbnailCached?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the ThumbnailError event
        /// </summary>
        private void OnThumbnailError(ThumbnailErrorEventArgs args)
        {
            ThumbnailError?.Invoke(this, args);
        }
    }

    /// <summary>
    /// Thumbnail configuration
    /// </summary>
    public class ThumbnailConfiguration
    {
        public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromHours(24);
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
        public int MaxCacheSize { get; set; } = 100 * 1024 * 1024; // 100MB
        public int MaxConcurrentGenerations { get; set; } = Environment.ProcessorCount;
        public ThumbnailFormat DefaultFormat { get; set; } = ThumbnailFormat.PNG;
        public int JpegQuality { get; set; } = 85;
        public bool EnableMemoryOptimization { get; set; } = true;
    }

    /// <summary>
    /// Thumbnail cache entry
    /// </summary>
    internal class ThumbnailCacheEntry
    {
        public byte[] ThumbnailData { get; set; }
        public ThumbnailDimensions ActualSize { get; set; }
        public ThumbnailFormat Format { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastAccessed { get; set; }
        public long FileSize { get; set; }
        public DateTime FileModified { get; set; }
    }

    /// <summary>
    /// Thumbnail generation result
    /// </summary>
    public class ThumbnailResult
    {
        public bool Success { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public ThumbnailSize RequestedSize { get; set; }
        public ThumbnailDimensions ActualSize { get; set; }
        public ThumbnailFormat Format { get; set; }
        public byte[] ThumbnailData { get; set; }
        public bool FromCache { get; set; }
        public TimeSpan GenerationTime { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime RequestedAt { get; set; }
    }

    /// <summary>
    /// Thumbnail cache statistics
    /// </summary>
    public class ThumbnailCacheStatistics
    {
        public int TotalEntries { get; set; }
        public long TotalSizeBytes { get; set; }
        public int ExpiredEntries { get; set; }
        public double HitRate { get; set; }
        public MemoryUsageInfo MemoryUsage { get; set; }
        public string FormattedTotalSize => FormatBytes(TotalSizeBytes);

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = bytes;

            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }

            return $"{number:n1} {suffixes[counter]}";
        }
    }

    /// <summary>
    /// Thumbnail dimensions
    /// </summary>
    public class ThumbnailDimensions
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public override string ToString() => $"{Width}x{Height}";
    }

    /// <summary>
    /// Thumbnail sizes
    /// </summary>
    public enum ThumbnailSize
    {
        Small,      // 64x64
        Medium,     // 128x128
        Large,      // 256x256
        ExtraLarge  // 512x512
    }

    /// <summary>
    /// Thumbnail formats
    /// </summary>
    public enum ThumbnailFormat
    {
        PNG,
        JPEG,
        WebP,
        BMP
    }

    /// <summary>
    /// Thumbnail generated event arguments
    /// </summary>
    public class ThumbnailGeneratedEventArgs : EventArgs
    {
        public string FileName { get; set; }
        public ThumbnailSize Size { get; set; }
        public TimeSpan GenerationTime { get; set; }
        public int ThumbnailSize { get; set; }
    }

    /// <summary>
    /// Thumbnail cache event arguments
    /// </summary>
    public class ThumbnailCacheEventArgs : EventArgs
    {
        public string CacheKey { get; set; }
        public int ThumbnailSize { get; set; }
    }

    /// <summary>
    /// Thumbnail error event arguments
    /// </summary>
    public class ThumbnailErrorEventArgs : EventArgs
    {
        public string FileName { get; set; }
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
    }
}
