using Shared.Platform;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Shared.Security
{
    /// <summary>
    /// Detects and prevents replay attacks using nonce and timestamp validation
    /// </summary>
    public class ReplayAttackDetector
    {
        private static ReplayAttackDetector _instance;
        private readonly ConcurrentDictionary<string, RequestSignature> _requestCache;
        private readonly ICryptographyProvider _cryptoProvider;
        private readonly Timer _cleanupTimer;
        private readonly TimeSpan _requestValidityWindow;

        public static ReplayAttackDetector Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ReplayAttackDetector();
                }
                return _instance;
            }
        }

        private ReplayAttackDetector()
        {
            _requestCache = new ConcurrentDictionary<string, RequestSignature>();
            _cryptoProvider = PlatformFactory.Current.GetCryptographyProvider();
            _requestValidityWindow = TimeSpan.FromMinutes(5); // 5-minute window
            
            // Start cleanup timer to remove expired requests
            _cleanupTimer = new Timer(CleanupExpiredRequests, null, 
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// Validates a request to detect replay attacks
        /// </summary>
        public ReplayValidationResult ValidateRequest(string method, string path, 
            Dictionary<string, string> headers, byte[] body, string remoteAddress)
        {
            var result = new ReplayValidationResult
            {
                IsValid = true,
                RemoteAddress = remoteAddress,
                Timestamp = DateTime.Now
            };

            try
            {
                // Extract timestamp and nonce from headers
                if (!headers.TryGetValue("X-Timestamp", out var timestampStr) ||
                    !headers.TryGetValue("X-Nonce", out var nonce))
                {
                    // If no replay protection headers, allow but warn
                    result.IsValid = true;
                    result.Warning = "No replay protection headers found";
                    return result;
                }

                // Validate timestamp format and freshness
                if (!DateTime.TryParse(timestampStr, out var requestTimestamp))
                {
                    result.IsValid = false;
                    result.Reason = "Invalid timestamp format";
                    return result;
                }

                var now = DateTime.Now;
                var timeDifference = Math.Abs((now - requestTimestamp).TotalMinutes);

                if (timeDifference > _requestValidityWindow.TotalMinutes)
                {
                    result.IsValid = false;
                    result.Reason = $"Request timestamp too old: {timeDifference:F1} minutes";
                    return result;
                }

                // Generate request signature
                var signature = GenerateRequestSignature(method, path, headers, body, nonce, timestampStr);
                
                // Check if we've seen this exact request before
                if (_requestCache.ContainsKey(signature))
                {
                    result.IsValid = false;
                    result.Reason = "Duplicate request detected (replay attack)";
                    result.OriginalRequestTime = _requestCache[signature].Timestamp;
                    return result;
                }

                // Store the request signature
                _requestCache[signature] = new RequestSignature
                {
                    Signature = signature,
                    Timestamp = now,
                    RemoteAddress = remoteAddress,
                    Method = method,
                    Path = path
                };

                result.RequestSignature = signature;
                return result;
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Reason = $"Error validating request: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Generates a unique signature for a request
        /// </summary>
        private string GenerateRequestSignature(string method, string path, 
            Dictionary<string, string> headers, byte[] body, string nonce, string timestamp)
        {
            var signatureData = new List<string>
            {
                method?.ToUpperInvariant() ?? "",
                path ?? "",
                nonce ?? "",
                timestamp ?? ""
            };

            // Include relevant headers in signature (excluding variable ones)
            var relevantHeaders = new[] { "Content-Type", "Authorization", "X-Device-Id" };
            foreach (var headerName in relevantHeaders)
            {
                if (headers.TryGetValue(headerName, out var headerValue))
                {
                    signatureData.Add($"{headerName}:{headerValue}");
                }
            }

            // Include body hash if present
            if (body != null && body.Length > 0)
            {
                var bodyHash = _cryptoProvider.ComputeHash(body, HashAlgorithmType.SHA256);
                signatureData.Add($"body:{bodyHash}");
            }

            // Combine all signature components
            var combinedData = string.Join("|", signatureData);
            
            // Generate final signature hash
            return _cryptoProvider.ComputeHash(combinedData, HashAlgorithmType.SHA256);
        }

        /// <summary>
        /// Generates a nonce for client requests
        /// </summary>
        public string GenerateNonce()
        {
            return _cryptoProvider.GenerateRandomString(32);
        }

        /// <summary>
        /// Generates a timestamp for client requests
        /// </summary>
        public string GenerateTimestamp()
        {
            return DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }

        /// <summary>
        /// Adds replay protection headers to outgoing requests
        /// </summary>
        public void AddReplayProtectionHeaders(Dictionary<string, string> headers)
        {
            headers["X-Timestamp"] = GenerateTimestamp();
            headers["X-Nonce"] = GenerateNonce();
        }

        /// <summary>
        /// Cleans up expired request signatures
        /// </summary>
        private void CleanupExpiredRequests(object state)
        {
            var now = DateTime.Now;
            var expiredKeys = new List<string>();

            foreach (var kvp in _requestCache)
            {
                var age = now - kvp.Value.Timestamp;
                if (age > _requestValidityWindow)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }

            foreach (var key in expiredKeys)
            {
                _requestCache.TryRemove(key, out _);
            }

            if (expiredKeys.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Cleaned up {expiredKeys.Count} expired request signatures");
            }
        }

        /// <summary>
        /// Gets replay attack statistics
        /// </summary>
        public ReplayStatistics GetStatistics()
        {
            var stats = new ReplayStatistics
            {
                CachedRequestCount = _requestCache.Count,
                ValidityWindowMinutes = (int)_requestValidityWindow.TotalMinutes
            };

            var now = DateTime.Now;
            foreach (var request in _requestCache.Values)
            {
                stats.TotalRequests++;
                
                var age = now - request.Timestamp;
                if (age.TotalMinutes < 1)
                {
                    stats.RecentRequests++;
                }
            }

            return stats;
        }

        /// <summary>
        /// Validates that a timestamp is within the acceptable window
        /// </summary>
        public bool IsTimestampValid(DateTime timestamp)
        {
            var now = DateTime.Now;
            var difference = Math.Abs((now - timestamp).TotalMinutes);
            return difference <= _requestValidityWindow.TotalMinutes;
        }

        /// <summary>
        /// Checks if a nonce has been used before
        /// </summary>
        public bool IsNonceUsed(string nonce, string remoteAddress)
        {
            // Simple nonce check - in production, this should be more sophisticated
            var nonceKey = $"nonce_{remoteAddress}_{nonce}";
            return _requestCache.ContainsKey(nonceKey);
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _requestCache?.Clear();
        }
    }

    /// <summary>
    /// Request signature information
    /// </summary>
    public class RequestSignature
    {
        public string Signature { get; set; }
        public DateTime Timestamp { get; set; }
        public string RemoteAddress { get; set; }
        public string Method { get; set; }
        public string Path { get; set; }
    }

    /// <summary>
    /// Replay validation result
    /// </summary>
    public class ReplayValidationResult
    {
        public bool IsValid { get; set; }
        public string Reason { get; set; }
        public string Warning { get; set; }
        public string RemoteAddress { get; set; }
        public DateTime Timestamp { get; set; }
        public DateTime? OriginalRequestTime { get; set; }
        public string RequestSignature { get; set; }
    }

    /// <summary>
    /// Replay attack statistics
    /// </summary>
    public class ReplayStatistics
    {
        public int CachedRequestCount { get; set; }
        public int TotalRequests { get; set; }
        public int RecentRequests { get; set; }
        public int ValidityWindowMinutes { get; set; }
        public int DetectedReplays { get; set; }
        public DateTime LastReplayDetected { get; set; }
    }
}
