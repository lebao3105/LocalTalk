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
    public static class Settings
    {
#if WINDOWS_PHONE
        private static IsolatedStorageSettings localSettings =
            IsolatedStorageSettings.ApplicationSettings;
#else
        private static ApplicationDataContainer localSettings =
            ApplicationData.Current.LocalSettings;
#endif

        public static void SetSetting(string key, object value)
        {
#if WINDOWS_UWP
            localSettings.Values[key] = value;
#else
            localSettings[key] = value;
            localSettings.Save();
#endif
        }

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

        public static string RandomizedName()
        {
            Random rnd = new Random();
            StringList adjs = (StringList)Application.Current.Resources
                .MergedDictionaries[0]["Adjectives"];
            StringList fruits = (StringList)Application.Current.Resources
                .MergedDictionaries[0]["Fruits"];

            return $"{adjs[rnd.Next(0, adjs.Count)]} {fruits[rnd.Next(0, fruits.Count)]}";
        }

        public static bool IsFirstRun
        {
            get { return GetSetting<bool?>("IsFirstRun") ?? true; }
            set { SetSetting("IsFirstRun", value); }
        }

        #region Device informations
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
        public static int Port
        {
            get { return GetSetting<int?>("Port") ?? 53317; }
            set { SetSetting("Port", value); }
        }

        public static string Address
        {
            get { return GetSetting<string>("Address") ?? "224.0.0.167"; }
            set { SetSetting("Address", value); }
        }
#endregion
    }
}
