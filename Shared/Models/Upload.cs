using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using Windows.UI.Xaml.Controls;

namespace Shared.Models
{
    [DataContract]
    public struct UploadObject
    {
        public string id { get; set; }
        public string fileName { get; set; }
        public int size { get; set; }
        public string fileType { get; set; }
        public string sha256 { get; set; }
        public string preview { get; set; }
        public object metadata { get; set; }
    }

    [DataContract]
    public struct UploadRequest
    {
        [DataMember(IsRequired = true, Name = "info")]
        public Device info { get; set; }

        [DataMember(IsRequired = true, Name = "files")]
        public Dictionary<string, UploadObject> files { get; set; }
    }

    [DataContract]
    public struct UploadResponse
    {
        public string sessionId { get; set; }
        public Dictionary<string, string> files { get; set; }
    }
}
