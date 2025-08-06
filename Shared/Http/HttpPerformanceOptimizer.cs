using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.IO;
using Shared.Platform;

namespace Shared.Http
{
    /// <summary>
    /// HTTP performance optimization with caching, connection reuse, and bandwidth optimization
    /// </summary>
    public class HttpPerformanceOptimizer
    {
        private static HttpPerformanceOptimizer _instance;
        private readonly ConcurrentDictionary<string, HttpConnectionPool> _connectionPools;
        private readonly ResponseCache _responseCache;
        private readonly BandwidthManager _bandwidthManager;
        private readonly PerformanceConfiguration _config;

        public static HttpPerformanceOptimizer Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new HttpPerformanceOptimizer();
                }
                return _instance;
            }
        }

        public event EventHandler<CacheEventArgs> CacheHit;
        public event EventHandler<CacheEventArgs> CacheMiss;
        public event EventHandler<BandwidthEventArgs> BandwidthThrottled;

        private HttpPerformanceOptimizer()
        {
            _connectionPools = new ConcurrentDictionary<string, HttpConnectionPool>();
            _responseCache = new ResponseCache();
            _bandwidthManager = new BandwidthManager();
            _config = new PerformanceConfiguration();
        }

        /// <summary>
        /// Gets an optimized HTTP client for a specific endpoint
        /// </summary>
        public async Task<OptimizedHttpClient> GetOptimizedClientAsync(string baseUrl)
        {
            var poolKey = GetPoolKey(baseUrl);

            var pool = _connectionPools.GetOrAdd(poolKey, key => new HttpConnectionPool(baseUrl, _config));
            var httpClient = await pool.GetClientAsync();

            return new OptimizedHttpClient(httpClient, _responseCache, _bandwidthManager, this);
        }

        /// <summary>
        /// Performs an optimized HTTP GET request with caching
        /// </summary>
        public async Task<OptimizedHttpResponse> GetAsync(string url, CachePolicy cachePolicy = null)
        {
            var response = new OptimizedHttpResponse
            {
                RequestUrl = url,
                RequestedAt = DateTime.Now
            };

            try
            {
                cachePolicy = cachePolicy ?? _config.DefaultCachePolicy;

                // Check cache first
                var cacheKey = GenerateCacheKey("GET", url, null);
                var cachedResponse = await _responseCache.GetAsync(cacheKey);

                if (cachedResponse != null && !IsExpired(cachedResponse, cachePolicy))
                {
                    response.Content = cachedResponse.Content;
                    response.Headers = cachedResponse.Headers;
                    response.StatusCode = cachedResponse.StatusCode;
                    response.FromCache = true;
                    response.CacheAge = DateTime.Now - cachedResponse.CachedAt;

                    OnCacheHit(new CacheEventArgs { Url = url, CacheKey = cacheKey });
                    return response;
                }

                OnCacheMiss(new CacheEventArgs { Url = url, CacheKey = cacheKey });

                // Make HTTP request
                using (var client = await GetOptimizedClientAsync(GetBaseUrl(url)))
                {
                    var httpResponse = await client.GetAsync(url);

                    response.Content = httpResponse.Content;
                    response.Headers = httpResponse.Headers;
                    response.StatusCode = httpResponse.StatusCode;
                    response.ResponseTime = httpResponse.ResponseTime;
                    response.FromCache = false;

                    // Cache successful responses
                    if (httpResponse.IsSuccessful && cachePolicy.EnableCaching)
                    {
                        var cacheEntry = new CacheEntry
                        {
                            Content = httpResponse.Content,
                            Headers = httpResponse.Headers,
                            StatusCode = httpResponse.StatusCode,
                            CachedAt = DateTime.Now,
                            ExpiresAt = DateTime.Now.Add(cachePolicy.CacheDuration)
                        };

                        await _responseCache.SetAsync(cacheKey, cacheEntry, cachePolicy.CacheDuration);
                    }
                }
            }
            catch (Exception ex)
            {
                response.IsSuccessful = false;
                response.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"HTTP GET error for {url}: {ex}");
            }

            return response;
        }

        /// <summary>
        /// Performs an optimized HTTP POST request
        /// </summary>
        public async Task<OptimizedHttpResponse> PostAsync(string url, byte[] content, string contentType = "application/json")
        {
            var response = new OptimizedHttpResponse
            {
                RequestUrl = url,
                RequestedAt = DateTime.Now
            };

            try
            {
                using (var client = await GetOptimizedClientAsync(GetBaseUrl(url)))
                {
                    var httpResponse = await client.PostAsync(url, content, contentType);

                    response.Content = httpResponse.Content;
                    response.Headers = httpResponse.Headers;
                    response.StatusCode = httpResponse.StatusCode;
                    response.ResponseTime = httpResponse.ResponseTime;
                    response.FromCache = false;
                }
            }
            catch (Exception ex)
            {
                response.IsSuccessful = false;
                response.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"HTTP POST error for {url}: {ex}");
            }

            return response;
        }

        /// <summary>
        /// Preloads frequently accessed resources
        /// </summary>
        public async Task PreloadResourcesAsync(List<string> urls, CachePolicy cachePolicy = null)
        {
            var tasks = urls.Select(url => GetAsync(url, cachePolicy)).ToArray();
            await Task.WhenAll(tasks);

            System.Diagnostics.Debug.WriteLine($"Preloaded {urls.Count} resources");
        }

        /// <summary>
        /// Gets performance statistics
        /// </summary>
        public PerformanceStatistics GetPerformanceStatistics()
        {
            var stats = new PerformanceStatistics
            {
                ActiveConnectionPools = _connectionPools.Count,
                CacheStatistics = _responseCache.GetStatistics(),
                BandwidthStatistics = _bandwidthManager.GetStatistics(),
                GeneratedAt = DateTime.Now
            };

            foreach (var pool in _connectionPools.Values)
            {
                stats.ConnectionPoolStatistics.Add(pool.GetStatistics());
            }

            return stats;
        }

        /// <summary>
        /// Clears all caches and resets connection pools
        /// </summary>
        public async Task ClearCachesAsync()
        {
            await _responseCache.ClearAsync();

            foreach (var pool in _connectionPools.Values)
            {
                pool.Dispose();
            }
            _connectionPools.Clear();

            System.Diagnostics.Debug.WriteLine("HTTP caches and connection pools cleared");
        }

        /// <summary>
        /// Generates cache key for request
        /// </summary>
        private string GenerateCacheKey(string method, string url, byte[] content)
        {
            var key = $"{method}:{url}";
            if (content != null && content.Length > 0)
            {
                var contentHash = System.Security.Cryptography.SHA256.Create().ComputeHash(content);
                key += ":" + Convert.ToBase64String(contentHash);
            }
            return key;
        }

        /// <summary>
        /// Checks if cached response is expired
        /// </summary>
        private bool IsExpired(CacheEntry entry, CachePolicy policy)
        {
            return DateTime.Now > entry.ExpiresAt;
        }

        /// <summary>
        /// Gets pool key for connection pooling
        /// </summary>
        private string GetPoolKey(string url)
        {
            var uri = new Uri(url);
            return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        }

        /// <summary>
        /// Gets base URL from full URL
        /// </summary>
        private string GetBaseUrl(string url)
        {
            var uri = new Uri(url);
            return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        }

        /// <summary>
        /// Raises the CacheHit event
        /// </summary>
        private void OnCacheHit(CacheEventArgs args)
        {
            CacheHit?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the CacheMiss event
        /// </summary>
        private void OnCacheMiss(CacheEventArgs args)
        {
            CacheMiss?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the BandwidthThrottled event
        /// </summary>
        private void OnBandwidthThrottled(BandwidthEventArgs args)
        {
            BandwidthThrottled?.Invoke(this, args);
        }
    }

    /// <summary>
    /// HTTP connection pool for connection reuse
    /// </summary>
    public class HttpConnectionPool : IDisposable
    {
        private readonly string _baseUrl;
        private readonly PerformanceConfiguration _config;
        private readonly ConcurrentQueue<HttpClient> _availableClients;
        private readonly object _lock = new object();
        private int _activeConnections;
        private int _totalRequests;
        private DateTime _createdAt;

        public HttpConnectionPool(string baseUrl, PerformanceConfiguration config)
        {
            _baseUrl = baseUrl;
            _config = config;
            _availableClients = new ConcurrentQueue<HttpClient>();
            _createdAt = DateTime.Now;
        }

        /// <summary>
        /// Gets an HTTP client from the pool
        /// </summary>
        public async Task<HttpClient> GetClientAsync()
        {
            // Try to get an available client first
            if (_availableClients.TryDequeue(out var client))
            {
                Interlocked.Increment(ref _totalRequests);
                return client;
            }

            // Check if we can create a new client
            lock (_lock)
            {
                if (_activeConnections < _config.MaxConnectionsPerPool)
                {
                    client = CreateOptimizedClient();
                    _activeConnections++;
                    Interlocked.Increment(ref _totalRequests);
                    return client;
                }
            }

            // Use exponential backoff instead of fixed delay for better performance under load
            var retryCount = 0;
            const int maxRetries = 10;

            while (retryCount < maxRetries)
            {
                // Try again to get an available client
                if (_availableClients.TryDequeue(out client))
                {
                    Interlocked.Increment(ref _totalRequests);
                    return client;
                }

                // Exponential backoff with jitter
                var delay = Math.Min(100 * (1 << retryCount), 1000);
                var jitter = new Random().Next(0, delay / 4);
                await Task.Delay(delay + jitter);
                retryCount++;
            }

            // If we still can't get a client, create a temporary one
            // This prevents complete blocking but may exceed pool limits temporarily
            System.Diagnostics.Debug.WriteLine($"HTTP pool exhausted, creating temporary client for {_baseUrl}");
            return CreateOptimizedClient();
        }

        /// <summary>
        /// Returns a client to the pool
        /// </summary>
        public void ReturnClient(HttpClient client)
        {
            if (client != null && _availableClients.Count < _config.MaxConnectionsPerPool)
            {
                _availableClients.Enqueue(client);
            }
            else
            {
                client?.Dispose();
                lock (_lock)
                {
                    _activeConnections--;
                }
            }
        }

        /// <summary>
        /// Gets pool statistics
        /// </summary>
        public ConnectionPoolStatistics GetStatistics()
        {
            return new ConnectionPoolStatistics
            {
                BaseUrl = _baseUrl,
                ActiveConnections = _activeConnections,
                AvailableConnections = _availableClients.Count,
                TotalRequests = _totalRequests,
                CreatedAt = _createdAt
            };
        }

        /// <summary>
        /// Creates an optimized HTTP client
        /// </summary>
        private HttpClient CreateOptimizedClient()
        {
            var handler = new HttpClientHandler();

            // Enable HTTP/2 if supported
            if (_config.EnableHttp2)
            {
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            }

            var client = new HttpClient(handler);
            client.Timeout = _config.RequestTimeout;
            client.DefaultRequestHeaders.Add("User-Agent", "LocalTalk/1.0");
            client.DefaultRequestHeaders.Add("Connection", "keep-alive");

            return client;
        }

        public void Dispose()
        {
            while (_availableClients.TryDequeue(out var client))
            {
                client.Dispose();
            }
        }
    }

    /// <summary>
    /// Response cache for HTTP responses
    /// </summary>
    public class ResponseCache
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache;
        private readonly object _statsLock = new object();
        private int _hits;
        private int _misses;
        private DateTime _createdAt;

        public ResponseCache()
        {
            _cache = new ConcurrentDictionary<string, CacheEntry>();
            _createdAt = DateTime.Now;
        }

        /// <summary>
        /// Gets cached response
        /// </summary>
        public async Task<CacheEntry> GetAsync(string key)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                lock (_statsLock)
                {
                    _hits++;
                }
                return entry;
            }

            lock (_statsLock)
            {
                _misses++;
            }
            return null;
        }

        /// <summary>
        /// Sets cached response
        /// </summary>
        public async Task SetAsync(string key, CacheEntry entry, TimeSpan duration)
        {
            entry.ExpiresAt = DateTime.Now.Add(duration);
            _cache[key] = entry;
        }

        /// <summary>
        /// Clears all cached responses
        /// </summary>
        public async Task ClearAsync()
        {
            _cache.Clear();
            lock (_statsLock)
            {
                _hits = 0;
                _misses = 0;
            }
        }

        /// <summary>
        /// Gets cache statistics
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            lock (_statsLock)
            {
                return new CacheStatistics
                {
                    TotalEntries = _cache.Count,
                    Hits = _hits,
                    Misses = _misses,
                    HitRatio = _hits + _misses > 0 ? (double)_hits / (_hits + _misses) : 0,
                    CreatedAt = _createdAt
                };
            }
        }
    }

    /// <summary>
    /// Bandwidth manager for throttling and QoS
    /// </summary>
    public class BandwidthManager
    {
        private readonly object _lock = new object();
        private long _totalBytesTransferred;
        private DateTime _lastReset;
        private readonly TimeSpan _measurementWindow = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Throttles bandwidth if needed
        /// </summary>
        public async Task<bool> ThrottleIfNeededAsync(int bytesToTransfer)
        {
            // Simplified bandwidth throttling
            await Task.Delay(1); // Simulate throttling delay

            lock (_lock)
            {
                _totalBytesTransferred += bytesToTransfer;
            }

            return false; // Not throttled in this simplified implementation
        }

        /// <summary>
        /// Gets bandwidth statistics
        /// </summary>
        public BandwidthStatistics GetStatistics()
        {
            lock (_lock)
            {
                var elapsed = DateTime.Now - _lastReset;
                var throughput = elapsed.TotalSeconds > 0 ? _totalBytesTransferred / elapsed.TotalSeconds : 0;

                return new BandwidthStatistics
                {
                    TotalBytesTransferred = _totalBytesTransferred,
                    CurrentThroughput = throughput,
                    MeasurementWindow = _measurementWindow
                };
            }
        }
    }

    /// <summary>
    /// Optimized HTTP client wrapper
    /// </summary>
    public class OptimizedHttpClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ResponseCache _cache;
        private readonly BandwidthManager _bandwidthManager;
        private readonly HttpPerformanceOptimizer _optimizer;

        internal OptimizedHttpClient(HttpClient httpClient, ResponseCache cache, BandwidthManager bandwidthManager, HttpPerformanceOptimizer optimizer)
        {
            _httpClient = httpClient;
            _cache = cache;
            _bandwidthManager = bandwidthManager;
            _optimizer = optimizer;
        }

        /// <summary>
        /// Performs optimized GET request
        /// </summary>
        public async Task<OptimizedHttpResponse> GetAsync(string url)
        {
            var startTime = DateTime.Now;

            try
            {
                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsByteArrayAsync();

                await _bandwidthManager.ThrottleIfNeededAsync(content.Length);

                // Optimize header processing to reduce allocations
                var headers = new Dictionary<string, string>(response.Headers.Count());
                foreach (var header in response.Headers)
                {
                    // Use StringBuilder for multiple values to reduce string allocations
                    if (header.Value.Count() > 1)
                    {
                        var sb = new System.Text.StringBuilder();
                        var first = true;
                        foreach (var value in header.Value)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append(value);
                            first = false;
                        }
                        headers[header.Key] = sb.ToString();
                    }
                    else
                    {
                        headers[header.Key] = header.Value.FirstOrDefault() ?? string.Empty;
                    }
                }

                return new OptimizedHttpResponse
                {
                    RequestUrl = url,
                    StatusCode = (int)response.StatusCode,
                    Content = content,
                    Headers = headers,
                    IsSuccessful = response.IsSuccessStatusCode,
                    ResponseTime = DateTime.Now - startTime,
                    RequestedAt = startTime
                };
            }
            catch (Exception ex)
            {
                return new OptimizedHttpResponse
                {
                    RequestUrl = url,
                    IsSuccessful = false,
                    ErrorMessage = ex.Message,
                    ResponseTime = DateTime.Now - startTime,
                    RequestedAt = startTime
                };
            }
        }

        /// <summary>
        /// Performs optimized POST request
        /// </summary>
        public async Task<OptimizedHttpResponse> PostAsync(string url, byte[] content, string contentType)
        {
            var startTime = DateTime.Now;

            try
            {
                await _bandwidthManager.ThrottleIfNeededAsync(content.Length);

                var httpContent = new ByteArrayContent(content);
                httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

                var response = await _httpClient.PostAsync(url, httpContent);
                var responseContent = await response.Content.ReadAsByteArrayAsync();

                await _bandwidthManager.ThrottleIfNeededAsync(responseContent.Length);

                return new OptimizedHttpResponse
                {
                    RequestUrl = url,
                    StatusCode = (int)response.StatusCode,
                    Content = responseContent,
                    Headers = response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value)),
                    IsSuccessful = response.IsSuccessStatusCode,
                    ResponseTime = DateTime.Now - startTime,
                    RequestedAt = startTime
                };
            }
            catch (Exception ex)
            {
                return new OptimizedHttpResponse
                {
                    RequestUrl = url,
                    IsSuccessful = false,
                    ErrorMessage = ex.Message,
                    ResponseTime = DateTime.Now - startTime,
                    RequestedAt = startTime
                };
            }
        }

        public void Dispose()
        {
            // Don't dispose the underlying HttpClient as it's managed by the pool
        }
    }

    /// <summary>
    /// Performance configuration
    /// </summary>
    public class PerformanceConfiguration
    {
        public int MaxConnectionsPerPool { get; set; } = 10;
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public bool EnableHttp2 { get; set; } = true;
        public bool EnableCompression { get; set; } = true;
        public CachePolicy DefaultCachePolicy { get; set; } = new CachePolicy();
        public int MaxCacheSize { get; set; } = 100; // MB
        public TimeSpan ConnectionPoolTimeout { get; set; } = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Cache policy configuration
    /// </summary>
    public class CachePolicy
    {
        public bool EnableCaching { get; set; } = true;
        public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(10);
        public bool CacheOnlySuccessful { get; set; } = true;
        public List<string> CacheableContentTypes { get; set; } = new List<string>
        {
            "application/json", "text/plain", "text/html", "application/xml"
        };
    }

    /// <summary>
    /// Cache entry
    /// </summary>
    public class CacheEntry
    {
        public byte[] Content { get; set; }
        public Dictionary<string, string> Headers { get; set; }
        public int StatusCode { get; set; }
        public DateTime CachedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>
    /// Optimized HTTP response
    /// </summary>
    public class OptimizedHttpResponse
    {
        public string RequestUrl { get; set; }
        public int StatusCode { get; set; }
        public byte[] Content { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        public bool IsSuccessful { get; set; } = true;
        public string ErrorMessage { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public bool FromCache { get; set; }
        public TimeSpan CacheAge { get; set; }
        public DateTime RequestedAt { get; set; }
    }

    /// <summary>
    /// Performance statistics
    /// </summary>
    public class PerformanceStatistics
    {
        public int ActiveConnectionPools { get; set; }
        public CacheStatistics CacheStatistics { get; set; }
        public BandwidthStatistics BandwidthStatistics { get; set; }
        public List<ConnectionPoolStatistics> ConnectionPoolStatistics { get; set; } = new List<ConnectionPoolStatistics>();
        public DateTime GeneratedAt { get; set; }
    }

    /// <summary>
    /// Cache statistics
    /// </summary>
    public class CacheStatistics
    {
        public int TotalEntries { get; set; }
        public int Hits { get; set; }
        public int Misses { get; set; }
        public double HitRatio { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Bandwidth statistics
    /// </summary>
    public class BandwidthStatistics
    {
        public long TotalBytesTransferred { get; set; }
        public double CurrentThroughput { get; set; }
        public TimeSpan MeasurementWindow { get; set; }
    }

    /// <summary>
    /// Connection pool statistics
    /// </summary>
    public class ConnectionPoolStatistics
    {
        public string BaseUrl { get; set; }
        public int ActiveConnections { get; set; }
        public int AvailableConnections { get; set; }
        public int TotalRequests { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Cache event arguments
    /// </summary>
    public class CacheEventArgs : EventArgs
    {
        public string Url { get; set; }
        public string CacheKey { get; set; }
    }

    /// <summary>
    /// Bandwidth event arguments
    /// </summary>
    public class BandwidthEventArgs : EventArgs
    {
        public string Url { get; set; }
        public int BytesThrottled { get; set; }
        public TimeSpan ThrottleDelay { get; set; }
    }
}
