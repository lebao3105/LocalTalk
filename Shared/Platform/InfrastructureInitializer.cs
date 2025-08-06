using System;
using Shared.Security;

namespace Shared.Platform
{
    /// <summary>
    /// Initializes the enhanced infrastructure components
    /// </summary>
    public static class InfrastructureInitializer
    {
        private static bool _initialized = false;
        private static readonly object _lock = new object();

        /// <summary>
        /// Initializes all infrastructure components with default settings
        /// </summary>
        public static void Initialize()
        {
            Initialize(new InfrastructureConfiguration());
        }

        /// <summary>
        /// Initializes all infrastructure components with custom configuration
        /// </summary>
        /// <param name="config">Infrastructure configuration</param>
        public static void Initialize(InfrastructureConfiguration config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            lock (_lock)
            {
                if (_initialized)
                    return;

                try
                {
                    // Initialize configuration management
                    InitializeConfiguration(config);

                    // Initialize logging
                    InitializeLogging(config);

                    // Initialize performance monitoring
                    InitializePerformanceMonitoring(config);

                    // Load default configuration values
                    ConfigurationManager.LoadDefaults();

                    // Validate configuration
                    ValidateConfiguration();

                    _initialized = true;

                    var logger = LogManager.GetLogger("InfrastructureInitializer");
                    logger.Info("Infrastructure initialization completed successfully");
                    
                    PerformanceManager.Counter("Infrastructure.Initializations");
                    PerformanceManager.Memory("Infrastructure");
                }
                catch (Exception ex)
                {
                    var logger = LogManager.GetLogger("InfrastructureInitializer");
                    logger.Critical("Infrastructure initialization failed", ex);
                    throw;
                }
            }
        }

        /// <summary>
        /// Checks if infrastructure has been initialized
        /// </summary>
        public static bool IsInitialized => _initialized;

        /// <summary>
        /// Resets the infrastructure (for testing purposes)
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _initialized = false;
                PerformanceManager.Current.Reset();
                
                var logger = LogManager.GetLogger("InfrastructureInitializer");
                logger.Info("Infrastructure reset completed");
            }
        }

        private static void InitializeConfiguration(InfrastructureConfiguration config)
        {
            if (config.CustomConfiguration != null)
            {
                ConfigurationManager.SetConfiguration(config.CustomConfiguration);
            }
        }

        private static void InitializeLogging(InfrastructureConfiguration config)
        {
            if (config.CustomLoggerFactory != null)
            {
                LogManager.SetFactory(config.CustomLoggerFactory);
            }
            else if (config.LoggingConfiguration != null)
            {
                var loggerFactory = CreateLoggerFactory(config.LoggingConfiguration);
                LogManager.SetFactory(loggerFactory);
            }
        }

        private static void InitializePerformanceMonitoring(InfrastructureConfiguration config)
        {
            if (config.CustomPerformanceMonitor != null)
            {
                PerformanceManager.SetMonitor(config.CustomPerformanceMonitor);
            }
        }

        private static ILoggerFactory CreateLoggerFactory(LoggingConfiguration loggingConfig)
        {
            return new LoggerFactory(categoryName =>
            {
                var loggers = new System.Collections.Generic.List<ILogger>();

                if (loggingConfig.EnableDebugOutput)
                {
                    var debugLogger = new DebugLogger(categoryName);
                    debugLogger.MinimumLevel = loggingConfig.MinimumLevel;
                    loggers.Add(debugLogger);
                }

                if (loggingConfig.EnableConsoleOutput)
                {
                    var consoleLogger = new ConsoleLogger(categoryName);
                    consoleLogger.MinimumLevel = loggingConfig.MinimumLevel;
                    loggers.Add(consoleLogger);
                }

                if (loggers.Count == 0)
                {
                    // Fallback to debug logger
                    var debugLogger = new DebugLogger(categoryName);
                    debugLogger.MinimumLevel = loggingConfig.MinimumLevel;
                    loggers.Add(debugLogger);
                }

                if (loggers.Count == 1)
                {
                    return loggers[0];
                }

                var composite = new CompositeLogger(categoryName, loggers.ToArray());
                composite.MinimumLevel = loggingConfig.MinimumLevel;
                return composite;
            });
        }

        private static void ValidateConfiguration()
        {
            var securityConfig = new SecurityConfiguration();
            securityConfig.LoadFrom(ConfigurationManager.Current);
            
            var validationResult = securityConfig.Validate();
            var logger = LogManager.GetLogger("InfrastructureInitializer");

            if (!validationResult.IsValid)
            {
                foreach (var error in validationResult.Errors)
                {
                    logger.Error($"Configuration validation error: {error}");
                }
                throw new InvalidOperationException("Configuration validation failed");
            }

            if (validationResult.Warnings.Count > 0)
            {
                foreach (var warning in validationResult.Warnings)
                {
                    logger.Warning($"Configuration validation warning: {warning}");
                }
            }
        }
    }

    /// <summary>
    /// Configuration for infrastructure initialization
    /// </summary>
    public class InfrastructureConfiguration
    {
        /// <summary>
        /// Custom configuration provider
        /// </summary>
        public IConfiguration CustomConfiguration { get; set; }

        /// <summary>
        /// Custom logger factory
        /// </summary>
        public ILoggerFactory CustomLoggerFactory { get; set; }

        /// <summary>
        /// Custom performance monitor
        /// </summary>
        public IPerformanceMonitor CustomPerformanceMonitor { get; set; }

        /// <summary>
        /// Logging configuration
        /// </summary>
        public LoggingConfiguration LoggingConfiguration { get; set; } = new LoggingConfiguration();
    }

    /// <summary>
    /// Logging configuration
    /// </summary>
    public class LoggingConfiguration
    {
        /// <summary>
        /// Minimum log level
        /// </summary>
        public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

        /// <summary>
        /// Enable debug output
        /// </summary>
        public bool EnableDebugOutput { get; set; } = true;

        /// <summary>
        /// Enable console output
        /// </summary>
        public bool EnableConsoleOutput { get; set; } = false;
    }
}
