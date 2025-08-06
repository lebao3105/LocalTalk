using Shared.Platform;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Shared.Security
{
    /// <summary>
    /// Analyzes and mitigates network security vulnerabilities
    /// </summary>
    public class SecurityAnalyzer : IDisposable
    {
        // Security analysis constants
        private const int CleanupIntervalDivisor = 12;  // Cleanup 12 times per expiry period
        private const int RateLimitTimeWindowMinutes = 1;  // Rate limit time window in minutes

        private static readonly Lazy<SecurityAnalyzer> _instance =
            new Lazy<SecurityAnalyzer>(() => new SecurityAnalyzer());
        private readonly ICryptographyProvider _cryptoProvider;
        private readonly ConcurrentDictionary<string, SecurityThreat> _detectedThreats;
        private readonly SecurityConfiguration _config;
        private readonly Timer _cleanupTimer;
        private readonly ILogger _logger;
        private bool _disposed = false;

        // Performance optimization: Cache analysis results to avoid redundant processing
        private readonly ConcurrentDictionary<string, SecurityAnalysisResult> _analysisCache;
        private readonly Timer _cacheCleanupTimer;

        // Enhanced security: Rate limiting and DDoS protection
        private readonly ConcurrentDictionary<string, RateLimitInfo> _rateLimitCache;
        private readonly ConcurrentDictionary<string, int> _requestCounts;
        private readonly Timer _rateLimitCleanupTimer;

        /// <summary>
        /// Gets the singleton instance of the SecurityAnalyzer
        /// </summary>
        public static SecurityAnalyzer Instance => _instance.Value;

        private SecurityAnalyzer()
        {
            _logger = LogManager.GetLogger<SecurityAnalyzer>();
            _cryptoProvider = PlatformFactory.Current.GetCryptographyProvider();
            _detectedThreats = new ConcurrentDictionary<string, SecurityThreat>();
            _config = new SecurityConfiguration();
            _config.LoadFrom(ConfigurationManager.Current);

            // Initialize analysis cache for performance optimization
            _analysisCache = new ConcurrentDictionary<string, SecurityAnalysisResult>();

            // Initialize rate limiting for DDoS protection
            _rateLimitCache = new ConcurrentDictionary<string, RateLimitInfo>();
            _requestCounts = new ConcurrentDictionary<string, int>();

            // Setup cleanup timer to remove old threats
            var cleanupInterval = TimeSpan.FromMinutes(_config.ThreatCacheExpiryMinutes / CleanupIntervalDivisor);
            _cleanupTimer = new Timer(CleanupOldThreats, null, cleanupInterval, cleanupInterval);

            // Setup cache cleanup timer (clean cache every 5 minutes)
            _cacheCleanupTimer = new Timer(CleanupAnalysisCache, null,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

            // Setup rate limit cleanup timer (clean every minute)
            _rateLimitCleanupTimer = new Timer(CleanupRateLimitCache, null,
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

            _logger.Info("SecurityAnalyzer initialized with performance optimizations");
        }

        /// <summary>
        /// Analyzes incoming HTTP requests for security threats
        /// </summary>
        /// <param name="remoteAddress">Remote IP address of the request</param>
        /// <param name="path">Request path</param>
        /// <param name="headers">HTTP headers</param>
        /// <param name="body">Request body</param>
        /// <returns>Security analysis result</returns>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are null</exception>
        /// <exception cref="ObjectDisposedException">Thrown when analyzer is disposed</exception>
        public SecurityAnalysisResult AnalyzeRequest(string remoteAddress, string path,
            Dictionary<string, string> headers, byte[] body)
        {
            using (PerformanceManager.Time("SecurityAnalyzer.AnalyzeRequest"))
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(remoteAddress))
                    throw new ArgumentNullException(nameof(remoteAddress));
                if (path == null)
                    throw new ArgumentNullException(nameof(path));
                if (headers == null)
                    throw new ArgumentNullException(nameof(headers));

                // Enhanced security: Check rate limiting first
                if (IsRateLimited(remoteAddress))
                {
                    var rateLimitResult = new SecurityAnalysisResult
                    {
                        RemoteAddress = remoteAddress,
                        Path = path,
                        ThreatLevel = ThreatLevel.Critical,
                        Timestamp = DateTime.Now,
                        IsBlocked = true
                    };
                    rateLimitResult.Threats.Add(new SecurityThreat
                    {
                        Type = ThreatType.RateLimit,
                        Severity = ThreatSeverity.Critical,
                        Description = "Request rate limit exceeded",
                        RemoteAddress = remoteAddress,
                        DetectedAt = DateTime.Now
                    });
                    PerformanceManager.Counter("SecurityAnalyzer.RateLimitBlocked");
                    return rateLimitResult;
                }

                // Performance optimization: Check cache first for identical requests
                var cacheKey = GenerateCacheKey(remoteAddress, path, headers, body);
                if (_analysisCache.TryGetValue(cacheKey, out var cachedResult))
                {
                    // Update timestamp but return cached analysis
                    cachedResult.Timestamp = DateTime.Now;
                    PerformanceManager.Counter("SecurityAnalyzer.CacheHits");
                    return cachedResult;
                }

                PerformanceManager.Counter("SecurityAnalyzer.RequestsAnalyzed");

                var result = new SecurityAnalysisResult
                {
                    RemoteAddress = remoteAddress,
                    RequestPath = path,
                    Timestamp = DateTime.Now,
                    ThreatLevel = ThreatLevel.None
                };

            var threats = new List<SecurityThreat>();

            // 1. Rate limiting analysis
            var rateLimitThreat = AnalyzeRateLimit(remoteAddress);
            if (rateLimitThreat != null)
            {
                threats.Add(rateLimitThreat);
            }

            // 2. Path traversal analysis
            var pathTraversalThreat = AnalyzePathTraversal(path);
            if (pathTraversalThreat != null)
            {
                threats.Add(pathTraversalThreat);
            }

            // 3. Header injection analysis
            var headerInjectionThreat = AnalyzeHeaderInjection(headers);
            if (headerInjectionThreat != null)
            {
                threats.Add(headerInjectionThreat);
            }

            // 4. Content length validation
            var contentLengthThreat = AnalyzeContentLength(headers, body);
            if (contentLengthThreat != null)
            {
                threats.Add(contentLengthThreat);
            }

            // 5. Malicious payload analysis
            var payloadThreat = AnalyzePayload(body);
            if (payloadThreat != null)
            {
                threats.Add(payloadThreat);
            }

            // 6. User-Agent analysis
            var userAgentThreat = AnalyzeUserAgent(headers);
            if (userAgentThreat != null)
            {
                threats.Add(userAgentThreat);
            }

            result.Threats = threats;
                result.ThreatLevel = threats.Any() ? threats.Max(t => t.Level) : ThreatLevel.None;

                // Log high-severity threats
                if (result.ThreatLevel >= ThreatLevel.High)
                {
                    LogSecurityThreat(result);
                    PerformanceManager.Counter($"SecurityAnalyzer.Threats.{result.ThreatLevel}");
                }

                PerformanceManager.Metric("SecurityAnalyzer.ThreatCount", result.Threats.Count);

                // Cache the result for future identical requests (only cache for 5 minutes)
                if (result.ThreatLevel <= ThreatLevel.Medium) // Don't cache high-threat results
                {
                    _analysisCache.TryAdd(cacheKey, result);
                }

                return result;
            }
        }

        /// <summary>
        /// Analyzes rate limiting to prevent DoS attacks
        /// </summary>
        private SecurityThreat AnalyzeRateLimit(string remoteAddress)
        {
            var key = $"rate_limit_{remoteAddress}";
            var now = DateTime.Now;

            var threat = _detectedThreats.AddOrUpdate(key,
                // Add new threat
                new SecurityThreat
                {
                    Type = ThreatType.RateLimit,
                    RemoteAddress = remoteAddress,
                    FirstDetected = now,
                    Count = 1
                },
                // Update existing threat
                (k, existingThreat) =>
                {
                    existingThreat.Count++;
                    existingThreat.LastDetected = now;
                    return existingThreat;
                });

            // If this was a new threat, don't flag it yet
            if (threat.Count == 1)
                return null;

            // Check if rate limit exceeded
            var timeWindow = now - threat.FirstDetected;
            var withinTimeWindow = timeWindow.TotalMinutes <= RateLimitTimeWindowMinutes;
            var exceedsRequestLimit = threat.Count > _config.MaxRequestsPerMinute;

            if (withinTimeWindow && exceedsRequestLimit)
            {
                threat.Level = ThreatLevel.High;
                threat.Description = $"Rate limit exceeded: {threat.Count} requests in " +
                    $"{timeWindow.TotalSeconds:F1} seconds";
                return threat;
            }

            // Reset counter if time window passed
            if (timeWindow.TotalMinutes > RateLimitTimeWindowMinutes)
            {
                threat.FirstDetected = now;
                threat.Count = 1;
            }

            return null;
        }

        /// <summary>
        /// Analyzes path traversal attempts
        /// </summary>
        private SecurityThreat AnalyzePathTraversal(string path)
        {
            var suspiciousPatterns = new[]
            {
                "../", "..\\", "%2e%2e%2f", "%2e%2e%5c",
                "....//", "....\\\\", "%252e%252e%252f"
            };

            foreach (var pattern in suspiciousPatterns)
            {
                if (path.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return new SecurityThreat
                    {
                        Type = ThreatType.PathTraversal,
                        Level = ThreatLevel.High,
                        Description = $"Path traversal attempt detected: {pattern}",
                        FirstDetected = DateTime.Now
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// Analyzes header injection attempts
        /// </summary>
        private SecurityThreat AnalyzeHeaderInjection(Dictionary<string, string> headers)
        {
            var suspiciousChars = new[] { '\r', '\n', '\0' };

            foreach (var header in headers)
            {
                if (header.Key.IndexOfAny(suspiciousChars) >= 0 ||
                    header.Value.IndexOfAny(suspiciousChars) >= 0)
                {
                    return new SecurityThreat
                    {
                        Type = ThreatType.HeaderInjection,
                        Level = ThreatLevel.High,
                        Description = $"Header injection attempt in {header.Key}",
                        FirstDetected = DateTime.Now
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// Analyzes content length for potential buffer overflow
        /// </summary>
        private SecurityThreat AnalyzeContentLength(Dictionary<string, string> headers, byte[] body)
        {
            if (body == null) return null;

            // Check for extremely large payloads
            if (body.Length > _config.MaxPayloadSize)
            {
                return new SecurityThreat
                {
                    Type = ThreatType.BufferOverflow,
                    Level = ThreatLevel.Medium,
                    Description = $"Oversized payload: {body.Length} bytes (max: {_config.MaxPayloadSize})",
                    FirstDetected = DateTime.Now
                };
            }

            // Check content-length header mismatch
            if (headers.TryGetValue("Content-Length", out var contentLengthStr))
            {
                if (int.TryParse(contentLengthStr, out var contentLength))
                {
                    if (Math.Abs(contentLength - body.Length) > 0)
                    {
                        return new SecurityThreat
                        {
                            Type = ThreatType.ContentLengthMismatch,
                            Level = ThreatLevel.Medium,
                            Description = $"Content-Length mismatch: header={contentLength}, actual={body.Length}",
                            FirstDetected = DateTime.Now
                        };
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Analyzes payload for malicious content
        /// </summary>
        private SecurityThreat AnalyzePayload(byte[] body)
        {
            if (body == null || body.Length == 0) return null;

            try
            {
                var content = Encoding.UTF8.GetString(body);

                // Check for script injection
                var scriptPatterns = new[]
                {
                    "<script", "javascript:", "vbscript:", "onload=", "onerror=",
                    "eval(", "setTimeout(", "setInterval("
                };

                foreach (var pattern in scriptPatterns)
                {
                    if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        return new SecurityThreat
                        {
                            Type = ThreatType.ScriptInjection,
                            Level = ThreatLevel.High,
                            Description = $"Script injection attempt detected: {pattern}",
                            FirstDetected = DateTime.Now
                        };
                    }
                }

                // Check for SQL injection patterns
                var sqlPatterns = new[]
                {
                    "' OR '1'='1", "'; DROP TABLE", "UNION SELECT", "INSERT INTO",
                    "DELETE FROM", "UPDATE SET", "CREATE TABLE"
                };

                foreach (var pattern in sqlPatterns)
                {
                    if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        return new SecurityThreat
                        {
                            Type = ThreatType.SqlInjection,
                            Level = ThreatLevel.High,
                            Description = $"SQL injection attempt detected: {pattern}",
                            FirstDetected = DateTime.Now
                        };
                    }
                }
            }
            catch
            {
                // If we can't decode as UTF-8, check for binary exploits
                return AnalyzeBinaryPayload(body);
            }

            return null;
        }

        /// <summary>
        /// Analyzes binary payload for exploits
        /// </summary>
        private SecurityThreat AnalyzeBinaryPayload(byte[] body)
        {
            // Check for executable signatures
            var executableSignatures = new Dictionary<byte[], string>
            {
                { new byte[] { 0x4D, 0x5A }, "PE executable" },
                { new byte[] { 0x7F, 0x45, 0x4C, 0x46 }, "ELF executable" },
                { new byte[] { 0xFE, 0xED, 0xFA, 0xCE }, "Mach-O executable" },
                { new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }, "Java class file" }
            };

            foreach (var signature in executableSignatures)
            {
                if (body.Length >= signature.Key.Length)
                {
                    bool matches = true;
                    for (int i = 0; i < signature.Key.Length; i++)
                    {
                        if (body[i] != signature.Key[i])
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (matches)
                    {
                        return new SecurityThreat
                        {
                            Type = ThreatType.MaliciousExecutable,
                            Level = ThreatLevel.Critical,
                            Description = $"Executable file detected: {signature.Value}",
                            FirstDetected = DateTime.Now
                        };
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Analyzes User-Agent header for suspicious patterns
        /// </summary>
        private SecurityThreat AnalyzeUserAgent(Dictionary<string, string> headers)
        {
            if (!headers.TryGetValue("User-Agent", out var userAgent))
            {
                return new SecurityThreat
                {
                    Type = ThreatType.SuspiciousUserAgent,
                    Level = ThreatLevel.Low,
                    Description = "Missing User-Agent header",
                    FirstDetected = DateTime.Now
                };
            }

            // Check for known malicious user agents
            var maliciousPatterns = new[]
            {
                "sqlmap", "nikto", "nmap", "masscan", "zap", "burp",
                "wget", "curl", "python-requests", "go-http-client"
            };

            foreach (var pattern in maliciousPatterns)
            {
                if (userAgent.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return new SecurityThreat
                    {
                        Type = ThreatType.SuspiciousUserAgent,
                        Level = ThreatLevel.Medium,
                        Description = $"Suspicious User-Agent: {pattern}",
                        FirstDetected = DateTime.Now
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// Logs security threats
        /// </summary>
        private void LogSecurityThreat(SecurityAnalysisResult result)
        {
            var message = $"SECURITY THREAT DETECTED - Level: {result.ThreatLevel}, " +
                         $"Address: {result.RemoteAddress}, Path: {result.RequestPath}, " +
                         $"Threats: {string.Join(", ", result.Threats.Select(t => t.Type))}";

            var logLevel = result.ThreatLevel switch
            {
                ThreatLevel.Critical => LogLevel.Critical,
                ThreatLevel.High => LogLevel.Error,
                ThreatLevel.Medium => LogLevel.Warning,
                ThreatLevel.Low => LogLevel.Info,
                _ => LogLevel.Debug
            };

            _logger.Log(logLevel, message);
        }

        /// <summary>
        /// Gets security statistics
        /// </summary>
        public SecurityStatistics GetStatistics()
        {
            var stats = new SecurityStatistics();

            foreach (var threat in _detectedThreats.Values)
            {
                stats.TotalThreats++;

                switch (threat.Level)
                {
                    case ThreatLevel.Low:
                        stats.LowThreats++;
                        break;
                    case ThreatLevel.Medium:
                        stats.MediumThreats++;
                        break;
                    case ThreatLevel.High:
                        stats.HighThreats++;
                        break;
                    case ThreatLevel.Critical:
                        stats.CriticalThreats++;
                        break;
                }
            }

            return stats;
        }

        /// <summary>
        /// Cleans up old threats from the cache
        /// </summary>
        private void CleanupOldThreats(object state)
        {
            if (_disposed) return;

            var cutoffTime = DateTime.Now - _config.ThreatCacheExpiry;
            var keysToRemove = new List<string>();

            foreach (var kvp in _detectedThreats)
            {
                if (kvp.Value.LastDetected < cutoffTime)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _detectedThreats.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// Generates a cache key for request analysis results
        /// </summary>
        private string GenerateCacheKey(string remoteAddress, string path,
            Dictionary<string, string> headers, byte[] body)
        {
            var sb = new StringBuilder();
            sb.Append(remoteAddress);
            sb.Append('|');
            sb.Append(path);
            sb.Append('|');

            // Include relevant headers that affect security analysis
            var relevantHeaders = new[] { "User-Agent", "Content-Type", "Authorization" };
            foreach (var headerName in relevantHeaders)
            {
                if (headers.TryGetValue(headerName, out var value))
                {
                    sb.Append(headerName);
                    sb.Append(':');
                    sb.Append(value);
                    sb.Append('|');
                }
            }

            // Include body hash for POST requests
            if (body != null && body.Length > 0)
            {
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    var hash = sha256.ComputeHash(body);
                    sb.Append(Convert.ToBase64String(hash));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Cleans up old analysis cache entries
        /// </summary>
        private void CleanupAnalysisCache(object state)
        {
            if (_disposed) return;

            try
            {
                var cutoffTime = DateTime.Now.AddMinutes(-10); // Cache entries older than 10 minutes
                var keysToRemove = new List<string>();

                foreach (var kvp in _analysisCache)
                {
                    if (kvp.Value.Timestamp < cutoffTime)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _analysisCache.TryRemove(key, out _);
                }

                if (keysToRemove.Count > 0)
                {
                    _logger.Debug($"Cleaned up {keysToRemove.Count} old analysis cache entries");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error during analysis cache cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a remote address is rate limited
        /// </summary>
        private bool IsRateLimited(string remoteAddress)
        {
            if (string.IsNullOrEmpty(remoteAddress))
                return false;

            var now = DateTime.Now;
            var windowStart = now.AddMinutes(-1); // 1-minute window

            // Get or create rate limit info
            var rateLimitInfo = _rateLimitCache.GetOrAdd(remoteAddress, _ => new RateLimitInfo
            {
                RemoteAddress = remoteAddress,
                FirstRequestTime = now,
                RequestCount = 0,
                LastRequestTime = now
            });

            lock (rateLimitInfo)
            {
                // Reset window if needed
                if (rateLimitInfo.FirstRequestTime < windowStart)
                {
                    rateLimitInfo.FirstRequestTime = now;
                    rateLimitInfo.RequestCount = 0;
                }

                rateLimitInfo.RequestCount++;
                rateLimitInfo.LastRequestTime = now;

                // Check if rate limit exceeded (100 requests per minute)
                return rateLimitInfo.RequestCount > 100;
            }
        }

        /// <summary>
        /// Cleans up old rate limit cache entries
        /// </summary>
        private void CleanupRateLimitCache(object state)
        {
            if (_disposed) return;

            try
            {
                var cutoffTime = DateTime.Now.AddMinutes(-5); // Remove entries older than 5 minutes
                var keysToRemove = new List<string>();

                foreach (var kvp in _rateLimitCache)
                {
                    if (kvp.Value.LastRequestTime < cutoffTime)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _rateLimitCache.TryRemove(key, out _);
                    _requestCounts.TryRemove(key, out _);
                }

                if (keysToRemove.Count > 0)
                {
                    _logger.Debug($"Cleaned up {keysToRemove.Count} old rate limit entries");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error during rate limit cache cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Throws ObjectDisposedException if the analyzer has been disposed
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when analyzer is disposed</exception>
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SecurityAnalyzer));
        }

        /// <summary>
        /// Disposes the SecurityAnalyzer and cleans up resources
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
                _cleanupTimer?.Dispose();
                _cacheCleanupTimer?.Dispose();
                _rateLimitCleanupTimer?.Dispose();
                _detectedThreats.Clear();
                _analysisCache.Clear();
                _rateLimitCache.Clear();
                _requestCounts.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Rate limiting information for DDoS protection
    /// </summary>
    internal class RateLimitInfo
    {
        public string RemoteAddress { get; set; }
        public DateTime FirstRequestTime { get; set; }
        public DateTime LastRequestTime { get; set; }
        public int RequestCount { get; set; }
        public bool IsBlocked { get; set; }
    }

    /// <summary>
    /// Security analysis result
    /// </summary>
    public class SecurityAnalysisResult
    {
        /// <summary>
        /// Remote IP address of the analyzed request
        /// </summary>
        public string RemoteAddress { get; set; }

        /// <summary>
        /// Request path that was analyzed
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Request path that was analyzed (alias for compatibility)
        /// </summary>
        public string RequestPath
        {
            get => Path;
            set => Path = value;
        }

        /// <summary>
        /// Timestamp when the analysis was performed
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Overall threat level of the request
        /// </summary>
        public ThreatLevel ThreatLevel { get; set; }

        /// <summary>
        /// List of detected security threats
        /// </summary>
        public List<SecurityThreat> Threats { get; set; } = new List<SecurityThreat>();

        /// <summary>
        /// Indicates whether the request should be blocked based on threat level
        /// </summary>
        public bool ShouldBlock => ThreatLevel >= ThreatLevel.High;

        /// <summary>
        /// Indicates whether the request should be logged based on threat level
        /// </summary>
        public bool ShouldLog => ThreatLevel >= ThreatLevel.Medium;

        /// <summary>
        /// Indicates whether the request is blocked
        /// </summary>
        public bool IsBlocked { get; set; }
    }

    /// <summary>
    /// Security threat information
    /// </summary>
    public class SecurityThreat
    {
        /// <summary>
        /// Type of security threat detected
        /// </summary>
        public ThreatType Type { get; set; }

        /// <summary>
        /// Severity level of the threat
        /// </summary>
        public ThreatLevel Level { get; set; }

        /// <summary>
        /// Severity level of the threat (alias for compatibility)
        /// </summary>
        public ThreatSeverity Severity
        {
            get => (ThreatSeverity)Level;
            set => Level = (ThreatLevel)value;
        }

        /// <summary>
        /// Human-readable description of the threat
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Remote IP address associated with the threat
        /// </summary>
        public string RemoteAddress { get; set; }

        /// <summary>
        /// Timestamp when the threat was first detected
        /// </summary>
        public DateTime FirstDetected { get; set; }

        /// <summary>
        /// Timestamp when the threat was last detected
        /// </summary>
        public DateTime LastDetected { get; set; }

        /// <summary>
        /// Timestamp when the threat was detected (alias for compatibility)
        /// </summary>
        public DateTime DetectedAt
        {
            get => FirstDetected;
            set => FirstDetected = value;
        }

        /// <summary>
        /// Number of times this threat has been detected
        /// </summary>
        public int Count { get; set; }
    }

    /// <summary>
    /// Types of security threats
    /// </summary>
    public enum ThreatType
    {
        RateLimit,
        PathTraversal,
        HeaderInjection,
        BufferOverflow,
        ContentLengthMismatch,
        ScriptInjection,
        SqlInjection,
        MaliciousExecutable,
        SuspiciousUserAgent,
        ReplayAttack,
        ManInTheMiddle,
        CertificateValidation
    }

    /// <summary>
    /// Threat severity levels
    /// </summary>
    public enum ThreatLevel
    {
        None = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

    /// <summary>
    /// Threat severity levels (alias for compatibility)
    /// </summary>
    public enum ThreatSeverity
    {
        None = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

    /// <summary>
    /// Security configuration
    /// </summary>
    public class SecurityConfiguration : IConfigurationSection
    {
        public string SectionName => "Security";

        public int MaxRequestsPerMinute { get; set; } = 100;
        public int ThreatCacheExpiryMinutes { get; set; } = 60;
        public int MaxPayloadSize { get; set; } = 1024 * 1024 * 10; // 10MB
        public int MaxHeaderSize { get; set; } = 8192; // 8KB
        public bool EnableRateLimiting { get; set; } = true;
        public bool EnablePayloadAnalysis { get; set; } = true;
        public bool EnableHeaderValidation { get; set; } = true;
        public bool EnableSqlInjectionDetection { get; set; } = true;
        public bool EnableXssDetection { get; set; } = true;
        public bool EnablePathTraversalDetection { get; set; } = true;
        public bool BlockSuspiciousRequests { get; set; } = true;

        public TimeSpan ThreatCacheExpiry => TimeSpan.FromMinutes(ThreatCacheExpiryMinutes);

        public void LoadFrom(IConfiguration configuration)
        {
            MaxRequestsPerMinute = configuration.GetValue($"{SectionName}.MaxRequestsPerMinute", MaxRequestsPerMinute);
            ThreatCacheExpiryMinutes = configuration.GetValue(
                $"{SectionName}.ThreatCacheExpiryMinutes", ThreatCacheExpiryMinutes);
            MaxPayloadSize = configuration.GetValue($"{SectionName}.MaxPayloadSize", MaxPayloadSize);
            MaxHeaderSize = configuration.GetValue($"{SectionName}.MaxHeaderSize", MaxHeaderSize);
            EnableRateLimiting = configuration.GetValue($"{SectionName}.EnableRateLimiting", EnableRateLimiting);
            EnablePayloadAnalysis = configuration.GetValue(
                $"{SectionName}.EnablePayloadAnalysis", EnablePayloadAnalysis);
            EnableHeaderValidation = configuration.GetValue(
                $"{SectionName}.EnableHeaderValidation", EnableHeaderValidation);
            EnableSqlInjectionDetection = configuration.GetValue(
                $"{SectionName}.EnableSqlInjectionDetection", EnableSqlInjectionDetection);
            EnableXssDetection = configuration.GetValue($"{SectionName}.EnableXssDetection", EnableXssDetection);
            EnablePathTraversalDetection = configuration.GetValue(
                $"{SectionName}.EnablePathTraversalDetection", EnablePathTraversalDetection);
            BlockSuspiciousRequests = configuration.GetValue(
                $"{SectionName}.BlockSuspiciousRequests", BlockSuspiciousRequests);
        }

        public void SaveTo(IConfiguration configuration)
        {
            configuration.SetValue($"{SectionName}.MaxRequestsPerMinute", MaxRequestsPerMinute);
            configuration.SetValue($"{SectionName}.ThreatCacheExpiryMinutes", ThreatCacheExpiryMinutes);
            configuration.SetValue($"{SectionName}.MaxPayloadSize", MaxPayloadSize);
            configuration.SetValue($"{SectionName}.MaxHeaderSize", MaxHeaderSize);
            configuration.SetValue($"{SectionName}.EnableRateLimiting", EnableRateLimiting);
            configuration.SetValue($"{SectionName}.EnablePayloadAnalysis", EnablePayloadAnalysis);
            configuration.SetValue($"{SectionName}.EnableHeaderValidation", EnableHeaderValidation);
            configuration.SetValue($"{SectionName}.EnableSqlInjectionDetection", EnableSqlInjectionDetection);
            configuration.SetValue($"{SectionName}.EnableXssDetection", EnableXssDetection);
            configuration.SetValue($"{SectionName}.EnablePathTraversalDetection", EnablePathTraversalDetection);
            configuration.SetValue($"{SectionName}.BlockSuspiciousRequests", BlockSuspiciousRequests);
        }

        public ConfigurationValidationResult Validate()
        {
            var result = new ConfigurationValidationResult { IsValid = true };

            if (MaxRequestsPerMinute <= 0)
                result.AddError("MaxRequestsPerMinute must be greater than 0");

            if (ThreatCacheExpiryMinutes <= 0)
                result.AddError("ThreatCacheExpiryMinutes must be greater than 0");

            if (MaxPayloadSize <= 0)
                result.AddError("MaxPayloadSize must be greater than 0");

            if (MaxHeaderSize <= 0)
                result.AddError("MaxHeaderSize must be greater than 0");

            if (MaxRequestsPerMinute > 10000)
                result.AddWarning("MaxRequestsPerMinute is very high, consider lowering for better security");

            return result;
        }
    }

    /// <summary>
    /// Security statistics
    /// </summary>
    public class SecurityStatistics
    {
        public int TotalThreats { get; set; }
        public int LowThreats { get; set; }
        public int MediumThreats { get; set; }
        public int HighThreats { get; set; }
        public int CriticalThreats { get; set; }
        public int BlockedRequests { get; set; }
        public DateTime LastThreatDetected { get; set; }
    }
}
