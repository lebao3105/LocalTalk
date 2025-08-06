using System;
using System.Collections.Generic;
using System.Text;

#if WINDOWS_PHONE
using System.Windows;
using System.IO.IsolatedStorage;
#else
using Windows.Storage;
using Windows.UI.Xaml;
#endif

namespace Shared
{
    /// <summary>
    /// Provides centralized application settings management for the LocalTalk application.
    /// This class handles persistent storage and retrieval of user preferences and device configuration
    /// across both Windows Phone and UWP platforms using platform-specific storage mechanisms.
    /// </summary>
    public static class Settings
    {
        #region Constants
        /// <summary>
        /// Default port number for LocalTalk communication.
        /// </summary>
        private const int DefaultPort = 53317;

        /// <summary>
        /// Default multicast address for device discovery.
        /// </summary>
        private const string DefaultMulticastAddress = "224.0.0.167";
        #endregion
#if WINDOWS_PHONE
        /// <summary>
        /// Platform-specific settings storage for Windows Phone using IsolatedStorageSettings.
        /// </summary>
        private static IsolatedStorageSettings localSettings =
            IsolatedStorageSettings.ApplicationSettings;
#else
        /// <summary>
        /// Platform-specific settings storage for UWP using ApplicationDataContainer.
        /// </summary>
        private static ApplicationDataContainer localSettings =
            ApplicationData.Current.LocalSettings;
#endif

        /// <summary>
        /// Stores a setting value with the specified key in platform-specific persistent storage.
        /// </summary>
        /// <param name="key">The unique identifier for the setting.</param>
        /// <param name="value">The value to store.</param>
        public static void SetSetting(string key, object value)
        {
#if WINDOWS_UWP
            localSettings.Values[key] = value;
#else
            localSettings[key] = value;
            localSettings.Save();
#endif
        }

        /// <summary>
        /// Retrieves a setting value of the specified type using the provided key.
        /// Returns the default value for the type if the setting doesn't exist or an error occurs.
        /// </summary>
        /// <typeparam name="T">The type of the setting value to retrieve.</typeparam>
        /// <param name="key">The unique identifier for the setting.</param>
        /// <returns>The setting value if found, otherwise the default value for type T.</returns>
        public static T GetSetting<T>(string key)
        {
            try
            {
#if WINDOWS_UWP
                return localSettings.Values.ContainsKey(key) ? (T)localSettings.Values[key] : default(T);
#else
                return localSettings.Contains(key) ? (T)localSettings[key] : default(T);
#endif
            }
            catch
            {
                return default(T);
            }
        }

        /// <summary>
        /// Generates a randomized device name by combining a random adjective with a random fruit name.
        /// The adjectives and fruits are loaded from application resources.
        /// </summary>
        /// <returns>A randomized device name in the format "Adjective Fruit" (e.g., "Happy Apple").</returns>
        public static string RandomizedName()
        {
            Random rnd = new Random();
            StringList adjs = (StringList)Application.Current.Resources
                .MergedDictionaries[0]["Adjectives"];
            StringList fruits = (StringList)Application.Current.Resources
                .MergedDictionaries[0]["Fruits"];

            return $"{adjs[rnd.Next(0, adjs.Count)]} {fruits[rnd.Next(0, fruits.Count)]}";
        }

        /// <summary>
        /// Gets or sets a value indicating whether this is the first run of the application.
        /// Used to trigger initial setup procedures such as generating a default device name.
        /// </summary>
        public static bool IsFirstRun
        {
            get { return GetSetting<bool?>("IsFirstRun") ?? true; }
            set { SetSetting("IsFirstRun", value); }
        }

        #region Device informations
        /// <summary>
        /// Gets or sets the device name used for identification in the LocalTalk network.
        /// If this is the first run, a randomized name is automatically generated and stored.
        /// </summary>
        public static string DeviceName
        {
            get
            {
                if (IsFirstRun) // TODO: Move this to App.xaml.cs
                {
                    SetSetting("DeviceName", RandomizedName());
                    IsFirstRun = false;
                }
                return GetSetting<string>("DeviceName");
            }
            set { SetSetting("DeviceName", value); }
        }

        /// <summary>
        /// Gets or sets the device model identifier.
        /// Defaults to "Universal Windows" for UWP or "Windows Phone" for Windows Phone platform.
        /// </summary>
        public static string DeviceModel
        {
            get { return GetSetting<string>("DeviceModel") ??
#if WINDOWS_UWP
                    "Universal Windows";}
#else
                    "Windows Phone";}
#endif
            set { SetSetting("DeviceModel", value); }
        }
        #endregion

#region Connection
        /// <summary>
        /// Gets or sets the network port used for LocalTalk communication.
        /// Defaults to 53317 if not previously configured.
        /// </summary>
        public static int Port
        {
            get { return GetSetting<int?>("Port") ?? DefaultPort; }
            set { SetSetting("Port", value); }
        }

        /// <summary>
        /// Gets or sets the multicast address used for device discovery.
        /// Defaults to "224.0.0.167" if not previously configured.
        /// </summary>
        public static string Address
        {
            get { return GetSetting<string>("Address") ?? DefaultMulticastAddress; }
            set { SetSetting("Address", value); }
        }
#endregion
    }
}
