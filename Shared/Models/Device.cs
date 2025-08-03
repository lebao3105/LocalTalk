using System;
using System.Runtime.Serialization;
using Windows.UI.Xaml.Controls;

namespace Shared.Models
{
    [DataContract]
    public struct Device : IEquatable<Device>
    {
        [DataMember(IsRequired = true, Name = "alias")]
        public string alias { get; set; }

        [DataMember(IsRequired = true, Name = "version")]
        public double version { get; set; }

        [DataMember(IsRequired = false, Name = "deviceModel")]
        public string deviceModel { get; set; }

        [DataMember(IsRequired = false, Name = "deviceType")]
        public string deviceType { get; set; }

        [DataMember(IsRequired = true, Name = "fingerprint")]
        public string fingerprint { get; set; }

        [DataMember(IsRequired = true, Name = "port")]
        public int port { get; set; }

        [DataMember(IsRequired = true, Name = "protocol")]
        public string protocol { get; set; }

        [DataMember(IsRequired = true, Name = "download")]
        public bool download { get; set; }

        [DataMember(IsRequired = false, Name = "announce")]
        public bool announce { get; set; }

        [IgnoreDataMember]
        public Symbol glyph
        {
            get
            {
                switch (deviceType)
                {
                    case "mobile": return Symbol.CellPhone;

                    case "desktop": return Symbol.XboxOneConsole;

                    case "web": return Symbol.Globe;

                    // Headless = program w/o GUI running on terminal
                    // (according to LocalSend documentation itself).
                    // That's why I use the keyboard symbol for this
                    case "headless": return Symbol.Keyboard;

                    case "server": return Symbol.MapDrive;

                    default: return Symbol.Help;
                }
            }
        }

        public override bool Equals(object obj) => obj is Device && Equals((Device)obj);

        public bool Equals(Device other)
        {
            return alias == other.alias &&
                   deviceModel == other.deviceModel &&
                   deviceType == other.deviceType &&
                   fingerprint == other.fingerprint;
        }

        public static bool operator ==(Device device1, Device device2) => device1.Equals(device2);

        public static bool operator !=(Device device1, Device device2) => !(device1 == device2);
    }
}