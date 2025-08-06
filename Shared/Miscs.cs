using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Reflection;

#if WINDOWS_UWP
using UWP = Windows.UI.Xaml.Controls;
#endif

namespace Shared
{
    /// <summary>
    /// Represents a specialized list collection for storing string values.
    /// This class extends the generic List&lt;string&gt; to provide a more specific type for string collections.
    /// </summary>
    public class StringList: List<string> { }

    /// <summary>
    /// Language code constants
    /// </summary>
    public struct Languages
    {
        /// <summary>
        /// English (United States) language code
        /// </summary>
        public static readonly string EnUS = "en-US";

        /// <summary>
        /// Checks if a language code is defined in this struct
        /// </summary>
        /// <param name="code">The language code to check</param>
        /// <returns>True if the code is defined, false otherwise</returns>
        public static bool HasCode(string code)
            => typeof(Languages).GetFields(BindingFlags.Static)
                                .Any(elm => elm.Name.Equals(code));
    }

    /// <summary>
    /// Defines HTTP response codes used in file transfer operations.
    /// These codes follow HTTP status code conventions and provide specific meanings
    /// for different file transfer scenarios in the LocalTalk protocol.
    /// </summary>
    public enum FileTransferResponses
    {
        /// <summary>
        /// Transfer completed successfully (HTTP 204 No Content).
        /// </summary>
        Finished = 204,

        /// <summary>
        /// Invalid request body received (HTTP 400 Bad Request).
        /// </summary>
        InvalidBody = 400,

        /// <summary>
        /// Invalid PIN provided for authentication (HTTP 401 Unauthorized).
        /// </summary>
        InvalidPIN = 401,

        /// <summary>
        /// Transfer request was rejected by the recipient (HTTP 403 Forbidden).
        /// </summary>
        Rejected = 403,

        /// <summary>
        /// Transfer blocked due to other active sessions (HTTP 409 Conflict).
        /// </summary>
        BlockedByOtherSessions = 409,

        /// <summary>
        /// Too many transfer requests received (HTTP 429 Too Many Requests).
        /// </summary>
        TooManyRequests = 429,

        /// <summary>
        /// Unknown or unexpected error occurred (HTTP 500 Internal Server Error).
        /// </summary>
        Unknown = 500
    }
}

// Removed problematic ListView alias that caused namespace conflicts
// Platform-specific ListView implementations are now handled in XAML files
