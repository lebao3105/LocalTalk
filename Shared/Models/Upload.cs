using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using Windows.UI.Xaml.Controls;

namespace LocalTalk.Shared.Models
{
    [DataContract]
    public struct UploadObject
    {
        [DataMember(IsRequired = true, Name = "id")]
        public string id { get; set; }

        [DataMember(IsRequired = true, Name = "fileName")]
        public string fileName { get; set; }

        [DataMember(IsRequired = true, Name = "size")]
        public int size { get; set; }

        [DataMember(IsRequired = true, Name = "fileType")]
        public string fileType { get; set; }

        [DataMember(IsRequired = false, Name = "sha256")]
        public string sha256 { get; set; }

        [DataMember(IsRequired = false, Name = "preview")]
        public string preview { get; set; }

        [DataMember(IsRequired = false, Name = "metadata")]
        public Dictionary<string, string> metadata { get; set; }
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
        [DataMember(IsRequired = true, Name = "sessionId")]
        public string sessionId { get; set; }

        [DataMember(IsRequired = true, Name = "files")]
        public Dictionary<string, string> files { get; set; }
    }
}
