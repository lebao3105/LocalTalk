using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Shared.Protocol
{
    /// <summary>
    /// Comprehensive error handling with categorized error types and intelligent retry strategies
    /// </summary>
    public class ErrorHandlingManager : IDisposable
    {
        private static ErrorHandlingManager _instance;
        private readonly ConcurrentDictionary<string, ErrorContext> _errorContexts;
        private readonly ErrorHandlingConfiguration _config;
        private readonly Timer _cleanupTimer;
        private bool _disposed;

        public static ErrorHandlingManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ErrorHandlingManager();
                }
                return _instance;
            }
        }

        public event EventHandler<ErrorOccurredEventArgs> ErrorOccurred;
        public event EventHandler<RetryAttemptEventArgs> RetryAttempt;
        public event EventHandler<ErrorResolvedEventArgs> ErrorResolved;

        private ErrorHandlingManager()
        {
            _errorContexts = new ConcurrentDictionary<string, ErrorContext>();
            _config = new ErrorHandlingConfiguration();
            
            // Cleanup expired error contexts every 5 minutes
            _cleanupTimer = new Timer(CleanupExpiredContexts, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// Handles an error with automatic categorization and retry logic
        /// </summary>
        public async Task<ErrorHandlingResult> HandleErrorAsync(string operationId, Exception exception, ErrorContext context = null)
        {
            try
            {
                // Get or create error context
                var errorContext = context ?? _errorContexts.GetOrAdd(operationId, _ => new ErrorContext
                {
                    OperationId = operationId,
                    FirstOccurrence = DateTime.Now,
                    ErrorHistory = new List<ErrorOccurrence>()
                });

                // Categorize the error
                var errorCategory = CategorizeError(exception);
                var errorSeverity = DetermineErrorSeverity(exception, errorCategory);
                
                // Record error occurrence
                var occurrence = new ErrorOccurrence
                {
                    Exception = exception,
                    Category = errorCategory,
                    Severity = errorSeverity,
                    Timestamp = DateTime.Now,
                    AttemptNumber = errorContext.AttemptCount + 1
                };
                
                errorContext.ErrorHistory.Add(occurrence);
                errorContext.AttemptCount++;
                errorContext.LastOccurrence = DateTime.Now;

                // Fire error occurred event
                OnErrorOccurred(new ErrorOccurredEventArgs
                {
                    OperationId = operationId,
                    Exception = exception,
                    Category = errorCategory,
                    Severity = errorSeverity,
                    AttemptNumber = occurrence.AttemptNumber
                });

                // Determine if retry is appropriate
                var retryDecision = ShouldRetry(errorContext, errorCategory, errorSeverity);
                
                if (retryDecision.ShouldRetry)
                {
                    // Calculate retry delay
                    var retryDelay = CalculateRetryDelay(errorContext, errorCategory);
                    
                    OnRetryAttempt(new RetryAttemptEventArgs
                    {
                        OperationId = operationId,
                        AttemptNumber = occurrence.AttemptNumber,
                        RetryDelay = retryDelay,
                        Reason = retryDecision.Reason
                    });

                    return new ErrorHandlingResult
                    {
                        ShouldRetry = true,
                        RetryDelay = retryDelay,
                        ErrorCategory = errorCategory,
                        ErrorSeverity = errorSeverity,
                        RecommendedAction = GetRecommendedAction(errorCategory, errorSeverity),
                        Context = errorContext
                    };
                }
                else
                {
                    // Mark as permanently failed
                    errorContext.IsPermanentFailure = true;
                    
                    return new ErrorHandlingResult
                    {
                        ShouldRetry = false,
                        ErrorCategory = errorCategory,
                        ErrorSeverity = errorSeverity,
                        RecommendedAction = GetRecommendedAction(errorCategory, errorSeverity),
                        FailureReason = retryDecision.Reason,
                        Context = errorContext
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in error handling: {ex.Message}");
                
                // Fallback error handling
                return new ErrorHandlingResult
                {
                    ShouldRetry = false,
                    ErrorCategory = ErrorCategory.Unknown,
                    ErrorSeverity = ErrorSeverity.Critical,
                    RecommendedAction = RecommendedAction.Abort,
                    FailureReason = "Error handling system failure"
                };
            }
        }

        /// <summary>
        /// Marks an operation as successfully resolved
        /// </summary>
        public void MarkAsResolved(string operationId)
        {
            if (_errorContexts.TryRemove(operationId, out var context))
            {
                OnErrorResolved(new ErrorResolvedEventArgs
                {
                    OperationId = operationId,
                    TotalAttempts = context.AttemptCount,
                    Duration = DateTime.Now - context.FirstOccurrence
                });
            }
        }

        /// <summary>
        /// Categorizes an error based on its type and characteristics
        /// </summary>
        private ErrorCategory CategorizeError(Exception exception)
        {
            return exception switch
            {
                // Network-related errors
                HttpRequestException _ => ErrorCategory.Network,
                WebException webEx when webEx.Status == WebExceptionStatus.Timeout => ErrorCategory.Timeout,
                WebException webEx when webEx.Status == WebExceptionStatus.ConnectFailure => ErrorCategory.Network,
                WebException webEx when webEx.Status == WebExceptionStatus.NameResolutionFailure => ErrorCategory.Network,
                TaskCanceledException _ => ErrorCategory.Timeout,
                TimeoutException _ => ErrorCategory.Timeout,
                
                // Authentication/Authorization errors
                UnauthorizedAccessException _ => ErrorCategory.Authentication,
                
                // File system errors
                DirectoryNotFoundException _ => ErrorCategory.FileSystem,
                FileNotFoundException _ => ErrorCategory.FileSystem,
                IOException ioEx when ioEx.Message.Contains("disk") => ErrorCategory.Storage,
                IOException _ => ErrorCategory.FileSystem,
                UnauthorizedAccessException _ when exception.Message.Contains("file") => ErrorCategory.FileSystem,
                
                // Security errors
                System.Security.SecurityException _ => ErrorCategory.Security,
                System.Security.Cryptography.CryptographicException _ => ErrorCategory.Security,
                
                // Resource errors
                OutOfMemoryException _ => ErrorCategory.Resource,
                InsufficientMemoryException _ => ErrorCategory.Resource,
                
                // Configuration errors
                ArgumentException _ => ErrorCategory.Configuration,
                InvalidOperationException _ => ErrorCategory.Configuration,
                NotSupportedException _ => ErrorCategory.Configuration,
                
                // Protocol errors
                FormatException _ => ErrorCategory.Protocol,
                InvalidDataException _ => ErrorCategory.Protocol,
                
                // Default
                _ => ErrorCategory.Unknown
            };
        }

        /// <summary>
        /// Determines error severity based on exception type and category
        /// </summary>
        private ErrorSeverity DetermineErrorSeverity(Exception exception, ErrorCategory category)
        {
            // Critical errors that should stop operation immediately
            if (exception is OutOfMemoryException || 
                exception is System.Security.SecurityException ||
                exception is UnauthorizedAccessException)
            {
                return ErrorSeverity.Critical;
            }

            // High severity errors that may be recoverable with significant intervention
            if (category == ErrorCategory.Security || 
                category == ErrorCategory.Authentication ||
                category == ErrorCategory.Resource)
            {
                return ErrorSeverity.High;
            }

            // Medium severity errors that are often temporary
            if (category == ErrorCategory.Network || 
                category == ErrorCategory.Timeout ||
                category == ErrorCategory.Storage)
            {
                return ErrorSeverity.Medium;
            }

            // Low severity errors that are usually recoverable
            if (category == ErrorCategory.FileSystem || 
                category == ErrorCategory.Protocol)
            {
                return ErrorSeverity.Low;
            }

            return ErrorSeverity.Medium; // Default
        }

        /// <summary>
        /// Determines if an operation should be retried
        /// </summary>
        private RetryDecision ShouldRetry(ErrorContext context, ErrorCategory category, ErrorSeverity severity)
        {
            // Never retry critical errors
            if (severity == ErrorSeverity.Critical)
            {
                return new RetryDecision { ShouldRetry = false, Reason = "Critical error - no retry" };
            }

            // Check maximum attempts
            if (context.AttemptCount >= _config.MaxRetryAttempts)
            {
                return new RetryDecision { ShouldRetry = false, Reason = "Maximum retry attempts exceeded" };
            }

            // Check if operation has been running too long
            var totalDuration = DateTime.Now - context.FirstOccurrence;
            if (totalDuration > _config.MaxOperationDuration)
            {
                return new RetryDecision { ShouldRetry = false, Reason = "Maximum operation duration exceeded" };
            }

            // Category-specific retry logic
            return category switch
            {
                ErrorCategory.Network => new RetryDecision { ShouldRetry = true, Reason = "Network error - retry with backoff" },
                ErrorCategory.Timeout => new RetryDecision { ShouldRetry = true, Reason = "Timeout error - retry with increased timeout" },
                ErrorCategory.Storage => new RetryDecision { ShouldRetry = context.AttemptCount < 3, Reason = "Storage error - limited retries" },
                ErrorCategory.FileSystem => new RetryDecision { ShouldRetry = context.AttemptCount < 2, Reason = "File system error - few retries" },
                ErrorCategory.Protocol => new RetryDecision { ShouldRetry = context.AttemptCount < 2, Reason = "Protocol error - few retries" },
                ErrorCategory.Authentication => new RetryDecision { ShouldRetry = false, Reason = "Authentication error - no retry" },
                ErrorCategory.Security => new RetryDecision { ShouldRetry = false, Reason = "Security error - no retry" },
                ErrorCategory.Configuration => new RetryDecision { ShouldRetry = false, Reason = "Configuration error - no retry" },
                ErrorCategory.Resource => new RetryDecision { ShouldRetry = context.AttemptCount < 2, Reason = "Resource error - few retries" },
                _ => new RetryDecision { ShouldRetry = context.AttemptCount < 3, Reason = "Unknown error - limited retries" }
            };
        }

        /// <summary>
        /// Calculates retry delay using exponential backoff with jitter
        /// </summary>
        private TimeSpan CalculateRetryDelay(ErrorContext context, ErrorCategory category)
        {
            var baseDelay = category switch
            {
                ErrorCategory.Network => TimeSpan.FromSeconds(1),
                ErrorCategory.Timeout => TimeSpan.FromSeconds(2),
                ErrorCategory.Storage => TimeSpan.FromSeconds(0.5),
                ErrorCategory.FileSystem => TimeSpan.FromSeconds(0.2),
                ErrorCategory.Protocol => TimeSpan.FromSeconds(0.1),
                _ => TimeSpan.FromSeconds(1)
            };

            // Exponential backoff: delay = baseDelay * (2 ^ attemptCount)
            var exponentialDelay = TimeSpan.FromMilliseconds(
                baseDelay.TotalMilliseconds * Math.Pow(2, context.AttemptCount - 1));

            // Add jitter to prevent thundering herd
            var jitter = TimeSpan.FromMilliseconds(new Random().Next(0, (int)(exponentialDelay.TotalMilliseconds * 0.1)));
            
            var totalDelay = exponentialDelay + jitter;
            
            // Cap the maximum delay
            return totalDelay > _config.MaxRetryDelay ? _config.MaxRetryDelay : totalDelay;
        }

        /// <summary>
        /// Gets recommended action for error category and severity
        /// </summary>
        private RecommendedAction GetRecommendedAction(ErrorCategory category, ErrorSeverity severity)
        {
            if (severity == ErrorSeverity.Critical)
                return RecommendedAction.Abort;

            return category switch
            {
                ErrorCategory.Network => RecommendedAction.RetryWithBackoff,
                ErrorCategory.Timeout => RecommendedAction.RetryWithIncreasedTimeout,
                ErrorCategory.Storage => RecommendedAction.CheckDiskSpace,
                ErrorCategory.FileSystem => RecommendedAction.CheckPermissions,
                ErrorCategory.Authentication => RecommendedAction.ReAuthenticate,
                ErrorCategory.Security => RecommendedAction.Abort,
                ErrorCategory.Configuration => RecommendedAction.CheckConfiguration,
                ErrorCategory.Protocol => RecommendedAction.RetryWithDifferentParameters,
                ErrorCategory.Resource => RecommendedAction.FreeResources,
                _ => RecommendedAction.RetryWithBackoff
            };
        }

        /// <summary>
        /// Cleans up expired error contexts
        /// </summary>
        private void CleanupExpiredContexts(object state)
        {
            var cutoffTime = DateTime.Now - _config.ContextRetentionPeriod;
            var expiredKeys = _errorContexts
                .Where(kvp => kvp.Value.LastOccurrence < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _errorContexts.TryRemove(key, out _);
            }
        }

        private void OnErrorOccurred(ErrorOccurredEventArgs args)
        {
            ErrorOccurred?.Invoke(this, args);
        }

        private void OnRetryAttempt(RetryAttemptEventArgs args)
        {
            RetryAttempt?.Invoke(this, args);
        }

        private void OnErrorResolved(ErrorResolvedEventArgs args)
        {
            ErrorResolved?.Invoke(this, args);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cleanupTimer?.Dispose();
                _errorContexts.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Error handling configuration
    /// </summary>
    public class ErrorHandlingConfiguration
    {
        public int MaxRetryAttempts { get; set; } = 5;
        public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan MaxOperationDuration { get; set; } = TimeSpan.FromHours(1);
        public TimeSpan ContextRetentionPeriod { get; set; } = TimeSpan.FromHours(24);
    }

    /// <summary>
    /// Error context for tracking retry attempts
    /// </summary>
    public class ErrorContext
    {
        public string OperationId { get; set; }
        public DateTime FirstOccurrence { get; set; }
        public DateTime LastOccurrence { get; set; }
        public int AttemptCount { get; set; }
        public bool IsPermanentFailure { get; set; }
        public List<ErrorOccurrence> ErrorHistory { get; set; } = new List<ErrorOccurrence>();
    }

    /// <summary>
    /// Individual error occurrence
    /// </summary>
    public class ErrorOccurrence
    {
        public Exception Exception { get; set; }
        public ErrorCategory Category { get; set; }
        public ErrorSeverity Severity { get; set; }
        public DateTime Timestamp { get; set; }
        public int AttemptNumber { get; set; }
    }

    /// <summary>
    /// Error handling result
    /// </summary>
    public class ErrorHandlingResult
    {
        public bool ShouldRetry { get; set; }
        public TimeSpan RetryDelay { get; set; }
        public ErrorCategory ErrorCategory { get; set; }
        public ErrorSeverity ErrorSeverity { get; set; }
        public RecommendedAction RecommendedAction { get; set; }
        public string FailureReason { get; set; }
        public ErrorContext Context { get; set; }
    }

    /// <summary>
    /// Retry decision
    /// </summary>
    internal class RetryDecision
    {
        public bool ShouldRetry { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Error category enumeration
    /// </summary>
    public enum ErrorCategory
    {
        Unknown,
        Network,
        Timeout,
        Authentication,
        FileSystem,
        Storage,
        Security,
        Resource,
        Configuration,
        Protocol
    }

    /// <summary>
    /// Error severity enumeration
    /// </summary>
    public enum ErrorSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>
    /// Recommended action enumeration
    /// </summary>
    public enum RecommendedAction
    {
        RetryWithBackoff,
        RetryWithIncreasedTimeout,
        RetryWithDifferentParameters,
        CheckDiskSpace,
        CheckPermissions,
        ReAuthenticate,
        CheckConfiguration,
        FreeResources,
        Abort
    }

    /// <summary>
    /// Error occurred event arguments
    /// </summary>
    public class ErrorOccurredEventArgs : EventArgs
    {
        public string OperationId { get; set; }
        public Exception Exception { get; set; }
        public ErrorCategory Category { get; set; }
        public ErrorSeverity Severity { get; set; }
        public int AttemptNumber { get; set; }
    }

    /// <summary>
    /// Retry attempt event arguments
    /// </summary>
    public class RetryAttemptEventArgs : EventArgs
    {
        public string OperationId { get; set; }
        public int AttemptNumber { get; set; }
        public TimeSpan RetryDelay { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Error resolved event arguments
    /// </summary>
    public class ErrorResolvedEventArgs : EventArgs
    {
        public string OperationId { get; set; }
        public int TotalAttempts { get; set; }
        public TimeSpan Duration { get; set; }
    }
}
