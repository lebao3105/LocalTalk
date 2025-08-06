using Shared.Platform;
using Shared.Security;
using Shared.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using System.Linq;

namespace Shared.Http
{
    /// <summary>
    /// HTTP server implementation for LocalSend protocol
    /// </summary>
    public class LocalSendHttpServer : IDisposable
    {
        #region HTTP Status Code Constants
        /// <summary>
        /// HTTP 200 OK status code.
        /// </summary>
        private const int StatusOk = 200;

        /// <summary>
        /// HTTP 400 Bad Request status code.
        /// </summary>
        private const int StatusBadRequest = 400;

        /// <summary>
        /// HTTP 403 Forbidden status code.
        /// </summary>
        private const int StatusForbidden = 403;

        /// <summary>
        /// HTTP 404 Not Found status code.
        /// </summary>
        private const int StatusNotFound = 404;

        /// <summary>
        /// HTTP 409 Conflict status code.
        /// </summary>
        private const int StatusConflict = 409;

        /// <summary>
        /// HTTP 405 Method Not Allowed status code.
        /// </summary>
        private const int StatusMethodNotAllowed = 405;

        /// <summary>
        /// HTTP 413 Request Entity Too Large status code.
        /// </summary>
        private const int StatusRequestEntityTooLarge = 413;

        /// <summary>
        /// HTTP 500 Internal Server Error status code.
        /// </summary>
        private const int StatusInternalServerError = 500;
        #endregion

        #region Security Constants
        /// <summary>
        /// Maximum allowed request body size (100MB).
        /// </summary>
        private const int MaxRequestBodySize = 100 * 1024 * 1024;

        /// <summary>
        /// Maximum allowed header value length (8KB).
        /// </summary>
        private const int MaxHeaderValueLength = 8192;

        /// <summary>
        /// Maximum allowed number of headers per request.
        /// </summary>
        private const int MaxHeaderCount = 100;
        #endregion

        private readonly IHttpServer _httpServer;
        private readonly Dictionary<string, IRouteHandler> _routes;
        private readonly CertificateManager _certificateManager;
        private readonly SecurityAnalyzer _securityAnalyzer;
        private readonly ReplayAttackDetector _replayDetector;
        private bool _isRunning;
        private bool _disposed;

        public bool IsRunning => _isRunning;
        public int Port { get; private set; }
        public bool UseHttps { get; private set; }

        public event EventHandler<ServerErrorEventArgs> ErrorOccurred;

        public LocalSendHttpServer()
        {
            var platform = PlatformFactory.Current;
            _httpServer = platform.CreateHttpServer();
            _routes = new Dictionary<string, IRouteHandler>();
            _certificateManager = CertificateManager.Instance;
            _securityAnalyzer = SecurityAnalyzer.Instance;
            _replayDetector = ReplayAttackDetector.Instance;

            // Validate that the security analyzer is properly initialized
            try
            {
                if (_securityAnalyzer == null)
                {
                    throw new InvalidOperationException("Security analyzer is null");
                }

                // Test the security analyzer with a safe test request
                var testResult = _securityAnalyzer.AnalyzeRequest("127.0.0.1", "/test", new System.Collections.Generic.Dictionary<string, string>(), null);
                if (testResult == null)
                {
                    throw new InvalidOperationException("Security analyzer returned null result");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Security analyzer validation failed: {ex.Message}");
                // Don't throw here to prevent initialization failures, but log the issue
            }

            RegisterRoutes();
            _httpServer.RequestReceived += OnRequestReceived;
        }

        /// <summary>
        /// Starts the HTTP server
        /// </summary>
        /// <param name="port">The port number to listen on</param>
        /// <param name="useHttps">Whether to use HTTPS (if supported by platform)</param>
        /// <exception cref="InvalidOperationException">Thrown when server is already running</exception>
        /// <exception cref="NotSupportedException">Thrown when HTTP server is not supported on this platform</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when port is not valid</exception>
        public async Task StartAsync(int port, bool useHttps = true)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("Server is already running");
            }

            if (port <= 0 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port),
                    $"Port {port} is not valid. Port must be between 1 and 65535");
            }

            if (!PlatformFactory.Features.SupportsHttpServer)
            {
                throw new NotSupportedException("HTTP server not supported on this platform");
            }

            if (_httpServer == null)
            {
                throw new InvalidOperationException("HTTP server is not initialized. Ensure the constructor completed successfully.");
            }

            Port = port;
            UseHttps = useHttps && PlatformFactory.Features.SupportsCertificateGeneration;

            try
            {
                await _httpServer.StartAsync(port, UseHttps);
                _isRunning = true;

                System.Diagnostics.Debug.WriteLine($"LocalSend HTTP server started on port {port} (HTTPS: {UseHttps})");
            }
            catch (Exception ex)
            {
                // Ensure we're not left in a partially started state
                _isRunning = false;
                Port = 0;
                UseHttps = false;

                OnError($"Failed to start HTTP server on port {port}: {ex.Message}", ex);

                // Wrap common exceptions with more specific information
                if (ex is System.Net.HttpListenerException httpEx)
                {
                    throw new InvalidOperationException(
                        $"Failed to start HTTP server on port {port}. Port may be in use or access denied. Error code: {httpEx.ErrorCode}",
                        ex);
                }
                else if (ex is UnauthorizedAccessException)
                {
                    throw new InvalidOperationException(
                        $"Access denied when starting HTTP server on port {port}. Administrator privileges may be required.",
                        ex);
                }
                else
                {
                    throw new InvalidOperationException($"Failed to start HTTP server on port {port}: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// Stops the HTTP server
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning)
            {
                return;
            }

            try
            {
                await _httpServer.StopAsync();
                _isRunning = false;

                System.Diagnostics.Debug.WriteLine("LocalSend HTTP server stopped");
            }
            catch (Exception ex)
            {
                OnError($"Error stopping HTTP server: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Registers all LocalSend protocol routes
        /// </summary>
        private void RegisterRoutes()
        {
            // Device registration and discovery
            _routes["/api/localsend/v2/register"] = new RegisterRouteHandler();
            _routes["/api/localsend/v2/info"] = new InfoRouteHandler();

            // File upload API
            _routes["/api/localsend/v2/prepare-upload"] = new PrepareUploadRouteHandler();
            _routes["/api/localsend/v2/upload"] = new UploadRouteHandler();
            _routes["/api/localsend/v2/cancel"] = new CancelRouteHandler();

            // File download API (reverse transfer)
            _routes["/api/localsend/v2/prepare-download"] = new PrepareDownloadRouteHandler();
            _routes["/api/localsend/v2/download"] = new DownloadRouteHandler();

            // Health check
            _routes["/health"] = new HealthCheckRouteHandler();
        }

        /// <summary>
        /// Handles incoming HTTP requests
        /// </summary>
        private void OnRequestReceived(object sender, HttpRequestEventArgs e)
        {
            // Use fire-and-forget pattern to avoid async void with proper exception handling
            _ = HandleRequestSafelyAsync(e);
        }

        /// <summary>
        /// Safely handles HTTP requests with comprehensive exception handling
        /// </summary>
        private async Task HandleRequestSafelyAsync(HttpRequestEventArgs e)
        {
            try
            {
                await HandleRequestAsync(e);
            }
            catch (Exception ex)
            {
                // Log the exception and ensure it doesn't crash the application
                OnError($"Unhandled exception in HTTP request handler: {ex.Message}", ex);

                // Try to send an error response if possible
                try
                {
                    if (e?.Response != null)
                    {
                        await SendErrorResponse(e.Response, 500, "Internal Server Error");
                    }
                }
                catch
                {
                    // Ignore errors when sending error response to prevent infinite loops
                }
            }
        }

        /// <summary>
        /// Handles incoming HTTP requests asynchronously
        /// </summary>
        private async Task HandleRequestAsync(HttpRequestEventArgs e)
        {
            try
            {
                if (e?.Request == null || e?.Response == null)
                {
                    System.Diagnostics.Debug.WriteLine("Received null request or response");
                    return;
                }

                var request = e.Request;
                var response = e.Response;

                // Enhanced security validation for request properties
                if (string.IsNullOrEmpty(request.Method) || string.IsNullOrEmpty(request.Path))
                {
                    System.Diagnostics.Debug.WriteLine("Received request with null or empty Method/Path");
                    await SendErrorResponse(response, 400, "Invalid request format");
                    return;
                }

                // Validate HTTP method against allowed methods
                var allowedMethods = new[] { "GET", "POST", "PUT", "DELETE", "OPTIONS", "HEAD" };
                if (!allowedMethods.Contains(request.Method.ToUpperInvariant()))
                {
                    System.Diagnostics.Debug.WriteLine($"Received request with invalid HTTP method: {request.Method}");
                    await SendErrorResponse(response, 405, "Method not allowed");
                    return;
                }

                // Validate and sanitize request path
                if (!IsValidRequestPath(request.Path))
                {
                    System.Diagnostics.Debug.WriteLine($"Received request with invalid path: {request.Path}");
                    await SendErrorResponse(response, 400, "Invalid request path");
                    return;
                }

                // Validate remote address format
                if (string.IsNullOrEmpty(request.RemoteAddress))
                {
                    System.Diagnostics.Debug.WriteLine("Received request with null or empty RemoteAddress");
                    request.RemoteAddress = "unknown"; // Set default to prevent null reference
                }
                else if (!IsValidIPAddress(request.RemoteAddress))
                {
                    System.Diagnostics.Debug.WriteLine($"Received request with invalid IP address: {request.RemoteAddress}");
                    // Don't reject, but log for security monitoring
                }

                // Ensure headers collection is not null and validate headers
                if (request.Headers == null)
                {
                    System.Diagnostics.Debug.WriteLine("Request headers collection is null");
                    await SendErrorResponse(response, 400, "Invalid request headers");
                    return;
                }

                // Validate request headers for security threats
                if (!ValidateRequestHeaders(request.Headers))
                {
                    System.Diagnostics.Debug.WriteLine("Request contains invalid or malicious headers");
                    await SendErrorResponse(response, 400, "Invalid request headers");
                    return;
                }

                // Validate request body size
                if (request.Body != null && request.Body.Length > MaxRequestBodySize)
                {
                    System.Diagnostics.Debug.WriteLine($"Request body too large: {request.Body.Length} bytes");
                    await SendErrorResponse(response, 413, "Request entity too large");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"HTTP {request.Method} {request.Path} from {request.RemoteAddress}");

                // Security analysis
                SecurityAnalysisResult securityResult = null;
                try
                {
                    securityResult = _securityAnalyzer?.AnalyzeRequest(
                        request.RemoteAddress, request.Path, request.Headers, request.Body);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Security analysis failed: {ex.Message}");
                    await SendErrorResponse(response, StatusInternalServerError, "Security analysis failed");
                    return;
                }

                if (securityResult?.ShouldBlock == true)
                {
                    System.Diagnostics.Debug.WriteLine($"Blocking request due to security threat: {securityResult.ThreatLevel}");
                    await SendErrorResponse(response, StatusForbidden, "Request blocked for security reasons");
                    return;
                }

                // Replay attack detection
                ReplayValidationResult replayResult = null;
                try
                {
                    replayResult = _replayDetector?.ValidateRequest(
                        request.Method, request.Path, request.Headers, request.Body, request.RemoteAddress);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Replay detection failed: {ex.Message}");
                    await SendErrorResponse(response, StatusInternalServerError, "Replay detection failed");
                    return;
                }

                if (replayResult?.IsValid == false)
                {
                    System.Diagnostics.Debug.WriteLine($"Blocking request due to replay attack: {replayResult.Reason}");
                    await SendErrorResponse(response, StatusConflict, "Request rejected: " + replayResult.Reason);
                    return;
                }

                // Find matching route
                IRouteHandler routeHandler = null;
                try
                {
                    routeHandler = FindRouteHandler(request.Path);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Route resolution failed: {ex.Message}");
                    await SendErrorResponse(response, StatusInternalServerError, "Route resolution failed");
                    return;
                }

                if (routeHandler == null)
                {
                    await SendNotFoundResponse(response);
                    return;
                }

                // Create request context
                var context = new HttpRequestContext
                {
                    Request = request,
                    Response = response,
                    Server = this,
                    SecurityResult = securityResult,
                    ReplayResult = replayResult
                };

                // Handle the request
                await routeHandler.HandleAsync(context);
            }
            catch (Exception ex)
            {
                OnError($"Error handling HTTP request: {ex.Message}", ex);

                try
                {
                    await SendErrorResponse(e.Response, StatusInternalServerError, "Internal Server Error");
                }
                catch
                {
                    // Ignore errors when sending error response
                }
            }
        }

        /// <summary>
        /// Finds the appropriate route handler for a path
        /// </summary>
        private IRouteHandler FindRouteHandler(string path)
        {
            // Exact match first
            if (_routes.TryGetValue(path, out var handler))
            {
                return handler;
            }

            // Check for parameterized routes
            foreach (var route in _routes.Keys)
            {
                if (IsRouteMatch(route, path))
                {
                    return _routes[route];
                }
            }

            return null;
        }

        /// <summary>
        /// Checks if a route pattern matches a path
        /// </summary>
        private bool IsRouteMatch(string routePattern, string path)
        {
            // Simple pattern matching - could be enhanced with regex
            var routeParts = routePattern.Split('/');
            var pathParts = path.Split('/');

            if (routeParts.Length != pathParts.Length)
            {
                return false;
            }

            for (int i = 0; i < routeParts.Length; i++)
            {
                if (routeParts[i].StartsWith("{") && routeParts[i].EndsWith("}"))
                {
                    // Parameter placeholder - matches any value
                    continue;
                }

                if (!string.Equals(routeParts[i], pathParts[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Sends a 404 Not Found response
        /// </summary>
        private async Task SendNotFoundResponse(IHttpResponse response)
        {
            response.StatusCode = StatusNotFound;
            response.Headers["Content-Type"] = "application/json";

            var errorResponse = new { error = "Not Found", message = "The requested endpoint was not found" };
            var json = Internet.SerializeObject(errorResponse);

            await response.WriteAsync(json);
            await response.CompleteAsync();
        }

        /// <summary>
        /// Sends an error response
        /// </summary>
        private async Task SendErrorResponse(IHttpResponse response, int statusCode, string message)
        {
            response.StatusCode = statusCode;
            response.Headers["Content-Type"] = "application/json";

            var errorResponse = new { error = message, statusCode = statusCode };
            var json = Internet.SerializeObject(errorResponse);

            await response.WriteAsync(json);
            await response.CompleteAsync();
        }

        /// <summary>
        /// Raises the ErrorOccurred event
        /// </summary>
        private void OnError(string message, Exception exception = null)
        {
            System.Diagnostics.Debug.WriteLine($"HTTP Server Error: {message}");
            if (exception != null)
            {
                System.Diagnostics.Debug.WriteLine($"Exception: {exception}");
            }

            ErrorOccurred?.Invoke(this, new ServerErrorEventArgs
            {
                Message = message,
                Exception = exception,
                Timestamp = DateTime.Now
            });
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                if (_isRunning)
                {
                    try
                    {
                        // Use ConfigureAwait(false) to prevent deadlocks
                        StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        OnError($"Error during HTTP server disposal: {ex.Message}", ex);
                    }
                }

                _httpServer?.Dispose();
            }
        }

        #region Security Validation Methods

        /// <summary>
        /// Validates if the request path is safe and properly formatted
        /// </summary>
        private bool IsValidRequestPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // Check path length
            if (path.Length > 2048) // RFC 2616 suggests 2048 as reasonable limit
                return false;

            // Must start with /
            if (!path.StartsWith("/"))
                return false;

            // Check for path traversal attempts
            if (path.Contains("..") || path.Contains("\\") || path.Contains("%2e%2e"))
                return false;

            // Check for null bytes and other dangerous characters
            if (path.Contains('\0') || path.Contains('\r') || path.Contains('\n'))
                return false;

            // Validate URL encoding
            try
            {
                var decoded = Uri.UnescapeDataString(path);
                if (decoded.Contains("..") || decoded.Contains('\0'))
                    return false;
            }
            catch
            {
                return false; // Invalid URL encoding
            }

            return true;
        }

        /// <summary>
        /// Validates if the IP address format is correct
        /// </summary>
        private bool IsValidIPAddress(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress))
                return false;

            // Try to parse as IPv4 or IPv6
            return System.Net.IPAddress.TryParse(ipAddress, out _);
        }

        /// <summary>
        /// Validates request headers for security threats
        /// </summary>
        private bool ValidateRequestHeaders(Dictionary<string, string> headers)
        {
            if (headers == null)
                return false;

            // Check header count limit
            if (headers.Count > MaxHeaderCount)
                return false;

            foreach (var header in headers)
            {
                // Validate header name
                if (string.IsNullOrEmpty(header.Key) || header.Key.Length > 256)
                    return false;

                // Check for invalid characters in header name
                if (header.Key.Any(c => c < 32 || c > 126 || c == ':'))
                    return false;

                // Validate header value
                if (header.Value != null)
                {
                    if (header.Value.Length > MaxHeaderValueLength)
                        return false;

                    // Check for CRLF injection
                    if (header.Value.Contains('\r') || header.Value.Contains('\n'))
                        return false;

                    // Check for null bytes
                    if (header.Value.Contains('\0'))
                        return false;
                }
            }

            return true;
        }

        #endregion
    }

    /// <summary>
    /// HTTP request context for route handlers
    /// </summary>
    public class HttpRequestContext
    {
        public IHttpRequest Request { get; set; }
        public IHttpResponse Response { get; set; }
        public LocalSendHttpServer Server { get; set; }
        public Dictionary<string, string> RouteParameters { get; set; } = new Dictionary<string, string>();
        public SecurityAnalysisResult SecurityResult { get; set; }
        public ReplayValidationResult ReplayResult { get; set; }
    }

    /// <summary>
    /// Interface for HTTP route handlers
    /// </summary>
    public interface IRouteHandler
    {
        Task HandleAsync(HttpRequestContext context);
    }

    /// <summary>
    /// Server error event arguments
    /// </summary>
    public class ServerErrorEventArgs : EventArgs
    {
        public string Message { get; set; }
        public Exception Exception { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
