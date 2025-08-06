using Shared.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using System.Linq;

namespace Shared.Http
{
    /// <summary>
    /// Base class for route handlers
    /// </summary>
    public abstract class BaseRouteHandler : IRouteHandler
    {
        /// <summary>
        /// Handles an HTTP request asynchronously
        /// </summary>
        /// <param name="context">The HTTP request context</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public abstract Task HandleAsync(HttpRequestContext context);

        /// <summary>
        /// Sends a JSON response with the specified data and status code
        /// </summary>
        /// <param name="response">The HTTP response object</param>
        /// <param name="data">The data to serialize as JSON</param>
        /// <param name="statusCode">The HTTP status code (default: 200)</param>
        /// <returns>A task representing the asynchronous operation</returns>
        protected async Task SendJsonResponse(IHttpResponse response, object data, int statusCode = 200)
        {
            response.StatusCode = statusCode;
            response.Headers["Content-Type"] = "application/json";
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";

            var json = Internet.SerializeObject(data);
            await response.WriteAsync(json);
            await response.CompleteAsync();
        }

        /// <summary>
        /// Sends an error response with the specified status code and message
        /// </summary>
        /// <param name="response">The HTTP response object</param>
        /// <param name="statusCode">The HTTP error status code</param>
        /// <param name="message">The error message</param>
        /// <returns>A task representing the asynchronous operation</returns>
        protected async Task SendErrorResponse(IHttpResponse response, int statusCode, string message)
        {
            var errorData = new { error = message, statusCode = statusCode };
            await SendJsonResponse(response, errorData, statusCode);
        }

        /// <summary>
        /// Deserializes the request body from JSON to the specified type
        /// </summary>
        /// <typeparam name="T">The type to deserialize to</typeparam>
        /// <param name="body">The request body as byte array</param>
        /// <returns>The deserialized object or default value if body is empty</returns>
        protected T DeserializeRequestBody<T>(byte[] body)
        {
            if (body == null || body.Length == 0)
            {
                return default(T);
            }

            var json = Encoding.UTF8.GetString(body);
            return Internet.DeserializeObject<T>(json);
        }

        /// <summary>
        /// Parses query parameters from a URL path
        /// </summary>
        /// <param name="path">The URL path containing query parameters</param>
        /// <returns>A dictionary of parameter names and values</returns>
        /// <exception cref="ArgumentNullException">Thrown when path is null</exception>
        protected Dictionary<string, string> ParseQueryParameters(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            var parameters = new Dictionary<string, string>();

            var queryIndex = path.IndexOf('?');
            if (queryIndex == -1)
            {
                return parameters;
            }

            var queryString = path.Substring(queryIndex + 1);
            if (string.IsNullOrEmpty(queryString))
                return parameters;

            var pairs = queryString.Split('&');

            foreach (var pair in pairs)
            {
                if (string.IsNullOrEmpty(pair))
                    continue;

                var parts = pair.Split('=');
                if (parts.Length == 2)
                {
                    try
                    {
                        var key = Uri.UnescapeDataString(parts[0]);
                        var value = Uri.UnescapeDataString(parts[1]);

                        if (!string.IsNullOrEmpty(key))
                        {
                            parameters[key] = value ?? string.Empty;
                        }
                    }
                    catch (UriFormatException)
                    {
                        // Skip malformed URL-encoded parameters
                        continue;
                    }
                }
            }

            return parameters;
        }
    }

    /// <summary>
    /// Handles device registration requests
    /// </summary>
    public class RegisterRouteHandler : BaseRouteHandler
    {
        public override async Task HandleAsync(HttpRequestContext context)
        {
            if (context.Request.Method != "POST")
            {
                await SendErrorResponse(context.Response, 405, "Method Not Allowed");
                return;
            }

            try
            {
                var deviceInfo = DeserializeRequestBody<Device>(context.Request.Body);
                if (deviceInfo.Equals(default(Device)))
                {
                    await SendErrorResponse(context.Response, 400, "Invalid device information");
                    return;
                }

                // Add device to discovered devices list
                var existingDevice = LocalSendProtocol.Devices
                    .FirstOrDefault(d => d.fingerprint == deviceInfo.fingerprint);
                if (existingDevice.Equals(default(Device)))
                {
                    // Add new device
#if WINDOWS_UWP
                    await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                        Windows.UI.Core.CoreDispatcherPriority.High, () => LocalSendProtocol.Devices.Add(deviceInfo));
#elif WINDOWS_PHONE
                    System.Windows.Deployment.Current.Dispatcher.BeginInvoke(
                        () => LocalSendProtocol.Devices.Add(deviceInfo));
#endif
                }

                // Respond with this device's information
                await SendJsonResponse(context.Response, LocalSendProtocol.ThisDevice);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RegisterRouteHandler: {ex.Message}");
                await SendErrorResponse(context.Response, 500, "Internal Server Error");
            }
        }
    }

    /// <summary>
    /// Handles device info requests
    /// </summary>
    public class InfoRouteHandler : BaseRouteHandler
    {
        public override async Task HandleAsync(HttpRequestContext context)
        {
            if (context.Request.Method != "GET")
            {
                await SendErrorResponse(context.Response, 405, "Method Not Allowed");
                return;
            }

            await SendJsonResponse(context.Response, LocalSendProtocol.ThisDevice);
        }
    }

    /// <summary>
    /// Handles file upload preparation requests
    /// </summary>
    public class PrepareUploadRouteHandler : BaseRouteHandler
    {
        public override async Task HandleAsync(HttpRequestContext context)
        {
            if (context.Request.Method != "POST")
            {
                await SendErrorResponse(context.Response, 405, "Method Not Allowed");
                return;
            }

            try
            {
                var queryParams = ParseQueryParameters(context.Request.Path);
                var pin = queryParams.ContainsKey("pin") ? queryParams["pin"] : null;

                // TODO: Validate PIN if required
                if (!string.IsNullOrEmpty(Settings.GetSetting<string>("RequiredPin")))
                {
                    var requiredPin = Settings.GetSetting<string>("RequiredPin");
                    if (pin != requiredPin)
                    {
                        await SendErrorResponse(context.Response, 401, "Invalid PIN");
                        return;
                    }
                }

                var uploadRequest = DeserializeRequestBody<UploadRequest>(context.Request.Body);
                if (uploadRequest.Equals(default(UploadRequest)))
                {
                    await SendErrorResponse(context.Response, 400, "Invalid upload request");
                    return;
                }

                // Generate session ID and file tokens
                var sessionId = Guid.NewGuid().ToString();
                var fileTokens = new Dictionary<string, string>();

                foreach (var file in uploadRequest.files)
                {
                    fileTokens[file.Key] = Guid.NewGuid().ToString();
                }

                // Store session information
                SessionManager.Instance.CreateUploadSession(sessionId, uploadRequest, fileTokens);

                var response = new UploadResponse
                {
                    sessionId = sessionId,
                    files = fileTokens
                };

                await SendJsonResponse(context.Response, response);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in PrepareUploadRouteHandler: {ex.Message}");
                await SendErrorResponse(context.Response, 500, "Internal Server Error");
            }
        }
    }

    /// <summary>
    /// Handles file upload requests
    /// </summary>
    public class UploadRouteHandler : BaseRouteHandler
    {
        public override async Task HandleAsync(HttpRequestContext context)
        {
            if (context.Request.Method != "POST")
            {
                await SendErrorResponse(context.Response, 405, "Method Not Allowed");
                return;
            }

            try
            {
                var queryParams = ParseQueryParameters(context.Request.Path);

                if (!queryParams.ContainsKey("sessionId") ||
                    !queryParams.ContainsKey("fileId") ||
                    !queryParams.ContainsKey("token"))
                {
                    await SendErrorResponse(context.Response, 400, "Missing required parameters");
                    return;
                }

                var sessionId = queryParams["sessionId"];
                var fileId = queryParams["fileId"];
                var token = queryParams["token"];

                // Validate session and token
                var session = SessionManager.Instance.GetUploadSession(sessionId);
                if (session == null)
                {
                    await SendErrorResponse(context.Response, 403, "Invalid session");
                    return;
                }

                if (!session.FileTokens.ContainsKey(fileId) || session.FileTokens[fileId] != token)
                {
                    await SendErrorResponse(context.Response, 403, "Invalid token");
                    return;
                }

                // Save the uploaded file
                var fileData = context.Request.Body;
                var fileName = session.UploadRequest.files[fileId].fileName;

                // TODO: Implement actual file saving logic
                System.Diagnostics.Debug.WriteLine($"Received file upload: {fileName} ({fileData.Length} bytes)");

                // Mark file as received
                session.MarkFileReceived(fileId);

                // Send success response
                context.Response.StatusCode = 200;
                await context.Response.CompleteAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in UploadRouteHandler: {ex.Message}");
                await SendErrorResponse(context.Response, 500, "Internal Server Error");
            }
        }
    }

    /// <summary>
    /// Handles upload cancellation requests
    /// </summary>
    public class CancelRouteHandler : BaseRouteHandler
    {
        public override async Task HandleAsync(HttpRequestContext context)
        {
            if (context.Request.Method != "POST")
            {
                await SendErrorResponse(context.Response, 405, "Method Not Allowed");
                return;
            }

            try
            {
                var queryParams = ParseQueryParameters(context.Request.Path);

                if (!queryParams.ContainsKey("sessionId"))
                {
                    await SendErrorResponse(context.Response, 400, "Missing sessionId parameter");
                    return;
                }

                var sessionId = queryParams["sessionId"];
                SessionManager.Instance.CancelSession(sessionId);

                context.Response.StatusCode = 200;
                await context.Response.CompleteAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in CancelRouteHandler: {ex.Message}");
                await SendErrorResponse(context.Response, 500, "Internal Server Error");
            }
        }
    }

    /// <summary>
    /// Handles download preparation requests
    /// </summary>
    public class PrepareDownloadRouteHandler : BaseRouteHandler
    {
        public override async Task HandleAsync(HttpRequestContext context)
        {
            if (context.Request.Method != "POST")
            {
                await SendErrorResponse(context.Response, 405, "Method Not Allowed");
                return;
            }

            // TODO: Implement download preparation logic
            await SendErrorResponse(context.Response, 501, "Download API not yet implemented");
        }
    }

    /// <summary>
    /// Handles file download requests
    /// </summary>
    public class DownloadRouteHandler : BaseRouteHandler
    {
        public override async Task HandleAsync(HttpRequestContext context)
        {
            if (context.Request.Method != "GET")
            {
                await SendErrorResponse(context.Response, 405, "Method Not Allowed");
                return;
            }

            // TODO: Implement file download logic
            await SendErrorResponse(context.Response, 501, "Download API not yet implemented");
        }
    }

    /// <summary>
    /// Handles health check requests
    /// </summary>
    public class HealthCheckRouteHandler : BaseRouteHandler
    {
        public override async Task HandleAsync(HttpRequestContext context)
        {
            var healthInfo = new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                version = "2.0",
                device = LocalSendProtocol.ThisDevice.alias
            };

            await SendJsonResponse(context.Response, healthInfo);
        }
    }
}
