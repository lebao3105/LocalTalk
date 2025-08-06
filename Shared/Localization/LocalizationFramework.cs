using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Shared.Platform;

namespace Shared.Localization
{
    /// <summary>
    /// Comprehensive localization framework with resource management, pluralization, and cultural formatting support
    /// </summary>
    public class LocalizationFramework : IDisposable
    {
        private static LocalizationFramework _instance;
        private readonly ConcurrentDictionary<string, LocalizationResource> _resources;
        private readonly ConcurrentDictionary<string, CultureInfo> _supportedCultures;
        private readonly LocalizationConfiguration _config;
        private CultureInfo _currentCulture;
        private CultureInfo _fallbackCulture;
        private bool _disposed;

        public static LocalizationFramework Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LocalizationFramework();
                }
                return _instance;
            }
        }

        public event EventHandler<CultureChangedEventArgs> CultureChanged;
        public event EventHandler<ResourceLoadedEventArgs> ResourceLoaded;
        public event EventHandler<LocalizationErrorEventArgs> LocalizationError;

        public CultureInfo CurrentCulture => _currentCulture;
        public CultureInfo FallbackCulture => _fallbackCulture;
        public IEnumerable<CultureInfo> SupportedCultures => _supportedCultures.Values;

        private LocalizationFramework()
        {
            _resources = new ConcurrentDictionary<string, LocalizationResource>();
            _supportedCultures = new ConcurrentDictionary<string, CultureInfo>();
            _config = new LocalizationConfiguration();
            
            // Set default cultures
            _currentCulture = CultureInfo.CurrentUICulture;
            _fallbackCulture = new CultureInfo("en-US");
            
            // Initialize with default supported cultures
            InitializeDefaultCultures();
        }

        /// <summary>
        /// Initializes the localization framework
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                // Load resources for current culture
                await LoadResourcesAsync(_currentCulture.Name);
                
                // Load fallback resources if different from current
                if (!string.Equals(_currentCulture.Name, _fallbackCulture.Name, StringComparison.OrdinalIgnoreCase))
                {
                    await LoadResourcesAsync(_fallbackCulture.Name);
                }
            }
            catch (Exception ex)
            {
                OnLocalizationError(new LocalizationErrorEventArgs
                {
                    ErrorMessage = $"Failed to initialize localization: {ex.Message}",
                    Exception = ex
                });
            }
        }

        /// <summary>
        /// Changes the current culture
        /// </summary>
        public async Task ChangeCultureAsync(string cultureName)
        {
            try
            {
                var newCulture = new CultureInfo(cultureName);
                var previousCulture = _currentCulture;
                
                // Load resources for new culture
                await LoadResourcesAsync(cultureName);
                
                _currentCulture = newCulture;
                
                // Update thread culture
                CultureInfo.CurrentUICulture = newCulture;
                CultureInfo.CurrentCulture = newCulture;
                
                OnCultureChanged(new CultureChangedEventArgs
                {
                    PreviousCulture = previousCulture,
                    NewCulture = newCulture
                });
            }
            catch (Exception ex)
            {
                OnLocalizationError(new LocalizationErrorEventArgs
                {
                    ErrorMessage = $"Failed to change culture to {cultureName}: {ex.Message}",
                    Exception = ex
                });
            }
        }

        /// <summary>
        /// Gets a localized string
        /// </summary>
        public string GetString(string key, params object[] args)
        {
            try
            {
                var localizedString = GetLocalizedString(key, _currentCulture.Name);
                
                if (localizedString == null && !string.Equals(_currentCulture.Name, _fallbackCulture.Name, StringComparison.OrdinalIgnoreCase))
                {
                    localizedString = GetLocalizedString(key, _fallbackCulture.Name);
                }
                
                if (localizedString == null)
                {
                    return $"[{key}]"; // Return key in brackets if not found
                }
                
                // Format with arguments if provided
                if (args != null && args.Length > 0)
                {
                    return string.Format(localizedString, args);
                }
                
                return localizedString;
            }
            catch (Exception ex)
            {
                OnLocalizationError(new LocalizationErrorEventArgs
                {
                    ErrorMessage = $"Error getting string for key '{key}': {ex.Message}",
                    Exception = ex
                });
                
                return $"[{key}]";
            }
        }

        /// <summary>
        /// Gets a pluralized string based on count
        /// </summary>
        public string GetPluralString(string key, int count, params object[] args)
        {
            try
            {
                var pluralKey = GetPluralKey(key, count, _currentCulture);
                var localizedString = GetLocalizedString(pluralKey, _currentCulture.Name);
                
                if (localizedString == null && !string.Equals(_currentCulture.Name, _fallbackCulture.Name, StringComparison.OrdinalIgnoreCase))
                {
                    pluralKey = GetPluralKey(key, count, _fallbackCulture);
                    localizedString = GetLocalizedString(pluralKey, _fallbackCulture.Name);
                }
                
                if (localizedString == null)
                {
                    return $"[{key}]";
                }
                
                // Include count in arguments
                var allArgs = new object[] { count }.Concat(args ?? new object[0]).ToArray();
                return string.Format(localizedString, allArgs);
            }
            catch (Exception ex)
            {
                OnLocalizationError(new LocalizationErrorEventArgs
                {
                    ErrorMessage = $"Error getting plural string for key '{key}': {ex.Message}",
                    Exception = ex
                });
                
                return $"[{key}]";
            }
        }

        /// <summary>
        /// Formats a date according to current culture
        /// </summary>
        public string FormatDate(DateTime date, string format = null)
        {
            try
            {
                if (string.IsNullOrEmpty(format))
                {
                    return date.ToString(_currentCulture);
                }
                
                return date.ToString(format, _currentCulture);
            }
            catch (Exception ex)
            {
                OnLocalizationError(new LocalizationErrorEventArgs
                {
                    ErrorMessage = $"Error formatting date: {ex.Message}",
                    Exception = ex
                });
                
                return date.ToString();
            }
        }

        /// <summary>
        /// Formats a number according to current culture
        /// </summary>
        public string FormatNumber(double number, string format = null)
        {
            try
            {
                if (string.IsNullOrEmpty(format))
                {
                    return number.ToString(_currentCulture);
                }
                
                return number.ToString(format, _currentCulture);
            }
            catch (Exception ex)
            {
                OnLocalizationError(new LocalizationErrorEventArgs
                {
                    ErrorMessage = $"Error formatting number: {ex.Message}",
                    Exception = ex
                });
                
                return number.ToString();
            }
        }

        /// <summary>
        /// Formats a currency amount according to current culture
        /// </summary>
        public string FormatCurrency(decimal amount)
        {
            try
            {
                return amount.ToString("C", _currentCulture);
            }
            catch (Exception ex)
            {
                OnLocalizationError(new LocalizationErrorEventArgs
                {
                    ErrorMessage = $"Error formatting currency: {ex.Message}",
                    Exception = ex
                });
                
                return amount.ToString();
            }
        }

        /// <summary>
        /// Loads resources for a specific culture
        /// </summary>
        private async Task LoadResourcesAsync(string cultureName)
        {
            try
            {
                var resourceKey = $"resources_{cultureName}";
                
                if (_resources.ContainsKey(resourceKey))
                {
                    return; // Already loaded
                }
                
                // Try to load from platform-specific resource provider
                var platform = PlatformFactory.Current;
                var resourceProvider = platform?.GetResourceProvider();
                
                if (resourceProvider != null)
                {
                    var resourceData = await resourceProvider.LoadResourcesAsync(cultureName);
                    
                    if (resourceData != null)
                    {
                        var resource = new LocalizationResource
                        {
                            CultureName = cultureName,
                            Strings = resourceData,
                            LoadedAt = DateTime.Now
                        };
                        
                        _resources[resourceKey] = resource;
                        
                        OnResourceLoaded(new ResourceLoadedEventArgs
                        {
                            CultureName = cultureName,
                            StringCount = resourceData.Count
                        });
                        
                        return;
                    }
                }
                
                // Fallback to embedded resources or default strings
                await LoadDefaultResourcesAsync(cultureName);
            }
            catch (Exception ex)
            {
                OnLocalizationError(new LocalizationErrorEventArgs
                {
                    ErrorMessage = $"Failed to load resources for culture {cultureName}: {ex.Message}",
                    Exception = ex
                });
            }
        }

        /// <summary>
        /// Loads default resources for a culture
        /// </summary>
        private async Task LoadDefaultResourcesAsync(string cultureName)
        {
            var resourceKey = $"resources_{cultureName}";
            var defaultStrings = GetDefaultStrings(cultureName);
            
            var resource = new LocalizationResource
            {
                CultureName = cultureName,
                Strings = defaultStrings,
                LoadedAt = DateTime.Now
            };
            
            _resources[resourceKey] = resource;
            
            OnResourceLoaded(new ResourceLoadedEventArgs
            {
                CultureName = cultureName,
                StringCount = defaultStrings.Count
            });
        }

        /// <summary>
        /// Gets default strings for a culture
        /// </summary>
        private Dictionary<string, string> GetDefaultStrings(string cultureName)
        {
            // This would typically load from embedded resources or a default resource file
            var strings = new Dictionary<string, string>();
            
            if (cultureName.StartsWith("en"))
            {
                strings["app.name"] = "LocalTalk";
                strings["app.description"] = "Share files across devices";
                strings["transfer.sending"] = "Sending...";
                strings["transfer.receiving"] = "Receiving...";
                strings["transfer.completed"] = "Transfer completed";
                strings["transfer.failed"] = "Transfer failed";
                strings["file.count.singular"] = "{0} file";
                strings["file.count.plural"] = "{0} files";
                strings["device.discovered"] = "Device discovered";
                strings["device.connected"] = "Connected to device";
                strings["error.network"] = "Network error occurred";
                strings["error.permission"] = "Permission denied";
            }
            else
            {
                // For other cultures, use English as fallback
                strings = GetDefaultStrings("en-US");
            }
            
            return strings;
        }

        /// <summary>
        /// Gets a localized string from resources
        /// </summary>
        private string GetLocalizedString(string key, string cultureName)
        {
            var resourceKey = $"resources_{cultureName}";
            
            if (_resources.TryGetValue(resourceKey, out var resource))
            {
                return resource.Strings.TryGetValue(key, out var value) ? value : null;
            }
            
            return null;
        }

        /// <summary>
        /// Gets the appropriate plural key based on count and culture
        /// </summary>
        private string GetPluralKey(string baseKey, int count, CultureInfo culture)
        {
            // Simplified pluralization rules - in practice, you'd use more sophisticated rules
            var pluralForm = GetPluralForm(count, culture);
            
            return pluralForm switch
            {
                PluralForm.Zero => $"{baseKey}.zero",
                PluralForm.One => $"{baseKey}.singular",
                PluralForm.Two => $"{baseKey}.dual",
                PluralForm.Few => $"{baseKey}.few",
                PluralForm.Many => $"{baseKey}.many",
                _ => $"{baseKey}.plural"
            };
        }

        /// <summary>
        /// Determines plural form based on count and culture
        /// </summary>
        private PluralForm GetPluralForm(int count, CultureInfo culture)
        {
            // Simplified pluralization - in practice, use CLDR plural rules
            if (count == 0)
                return PluralForm.Zero;
            if (count == 1)
                return PluralForm.One;
            if (count == 2 && (culture.Name.StartsWith("ar") || culture.Name.StartsWith("he")))
                return PluralForm.Two;
            
            return PluralForm.Other;
        }

        /// <summary>
        /// Initializes default supported cultures
        /// </summary>
        private void InitializeDefaultCultures()
        {
            var defaultCultures = new[]
            {
                "en-US", "en-GB", "es-ES", "fr-FR", "de-DE", "it-IT", "pt-BR",
                "ru-RU", "zh-CN", "zh-TW", "ja-JP", "ko-KR", "ar-SA", "he-IL"
            };
            
            foreach (var cultureName in defaultCultures)
            {
                try
                {
                    var culture = new CultureInfo(cultureName);
                    _supportedCultures[cultureName] = culture;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to add culture {cultureName}: {ex.Message}");
                }
            }
        }

        private void OnCultureChanged(CultureChangedEventArgs args)
        {
            CultureChanged?.Invoke(this, args);
        }

        private void OnResourceLoaded(ResourceLoadedEventArgs args)
        {
            ResourceLoaded?.Invoke(this, args);
        }

        private void OnLocalizationError(LocalizationErrorEventArgs args)
        {
            LocalizationError?.Invoke(this, args);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _resources.Clear();
                _supportedCultures.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Localization configuration
    /// </summary>
    public class LocalizationConfiguration
    {
        public string DefaultCulture { get; set; } = "en-US";
        public string FallbackCulture { get; set; } = "en-US";
        public bool EnablePluralizations { get; set; } = true;
        public bool EnableCaching { get; set; } = true;
        public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromHours(24);
    }

    /// <summary>
    /// Localization resource
    /// </summary>
    internal class LocalizationResource
    {
        public string CultureName { get; set; }
        public Dictionary<string, string> Strings { get; set; }
        public DateTime LoadedAt { get; set; }
    }

    /// <summary>
    /// Plural form enumeration
    /// </summary>
    public enum PluralForm
    {
        Zero,
        One,
        Two,
        Few,
        Many,
        Other
    }

    /// <summary>
    /// Culture changed event arguments
    /// </summary>
    public class CultureChangedEventArgs : EventArgs
    {
        public CultureInfo PreviousCulture { get; set; }
        public CultureInfo NewCulture { get; set; }
    }

    /// <summary>
    /// Resource loaded event arguments
    /// </summary>
    public class ResourceLoadedEventArgs : EventArgs
    {
        public string CultureName { get; set; }
        public int StringCount { get; set; }
    }

    /// <summary>
    /// Localization error event arguments
    /// </summary>
    public class LocalizationErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// Resource provider interface
    /// </summary>
    public interface IResourceProvider
    {
        Task<Dictionary<string, string>> LoadResourcesAsync(string cultureName);
        Task<bool> SaveResourcesAsync(string cultureName, Dictionary<string, string> resources);
        Task<string[]> GetAvailableCulturesAsync();
    }
}
