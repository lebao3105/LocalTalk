using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Windows.Networking.Connectivity;
using System.Runtime.Serialization.Json;
using Windows.Storage.Streams;
using System.Net.Http;
using System.Threading.Tasks;

namespace Shared
{
    public class Internet
    {
        public static Internet Instance;
        public bool IsFine { get; private set; }
        public NetworkConnectivityLevel ConnectionState;

        private static bool IsHandlerRegistered = false;

        public Internet()
        {
            Instance = this;

            if (!IsHandlerRegistered)
            {
                NetworkInformation.NetworkStatusChanged += NetworkStatusChanged;
                IsHandlerRegistered = true;
            }
        }

        ~Internet()
        {
            NetworkInformation.NetworkStatusChanged -= NetworkStatusChanged;
            IsHandlerRegistered = false;
        }

        private static void NetworkStatusChanged(object sender)
        {
            ConnectionProfile pfp = NetworkInformation.GetInternetConnectionProfile();

            Instance.ConnectionState = pfp != null ? pfp.GetNetworkConnectivityLevel()
                                                   : NetworkConnectivityLevel.None;
        }

        public static string SerializeObject<T>(T obj)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, obj);
                return Encoding.UTF8.GetString(stream.ToArray(), 0, (int)stream.Length);
            }
        }

        public static T DeserializeObject<T>(string data)
        {
            using (var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(data)))
            {
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
        }
    }
}
