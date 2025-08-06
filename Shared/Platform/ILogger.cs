using System;

namespace Shared.Platform
{
    /// <summary>
    /// Log levels for filtering and categorizing log messages
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// Detailed information for debugging purposes
        /// </summary>
        Debug = 0,

        /// <summary>
        /// General information about application flow
        /// </summary>
        Info = 1,

        /// <summary>
        /// Warning about potential issues
        /// </summary>
        Warning = 2,

        /// <summary>
        /// Error conditions that don't stop the application
        /// </summary>
        Error = 3,

        /// <summary>
        /// Critical errors that may cause application failure
        /// </summary>
        Critical = 4
    }

    /// <summary>
    /// Interface for logging operations
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// Gets or sets the minimum log level to output
        /// </summary>
        LogLevel MinimumLevel { get; set; }

        /// <summary>
        /// Logs a debug message
        /// </summary>
        /// <param name="message">Message to log</param>
        /// <param name="exception">Optional exception</param>
        void Debug(string message, Exception exception = null);

        /// <summary>
        /// Logs an informational message
        /// </summary>
        /// <param name="message">Message to log</param>
        /// <param name="exception">Optional exception</param>
        void Info(string message, Exception exception = null);

        /// <summary>
        /// Logs a warning message
        /// </summary>
        /// <param name="message">Message to log</param>
        /// <param name="exception">Optional exception</param>
        void Warning(string message, Exception exception = null);

        /// <summary>
        /// Logs an error message
        /// </summary>
        /// <param name="message">Message to log</param>
        /// <param name="exception">Optional exception</param>
        void Error(string message, Exception exception = null);

        /// <summary>
        /// Logs a critical error message
        /// </summary>
        /// <param name="message">Message to log</param>
        /// <param name="exception">Optional exception</param>
        void Critical(string message, Exception exception = null);

        /// <summary>
        /// Logs a message with specified level
        /// </summary>
        /// <param name="level">Log level</param>
        /// <param name="message">Message to log</param>
        /// <param name="exception">Optional exception</param>
        void Log(LogLevel level, string message, Exception exception = null);

        /// <summary>
        /// Checks if a log level is enabled
        /// </summary>
        /// <param name="level">Log level to check</param>
        /// <returns>True if the level is enabled</returns>
        bool IsEnabled(LogLevel level);
    }

    /// <summary>
    /// Factory for creating logger instances
    /// </summary>
    public interface ILoggerFactory
    {
        /// <summary>
        /// Creates a logger for the specified category
        /// </summary>
        /// <param name="categoryName">Category name (typically class name)</param>
        /// <returns>Logger instance</returns>
        ILogger CreateLogger(string categoryName);

        /// <summary>
        /// Creates a logger for the specified type
        /// </summary>
        /// <typeparam name="T">Type to create logger for</typeparam>
        /// <returns>Logger instance</returns>
        ILogger CreateLogger<T>();
    }

    /// <summary>
    /// Log entry information
    /// </summary>
    public class LogEntry
    {
        /// <summary>
        /// Timestamp when the log entry was created
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Log level
        /// </summary>
        public LogLevel Level { get; set; }

        /// <summary>
        /// Category name (typically class name)
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Log message
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Optional exception
        /// </summary>
        public Exception Exception { get; set; }

        /// <summary>
        /// Thread ID where the log entry was created
        /// </summary>
        public int ThreadId { get; set; }

        /// <summary>
        /// Formats the log entry as a string
        /// </summary>
        /// <returns>Formatted log entry</returns>
        public override string ToString()
        {
            var exceptionText = Exception != null ? $" | Exception: {Exception}" : "";
            return $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level}] [{Category}] [Thread:{ThreadId}] {Message}{exceptionText}";
        }
    }
}
