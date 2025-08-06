using System;
using System.Runtime.Serialization;
using Windows.UI.Xaml.Controls;

namespace Shared.Models
{
    /// <summary>
    /// Represents a device in the LocalTalk network with its identification and connection properties.
    /// This struct implements the LocalSend protocol device specification.
    /// </summary>
    [DataContract]
    public struct Device : IEquatable<Device>
    {
        /// <summary>
        /// Gets or sets the human-readable name/alias of the device.
        /// </summary>
        [DataMember(IsRequired = true, Name = "alias")]
        public string Alias { get; set; }

        /// <summary>
        /// Gets or sets the protocol version supported by the device.
        /// </summary>
        [DataMember(IsRequired = true, Name = "version")]
        public double Version { get; set; }

        /// <summary>
        /// Gets or sets the device model identifier (e.g., "iPhone 12", "Windows PC").
        /// </summary>
        [DataMember(IsRequired = false, Name = "deviceModel")]
        public string DeviceModel { get; set; }

        /// <summary>
        /// Gets or sets the device type category (mobile, desktop, web, headless, server).
        /// </summary>
        [DataMember(IsRequired = false, Name = "deviceType")]
        public string DeviceType { get; set; }

        /// <summary>
        /// Gets or sets the unique fingerprint identifier for the device.
        /// </summary>
        [DataMember(IsRequired = true, Name = "fingerprint")]
        public string Fingerprint { get; set; }

        /// <summary>
        /// Gets or sets the network port number for communication.
        /// </summary>
        [DataMember(IsRequired = true, Name = "port")]
        public int Port { get; set; }

        /// <summary>
        /// Gets or sets the communication protocol identifier.
        /// </summary>
        [DataMember(IsRequired = true, Name = "protocol")]
        public string Protocol { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the device supports file downloads.
        /// </summary>
        [DataMember(IsRequired = true, Name = "download")]
        public bool Download { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the device announces its presence.
        /// </summary>
        [DataMember(IsRequired = false, Name = "announce")]
        public bool Announce { get; set; }

        /// <summary>
        /// Gets the appropriate UI symbol/glyph for the device based on its type.
        /// </summary>
        [IgnoreDataMember]
        public Symbol Glyph
        {
            get
            {
                switch (DeviceType)
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

        /// <summary>
        /// Determines whether the specified object is equal to the current device.
        /// </summary>
        /// <param name="obj">The object to compare with the current device.</param>
        /// <returns>true if the specified object is equal to the current device; otherwise, false.</returns>
        public override bool Equals(object obj) => obj is Device && Equals((Device)obj);

        /// <summary>
        /// Indicates whether the current device is equal to another device.
        /// </summary>
        /// <param name="other">A device to compare with this device.</param>
        /// <returns>true if the current device is equal to the other parameter; otherwise, false.</returns>
        public bool Equals(Device other)
        {
            return Alias == other.Alias &&
                   DeviceModel == other.DeviceModel &&
                   DeviceType == other.DeviceType &&
                   Fingerprint == other.Fingerprint;
        }

        /// <summary>
        /// Returns a hash code for the current device.
        /// </summary>
        /// <returns>A hash code for the current device.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(Alias, DeviceModel, DeviceType, Fingerprint);
        }

        /// <summary>
        /// Determines whether two device instances are equal.
        /// </summary>
        /// <param name="device1">The first device to compare.</param>
        /// <param name="device2">The second device to compare.</param>
        /// <returns>true if the devices are equal; otherwise, false.</returns>
        public static bool operator ==(Device device1, Device device2) => device1.Equals(device2);

        /// <summary>
        /// Determines whether two device instances are not equal.
        /// </summary>
        /// <param name="device1">The first device to compare.</param>
        /// <param name="device2">The second device to compare.</param>
        /// <returns>true if the devices are not equal; otherwise, false.</returns>
        public static bool operator !=(Device device1, Device device2) => !(device1 == device2);
    }
}