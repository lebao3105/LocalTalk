using Shared.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Controls;

namespace Shared
{
    public class LocalSendProtocol
    {
        public static LocalSendProtocol Instance { get; private set; }
        public static readonly ADevice ThisDevice = new ADevice
        {
            alias = Settings.DeviceName,
            version = 2.0,
            deviceModel = Settings.DeviceModel,
#if WINDOWS_UWP
            deviceType = Windows.System.Profile.AnalyticsInfo.DeviceForm.EndsWith("Mobile") ? "mobile" : "desktop",
#else
            deviceType = "mobile",
#endif
            fingerprint = GetUniqueKey(30), // TODO: Cert hash w/ HTTPS enabled + String length
            port = Settings.Port,
            protocol = "https",
            download = true,
            announce = true
        };
        public static readonly ObservableCollection<ADevice> Devices
            = new ObservableCollection<ADevice>();

        private DatagramSocket DatagramSocket;

        public LocalSendProtocol()
        {
            Instance = this;
        }

        public async Task Start()
        {
            //foreach (HostName localHostName in NetworkInformation.GetHostNames())
            //{
            //    if (localHostName.IPInformation != null &&
            //        localHostName.Type == HostNameType.Ipv4)
            //    {

            //    }
            //}
            DatagramSocket = new DatagramSocket();
            DatagramSocket.MessageReceived += DatagramSocket_MessageReceived;

            await DatagramSocket.BindServiceNameAsync(Settings.Port.ToString());
            DatagramSocket.JoinMulticastGroup(new HostName(Settings.Address));

            DataWriter writer = new DataWriter(await DatagramSocket.GetOutputStreamAsync(new HostName(Settings.Address), Settings.Port.ToString()));
            writer.WriteString(Internet.SerializeObject(ThisDevice));
            await writer.StoreAsync();
        }

        private async void DatagramSocket_MessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args)
        {
            var t = args.GetDataReader();
            var read = t.ReadString(t.UnconsumedBufferLength);
            System.Diagnostics.Debug.WriteLine(read);

            try
            {
                ADevice dev = Internet.DeserializeObject<ADevice>(read);
                // The fingerprint is used to...
                if (!Devices.Any(elm => elm.Equals(dev)) && // avoid re-discovery...
                    !dev.Equals(ThisDevice)) // ...and avoid self-discovery
                    Devices.Add(dev);
            }
            catch (Exception ex)
            {
                // ....
                System.Diagnostics.Debug.WriteLine(ex.ToString());
            }
        }

        public static string GetUniqueKey(int size)
        {
            char[] chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890".ToCharArray();
            byte[] data = new byte[4 * size];

#if WINDOWS_UWP
            using (var crypto = RandomNumberGenerator.Create())
#endif
            {
#if WINDOWS_PHONE
                var crypto = new RNGCryptoServiceProvider();
#endif
                crypto.GetBytes(data);
            }

            StringBuilder result = new StringBuilder(size);
            for (int i = 0; i < size; i++)
            {
                var rnd = BitConverter.ToUInt32(data, i * 4);
                var idx = rnd % chars.Length;

                result.Append(chars[idx]);
            }

            return result.ToString();
        }
    }
}
