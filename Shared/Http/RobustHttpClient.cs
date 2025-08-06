using Shared.Platform;
using Shared.Security;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace Shared.Http
{
    /// <summary>
    /// Robust HTTP client with retry logic, connection pooling, and error recovery
    /// </summary>
    public class RobustHttpClient : IDisposable
    {
        private readonly IHttpClient _httpClient;
        private readonly ReplayAttackDetector _replayDetector;
        private readonly RetryConfiguration _retryConfig;
        private readonly Dictionary<string, ConnectionInfo> _connectionPool;
        private readonly SemaphoreSlim _connectionSemaphore;
        private bool _disposed;

        public event EventHandler<HttpClientErrorEventArgs> ErrorOccurred;
        public event EventHandler<HttpClientRetryEventArgs> RetryAttempt;

        public RobustHttpClient(RetryConfiguration retryConfig = null)
        {
            var platform = PlatformFactory.Current;
            _httpClient = platform.CreateHttpClient();
            _replayDetector = ReplayAttackDetector.Instance;
            _retryConfig = retryConfig ?? new RetryConfiguration();
            _connectionPool = new Dictionary<string, ConnectionInfo>();
            _connectionSemaphore = new SemaphoreSlim(_retryConfig.MaxConcurrentConnections, _retryConfig.MaxConcurrentConnections);

            // Set default timeout
            _httpClient.SetTimeout(TimeSpan.FromSeconds(_retryConfig.TimeoutSeconds));
        }

        /// <summary>
        /// Performs a GET request with retry logic
        /// </summary>
        public async Task<HttpClientResult> GetAsync(string url, Dictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                // Add replay protection headers
                var requestHeaders = headers ?? new Dictionary<string, string>();
                _replayDetector.AddReplayProtectionHeaders(requestHeaders);

                // Set headers
                foreach (var header in requestHeaders)
                {
                    _httpClient.SetHeader(header.Key, header.Value);
                }

                var response = await _httpClient.GetAsync(url);
                return new HttpClientResult
                {
                    StatusCode = response.StatusCode,
                    Headers = response.Headers,
                    Content = response.Content,
                    IsSuccessStatusCode = response.IsSuccessStatusCode,
                    Url = url,
                    Method = "GET"
                };
            }, url, "GET", cancellationToken);
        }

        /// <summary>
        /// Performs a POST request with retry logic
        /// </summary>
        public async Task<HttpClientResult> PostAsync(string url, byte[] data, string contentType = "application/json",
            Dictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                // Add replay protection headers
                var requestHeaders = headers ?? new Dictionary<string, string>();
                _replayDetector.AddReplayProtectionHeaders(requestHeaders);

                // Set headers
                foreach (var header in requestHeaders)
                {
                    _httpClient.SetHeader(header.Key, header.Value);
                }

                var response = await _httpClient.PostAsync(url, data, contentType);
                return new HttpClientResult
                {
                    StatusCode = response.StatusCode,
                    Headers = response.Headers,
                    Content = response.Content,
                    IsSuccessStatusCode = response.IsSuccessStatusCode,
                    Url = url,
                    Method = "POST"
                };
            }, url, "POST", cancellationToken);
        }

        /// <summary>
        /// Performs a POST request with string data
        /// </summary>
        public async Task<HttpClientResult> PostAsync(string url, string data, string contentType = "application/json",
            Dictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            return await PostAsync(url, bytes, contentType, headers, cancellationToken);
        }

        /// <summary>
        /// Executes a request with retry logic and exponential backoff
        /// </summary>
        private async Task<HttpClientResult> ExecuteWithRetryAsync(Func<Task<HttpClientResult>> operation,
            string url, string method, CancellationToken cancellationToken)
        {
            var attempt = 0;
            var delay = TimeSpan.FromMilliseconds(_retryConfig.InitialDelayMs);
            Exception lastException = null;

            // Acquire connection semaphore
            await _connectionSemaphore.WaitAsync(cancellationToken);

            try
            {
                while (attempt < _retryConfig.MaxRetries)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Track connection info
                        UpdateConnectionInfo(url, method);

                        // Execute the operation
                        var result = await operation();

                        // Check if we should retry based on status code
                        if (result.IsSuccessStatusCode || !ShouldRetry(result.StatusCode, attempt))
                        {
                            return result;
                        }

                        lastException = new HttpRequestException($"HTTP {result.StatusCode}: Request failed");
                    }
                    catch (Exception ex) when (ShouldRetryException(ex, attempt))
                    {
                        lastException = ex;
                    }

                    attempt++;

                    if (attempt < _retryConfig.MaxRetries)
                    {
                        // Notify about retry attempt
                        OnRetryAttempt(new HttpClientRetryEventArgs
                        {
                            Url = url,
                            Method = method,
                            Attempt = attempt,
                            Delay = delay,
                            Exception = lastException
                        });

                        // Wait with exponential backoff
                        await Task.Delay(delay, cancellationToken);
                        delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * _retryConfig.BackoffMultiplier, _retryConfig.MaxDelayMs));
                    }
                }

                // All retries exhausted
                OnError(new HttpClientErrorEventArgs
                {
                    Url = url,
                    Method = method,
                    Exception = lastException,
                    Message = $"Request failed after {_retryConfig.MaxRetries} attempts"
                });

                return new HttpClientResult
                {
                    StatusCode = 0,
                    IsSuccessStatusCode = false,
                    Url = url,
                    Method = method,
                    Exception = lastException
                };
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        /// <summary>
        /// Determines if a request should be retried based on status code
        /// </summary>
        private bool ShouldRetry(int statusCode, int attempt)
        {
            if (attempt >= _retryConfig.MaxRetries - 1)
                return false;

            // Retry on server errors and specific client errors
            return statusCode >= 500 || // Server errors
                   statusCode == 408 || // Request timeout
                   statusCode == 429 || // Too many requests
                   statusCode == 502 || // Bad gateway
                   statusCode == 503 || // Service unavailable
                   statusCode == 504;   // Gateway timeout
        }

        /// <summary>
        /// Determines if an exception should trigger a retry
        /// </summary>
        private bool ShouldRetryException(Exception ex, int attempt)
        {
            if (attempt >= _retryConfig.MaxRetries - 1)
                return false;

            // Retry on network-related exceptions
            return ex is TimeoutException ||
                   ex is TaskCanceledException ||
                   (ex.Message?.Contains("timeout", StringComparison.OrdinalIgnoreCase) == true) ||
                   (ex.Message?.Contains("connection", StringComparison.OrdinalIgnoreCase) == true) ||
                   (ex.Message?.Contains("network", StringComparison.OrdinalIgnoreCase) == true);
        }

        /// <summary>
        /// Updates connection information for monitoring
        /// </summary>
        private void UpdateConnectionInfo(string url, string method)
        {
            var uri = new Uri(url);
            var key = $"{uri.Host}:{uri.Port}";

            if (!_connectionPool.ContainsKey(key))
            {
                _connectionPool[key] = new ConnectionInfo
                {
                    Host = uri.Host,
                    Port = uri.Port,
                    FirstUsed = DateTime.Now
                };
            }

            var info = _connectionPool[key];
            info.LastUsed = DateTime.Now;
            info.RequestCount++;
        }

        /// <summary>
        /// Gets connection statistics
        /// </summary>
        public ConnectionStatistics GetConnectionStatistics()
        {
            var stats = new ConnectionStatistics
            {
                ActiveConnections = _connectionPool.Count,
                MaxConcurrentConnections = _retryConfig.MaxConcurrentConnections,
                AvailableConnections = _connectionSemaphore.CurrentCount
            };

            foreach (var connection in _connectionPool.Values)
            {
                stats.TotalRequests += connection.RequestCount;
            }

            return stats;
        }

        /// <summary>
        /// Raises the ErrorOccurred event
        /// </summary>
        private void OnError(HttpClientErrorEventArgs args)
        {
            System.Diagnostics.Debug.WriteLine($"HTTP Client Error: {args.Message}");
            ErrorOccurred?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the RetryAttempt event
        /// </summary>
        private void OnRetryAttempt(HttpClientRetryEventArgs args)
        {
            System.Diagnostics.Debug.WriteLine($"HTTP Client Retry: {args.Method} {args.Url} (attempt {args.Attempt})");
            RetryAttempt?.Invoke(this, args);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _httpClient?.Dispose();
                _connectionSemaphore?.Dispose();
            }
        }
    }

    /// <summary>
    /// HTTP client result
    /// </summary>
    public class HttpClientResult
    {
        public int StatusCode { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        public byte[] Content { get; set; }
        public bool IsSuccessStatusCode { get; set; }
        public string Url { get; set; }
        public string Method { get; set; }
        public Exception Exception { get; set; }
        public TimeSpan Duration { get; set; }

        public string ContentAsString => Content != null ? Encoding.UTF8.GetString(Content) : string.Empty;

        public T DeserializeContent<T>()
        {
            if (Content == null || Content.Length == 0)
                return default(T);

            var json = ContentAsString;
            return Internet.DeserializeObject<T>(json);
        }
    }

    /// <summary>
    /// Retry configuration
    /// </summary>
    public class RetryConfiguration
    {
        #region Default Configuration Constants
        /// <summary>
        /// Default maximum number of retry attempts.
        /// </summary>
        private const int DefaultMaxRetries = 3;

        /// <summary>
        /// Default initial delay between retries in milliseconds.
        /// </summary>
        private const int DefaultInitialDelayMs = 1000;

        /// <summary>
        /// Default maximum delay between retries in milliseconds.
        /// </summary>
        private const int DefaultMaxDelayMs = 30000;

        /// <summary>
        /// Default backoff multiplier for exponential backoff.
        /// </summary>
        private const double DefaultBackoffMultiplier = 2.0;

        /// <summary>
        /// Default timeout for HTTP requests in seconds.
        /// </summary>
        private const int DefaultTimeoutSeconds = 30;

        /// <summary>
        /// Default maximum number of concurrent connections.
        /// </summary>
        private const int DefaultMaxConcurrentConnections = 10;
        #endregion

        public int MaxRetries { get; set; } = DefaultMaxRetries;
        public int InitialDelayMs { get; set; } = DefaultInitialDelayMs;
        public int MaxDelayMs { get; set; } = DefaultMaxDelayMs;
        public double BackoffMultiplier { get; set; } = DefaultBackoffMultiplier;
        public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
        public int MaxConcurrentConnections { get; set; } = DefaultMaxConcurrentConnections;
    }

    /// <summary>
    /// Connection information
    /// </summary>
    public class ConnectionInfo
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public DateTime FirstUsed { get; set; }
        public DateTime LastUsed { get; set; }
        public int RequestCount { get; set; }
    }

    /// <summary>
    /// Connection statistics
    /// </summary>
    public class ConnectionStatistics
    {
        public int ActiveConnections { get; set; }
        public int MaxConcurrentConnections { get; set; }
        public int AvailableConnections { get; set; }
        public int TotalRequests { get; set; }
    }

    /// <summary>
    /// HTTP client error event arguments
    /// </summary>
    public class HttpClientErrorEventArgs : EventArgs
    {
        public string Url { get; set; }
        public string Method { get; set; }
        public Exception Exception { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// HTTP client retry event arguments
    /// </summary>
    public class HttpClientRetryEventArgs : EventArgs
    {
        public string Url { get; set; }
        public string Method { get; set; }
        public int Attempt { get; set; }
        public TimeSpan Delay { get; set; }
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// HTTP request exception
    /// </summary>
    public class HttpRequestException : Exception
    {
        public HttpRequestException(string message) : base(message) { }
        public HttpRequestException(string message, Exception innerException) : base(message, innerException) { }
    }
}
