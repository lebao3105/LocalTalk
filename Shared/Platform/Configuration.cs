using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace Shared.Platform
{
    /// <summary>
    /// In-memory configuration implementation with thread-safe operations
    /// </summary>
    public class MemoryConfiguration : IConfiguration
    {
        private readonly ConcurrentDictionary<string, string> _values = new ConcurrentDictionary<string, string>();
        private readonly ILogger _logger;

        public MemoryConfiguration()
        {
            _logger = LogManager.GetLogger<MemoryConfiguration>();
        }

        public event EventHandler<ConfigurationChangedEventArgs> ConfigurationChanged;

        public string GetValue(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            _values.TryGetValue(key, out var value);
            return value;
        }

        public string GetValue(string key, string defaultValue)
        {
            return GetValue(key) ?? defaultValue;
        }

        public T GetValue<T>(string key, T defaultValue = default)
        {
            var stringValue = GetValue(key);
            if (string.IsNullOrEmpty(stringValue))
                return defaultValue;

            try
            {
                return ConvertValue<T>(stringValue);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to convert configuration value '{key}' = '{stringValue}' to type {typeof(T).Name}", ex);
                return defaultValue;
            }
        }

        public void SetValue(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            var oldValue = GetValue(key);
            var changeType = oldValue == null ? ConfigurationChangeType.Added : ConfigurationChangeType.Modified;

            if (value == null)
            {
                _values.TryRemove(key, out _);
                changeType = ConfigurationChangeType.Removed;
            }
            else
            {
                _values[key] = value;
            }

            OnConfigurationChanged(new ConfigurationChangedEventArgs
            {
                Key = key,
                OldValue = oldValue,
                NewValue = value,
                ChangeType = changeType
            });
        }

        public void SetValue<T>(string key, T value)
        {
            SetValue(key, value?.ToString());
        }

        public bool HasKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            return _values.ContainsKey(key);
        }

        public bool RemoveKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            var oldValue = GetValue(key);
            var removed = _values.TryRemove(key, out _);

            if (removed)
            {
                OnConfigurationChanged(new ConfigurationChangedEventArgs
                {
                    Key = key,
                    OldValue = oldValue,
                    NewValue = null,
                    ChangeType = ConfigurationChangeType.Removed
                });
            }

            return removed;
        }

        public IEnumerable<string> GetAllKeys()
        {
            return _values.Keys.ToList();
        }

        public virtual void Reload()
        {
            // Base implementation does nothing - override in derived classes
            _logger.Debug("Configuration reload requested (no-op for MemoryConfiguration)");
        }

        public virtual void Save()
        {
            // Base implementation does nothing - override in derived classes
            _logger.Debug("Configuration save requested (no-op for MemoryConfiguration)");
        }

        protected virtual void OnConfigurationChanged(ConfigurationChangedEventArgs args)
        {
            ConfigurationChanged?.Invoke(this, args);
        }

        private static T ConvertValue<T>(string value)
        {
            var targetType = typeof(T);
            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (underlyingType == typeof(string))
                return (T)(object)value;

            if (underlyingType.IsEnum)
                return (T)Enum.Parse(underlyingType, value, true);

            var converter = TypeDescriptor.GetConverter(underlyingType);
            if (converter.CanConvertFrom(typeof(string)))
                return (T)converter.ConvertFromString(value);

            // Fallback to Convert.ChangeType
            return (T)Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Configuration manager for centralized configuration access
    /// </summary>
    public static class ConfigurationManager
    {
        private static IConfiguration _current = new MemoryConfiguration();
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets the current configuration instance
        /// </summary>
        public static IConfiguration Current
        {
            get
            {
                lock (_lock)
                {
                    return _current;
                }
            }
        }

        /// <summary>
        /// Sets the configuration instance
        /// </summary>
        /// <param name="configuration">Configuration instance</param>
        public static void SetConfiguration(IConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            lock (_lock)
            {
                _current = configuration;
            }
        }

        /// <summary>
        /// Loads default configuration values
        /// </summary>
        public static void LoadDefaults()
        {
            var config = Current;

            // Logging configuration
            config.SetValue("Logging.MinimumLevel", LogLevel.Info.ToString());
            config.SetValue("Logging.EnableConsole", "false");
            config.SetValue("Logging.EnableDebug", "true");

            // Security configuration
            config.SetValue("Security.ThreatCacheExpiryMinutes", "60");
            config.SetValue("Security.MaxRequestsPerMinute", "100");
            config.SetValue("Security.EnableSqlInjectionDetection", "true");
            config.SetValue("Security.EnableXssDetection", "true");
            config.SetValue("Security.EnablePathTraversalDetection", "true");

            // Network configuration
            config.SetValue("Network.InterfaceCacheTimeoutSeconds", "30");
            config.SetValue("Network.ConnectivityTestTimeoutSeconds", "10");
            config.SetValue("Network.MaxRetryAttempts", "3");

            // Firewall configuration
            config.SetValue("Firewall.DefaultPortMappingTimeoutMinutes", "60");
            config.SetValue("Firewall.EnableUpnp", "true");
            config.SetValue("Firewall.EnableNatPmp", "true");
            config.SetValue("Firewall.EnablePcp", "false");

            LogManager.GetLogger("ConfigurationManager").Info("Default configuration loaded");
        }
    }
}
