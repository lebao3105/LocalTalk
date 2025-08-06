using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Shared.Platform
{
    /// <summary>
    /// Base logger implementation
    /// </summary>
    public abstract class Logger : ILogger
    {
        private readonly string _categoryName;
        private LogLevel _minimumLevel = LogLevel.Info;

        protected Logger(string categoryName)
        {
            _categoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
        }

        /// <summary>
        /// Gets or sets the minimum log level to output
        /// </summary>
        public LogLevel MinimumLevel
        {
            get => _minimumLevel;
            set => _minimumLevel = value;
        }

        /// <summary>
        /// Logs a debug message
        /// </summary>
        public void Debug(string message, Exception exception = null)
        {
            Log(LogLevel.Debug, message, exception);
        }

        /// <summary>
        /// Logs an informational message
        /// </summary>
        public void Info(string message, Exception exception = null)
        {
            Log(LogLevel.Info, message, exception);
        }

        /// <summary>
        /// Logs a warning message
        /// </summary>
        public void Warning(string message, Exception exception = null)
        {
            Log(LogLevel.Warning, message, exception);
        }

        /// <summary>
        /// Logs an error message
        /// </summary>
        public void Error(string message, Exception exception = null)
        {
            Log(LogLevel.Error, message, exception);
        }

        /// <summary>
        /// Logs a critical error message
        /// </summary>
        public void Critical(string message, Exception exception = null)
        {
            Log(LogLevel.Critical, message, exception);
        }

        /// <summary>
        /// Logs a message with specified level
        /// </summary>
        public void Log(LogLevel level, string message, Exception exception = null)
        {
            if (!IsEnabled(level) || string.IsNullOrEmpty(message))
                return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Category = _categoryName,
                Message = message,
                Exception = exception,
                ThreadId = Thread.CurrentThread.ManagedThreadId
            };

            WriteLog(entry);
        }

        /// <summary>
        /// Checks if a log level is enabled
        /// </summary>
        public bool IsEnabled(LogLevel level)
        {
            return level >= _minimumLevel;
        }

        /// <summary>
        /// Writes the log entry to the output
        /// </summary>
        /// <param name="entry">Log entry to write</param>
        protected abstract void WriteLog(LogEntry entry);
    }

    /// <summary>
    /// Debug output logger implementation
    /// </summary>
    public class DebugLogger : Logger
    {
        public DebugLogger(string categoryName) : base(categoryName)
        {
        }

        protected override void WriteLog(LogEntry entry)
        {
            System.Diagnostics.Debug.WriteLine(entry.ToString());
        }
    }

    /// <summary>
    /// Console logger implementation
    /// </summary>
    public class ConsoleLogger : Logger
    {
        public ConsoleLogger(string categoryName) : base(categoryName)
        {
        }

        protected override void WriteLog(LogEntry entry)
        {
            // Use different colors for different log levels
            var originalColor = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = GetColorForLevel(entry.Level);
                Console.WriteLine(entry.ToString());
            }
            finally
            {
                Console.ForegroundColor = originalColor;
            }
        }

        private static ConsoleColor GetColorForLevel(LogLevel level)
        {
            return level switch
            {
                LogLevel.Debug => ConsoleColor.Gray,
                LogLevel.Info => ConsoleColor.White,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Critical => ConsoleColor.Magenta,
                _ => ConsoleColor.White
            };
        }
    }

    /// <summary>
    /// Composite logger that writes to multiple outputs
    /// </summary>
    public class CompositeLogger : Logger
    {
        private readonly List<ILogger> _loggers;

        public CompositeLogger(string categoryName, params ILogger[] loggers) : base(categoryName)
        {
            _loggers = new List<ILogger>(loggers ?? throw new ArgumentNullException(nameof(loggers)));
        }

        protected override void WriteLog(LogEntry entry)
        {
            foreach (var logger in _loggers)
            {
                if (logger.IsEnabled(entry.Level))
                {
                    logger.Log(entry.Level, entry.Message, entry.Exception);
                }
            }
        }

        public void AddLogger(ILogger logger)
        {
            if (logger != null)
            {
                _loggers.Add(logger);
            }
        }

        public void RemoveLogger(ILogger logger)
        {
            _loggers.Remove(logger);
        }
    }

    /// <summary>
    /// Default logger factory implementation
    /// </summary>
    public class LoggerFactory : ILoggerFactory
    {
        private readonly ConcurrentDictionary<string, ILogger> _loggers = new ConcurrentDictionary<string, ILogger>();
        private readonly Func<string, ILogger> _loggerCreator;

        public LoggerFactory(Func<string, ILogger> loggerCreator = null)
        {
            _loggerCreator = loggerCreator ?? (categoryName => new DebugLogger(categoryName));
        }

        public ILogger CreateLogger(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName))
                throw new ArgumentNullException(nameof(categoryName));

            return _loggers.GetOrAdd(categoryName, _loggerCreator);
        }

        public ILogger CreateLogger<T>()
        {
            return CreateLogger(typeof(T).Name);
        }
    }

    /// <summary>
    /// Static logger manager for easy access
    /// </summary>
    public static class LogManager
    {
        private static ILoggerFactory _factory = new LoggerFactory();

        /// <summary>
        /// Sets the logger factory
        /// </summary>
        public static void SetFactory(ILoggerFactory factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <summary>
        /// Gets a logger for the specified category
        /// </summary>
        public static ILogger GetLogger(string categoryName)
        {
            return _factory.CreateLogger(categoryName);
        }

        /// <summary>
        /// Gets a logger for the specified type
        /// </summary>
        public static ILogger GetLogger<T>()
        {
            return _factory.CreateLogger<T>();
        }

        /// <summary>
        /// Gets a logger for the calling class
        /// </summary>
        public static ILogger GetLogger()
        {
            var frame = new System.Diagnostics.StackFrame(1);
            var method = frame.GetMethod();
            var className = method?.DeclaringType?.Name ?? "Unknown";
            return GetLogger(className);
        }
    }
}
