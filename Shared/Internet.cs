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
    /// <summary>
    /// Provides network connectivity monitoring and JSON serialization utilities for the LocalTalk application.
    /// This class manages network status changes and provides helper methods for object serialization/deserialization.
    /// </summary>
    public class Internet
    {
        /// <summary>
        /// Gets the singleton instance of the Internet class.
        /// </summary>
        public static Internet Instance;

        /// <summary>
        /// Gets a value indicating whether the internet connection is functioning properly.
        /// </summary>
        public bool IsFine { get; private set; }

        /// <summary>
        /// Gets or sets the current network connectivity level.
        /// </summary>
        public NetworkConnectivityLevel ConnectionState;

        /// <summary>
        /// Tracks whether the network status change handler has been registered to prevent duplicate registrations.
        /// </summary>
        private static bool IsHandlerRegistered = false;

        /// <summary>
        /// Initializes a new instance of the Internet class.
        /// Sets up network status monitoring and registers event handlers for connectivity changes.
        /// </summary>
        public Internet()
        {
            Instance = this;

            if (!IsHandlerRegistered)
            {
                NetworkInformation.NetworkStatusChanged += NetworkStatusChanged;
                IsHandlerRegistered = true;
            }
        }

        /// <summary>
        /// Finalizes an instance of the Internet class.
        /// Unregisters network status change event handlers to prevent memory leaks.
        /// </summary>
        ~Internet()
        {
            NetworkInformation.NetworkStatusChanged -= NetworkStatusChanged;
            IsHandlerRegistered = false;
        }

        /// <summary>
        /// Handles network status change events and updates the connection state.
        /// This method is called automatically when the network connectivity level changes.
        /// </summary>
        /// <param name="sender">The event sender (not used).</param>
        private static void NetworkStatusChanged(object sender)
        {
            ConnectionProfile pfp = NetworkInformation.GetInternetConnectionProfile();

            Instance.ConnectionState = pfp != null ? pfp.GetNetworkConnectivityLevel()
                                                   : NetworkConnectivityLevel.None;
        }

        /// <summary>
        /// Serializes an object to a JSON string using DataContractJsonSerializer.
        /// </summary>
        /// <typeparam name="T">The type of object to serialize.</typeparam>
        /// <param name="obj">The object to serialize.</param>
        /// <returns>A JSON string representation of the object.</returns>
        public static string SerializeObject<T>(T obj)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, obj);
                return Encoding.UTF8.GetString(stream.ToArray(), 0, (int)stream.Length);
            }
        }

        /// <summary>
        /// Deserializes a JSON string to an object of the specified type using DataContractJsonSerializer.
        /// </summary>
        /// <typeparam name="T">The type of object to deserialize to.</typeparam>
        /// <param name="data">The JSON string to deserialize.</param>
        /// <returns>An object of type T deserialized from the JSON string.</returns>
        public static T DeserializeObject<T>(string data)
        {
            using (var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(data)))
            {
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
        }
    }
}
