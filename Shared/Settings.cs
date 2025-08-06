using System;
using Windows.Storage;
using Windows.UI.Xaml;

namespace LocalTalk.Shared
{
    public static class Settings
    {
        private static ApplicationDataContainer localSettings =
            ApplicationData.Current.LocalSettings;

        public static void SetSetting(string key, object value)
        {
            localSettings.Values[key] = value;
        }

        public static T GetSetting<T>(string key)
        {
            try
            {
                return localSettings.Values.ContainsKey(key) ? (T)localSettings.Values[key] : default(T);
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

        public static string Language
        {
            get { return GetSetting<string>("Language") ?? Windows.Globalization.ApplicationLanguages.Languages[0]; }
            set { SetSetting("Language", value); }
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
