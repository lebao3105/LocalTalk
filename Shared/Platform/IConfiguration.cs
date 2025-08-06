using System;
using System.Collections.Generic;

namespace Shared.Platform
{
    /// <summary>
    /// Interface for configuration management
    /// </summary>
    public interface IConfiguration
    {
        /// <summary>
        /// Gets a configuration value by key
        /// </summary>
        /// <param name="key">Configuration key</param>
        /// <returns>Configuration value or null if not found</returns>
        string GetValue(string key);

        /// <summary>
        /// Gets a configuration value by key with a default value
        /// </summary>
        /// <param name="key">Configuration key</param>
        /// <param name="defaultValue">Default value if key not found</param>
        /// <returns>Configuration value or default value</returns>
        string GetValue(string key, string defaultValue);

        /// <summary>
        /// Gets a configuration value as a specific type
        /// </summary>
        /// <typeparam name="T">Type to convert to</typeparam>
        /// <param name="key">Configuration key</param>
        /// <param name="defaultValue">Default value if key not found or conversion fails</param>
        /// <returns>Configuration value converted to type T</returns>
        T GetValue<T>(string key, T defaultValue = default);

        /// <summary>
        /// Sets a configuration value
        /// </summary>
        /// <param name="key">Configuration key</param>
        /// <param name="value">Configuration value</param>
        void SetValue(string key, string value);

        /// <summary>
        /// Sets a configuration value of a specific type
        /// </summary>
        /// <typeparam name="T">Type of the value</typeparam>
        /// <param name="key">Configuration key</param>
        /// <param name="value">Configuration value</param>
        void SetValue<T>(string key, T value);

        /// <summary>
        /// Checks if a configuration key exists
        /// </summary>
        /// <param name="key">Configuration key</param>
        /// <returns>True if key exists</returns>
        bool HasKey(string key);

        /// <summary>
        /// Removes a configuration key
        /// </summary>
        /// <param name="key">Configuration key</param>
        /// <returns>True if key was removed</returns>
        bool RemoveKey(string key);

        /// <summary>
        /// Gets all configuration keys
        /// </summary>
        /// <returns>Collection of all keys</returns>
        IEnumerable<string> GetAllKeys();

        /// <summary>
        /// Reloads configuration from source
        /// </summary>
        void Reload();

        /// <summary>
        /// Saves configuration to persistent storage
        /// </summary>
        void Save();

        /// <summary>
        /// Event raised when configuration changes
        /// </summary>
        event EventHandler<ConfigurationChangedEventArgs> ConfigurationChanged;
    }

    /// <summary>
    /// Event arguments for configuration changes
    /// </summary>
    public class ConfigurationChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The configuration key that changed
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// The old value (null if key was added)
        /// </summary>
        public string OldValue { get; set; }

        /// <summary>
        /// The new value (null if key was removed)
        /// </summary>
        public string NewValue { get; set; }

        /// <summary>
        /// Type of change
        /// </summary>
        public ConfigurationChangeType ChangeType { get; set; }
    }

    /// <summary>
    /// Types of configuration changes
    /// </summary>
    public enum ConfigurationChangeType
    {
        /// <summary>
        /// A new key was added
        /// </summary>
        Added,

        /// <summary>
        /// An existing key was modified
        /// </summary>
        Modified,

        /// <summary>
        /// A key was removed
        /// </summary>
        Removed
    }

    /// <summary>
    /// Configuration section interface for strongly-typed configuration
    /// </summary>
    public interface IConfigurationSection
    {
        /// <summary>
        /// Section name
        /// </summary>
        string SectionName { get; }

        /// <summary>
        /// Loads configuration from the configuration provider
        /// </summary>
        /// <param name="configuration">Configuration provider</param>
        void LoadFrom(IConfiguration configuration);

        /// <summary>
        /// Saves configuration to the configuration provider
        /// </summary>
        /// <param name="configuration">Configuration provider</param>
        void SaveTo(IConfiguration configuration);

        /// <summary>
        /// Validates the configuration section
        /// </summary>
        /// <returns>Validation result</returns>
        ConfigurationValidationResult Validate();
    }

    /// <summary>
    /// Configuration validation result
    /// </summary>
    public class ConfigurationValidationResult
    {
        /// <summary>
        /// Whether the configuration is valid
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Validation errors
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// Validation warnings
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// Adds an error message
        /// </summary>
        public void AddError(string message)
        {
            Errors.Add(message);
            IsValid = false;
        }

        /// <summary>
        /// Adds a warning message
        /// </summary>
        public void AddWarning(string message)
        {
            Warnings.Add(message);
        }
    }
}
