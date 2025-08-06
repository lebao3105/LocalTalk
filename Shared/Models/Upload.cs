using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using Windows.UI.Xaml.Controls;

namespace Shared.Models
{
    /// <summary>
    /// Represents a file object in an upload request with metadata and validation information
    /// </summary>
    [DataContract]
    public struct UploadObject
    {
        /// <summary>
        /// Gets or sets the unique identifier for this file in the upload session
        /// </summary>
        [DataMember(IsRequired = true, Name = "id")]
        public string id { get; set; }

        /// <summary>
        /// Gets or sets the original filename of the uploaded file
        /// </summary>
        [DataMember(IsRequired = true, Name = "fileName")]
        public string fileName { get; set; }

        /// <summary>
        /// Gets or sets the file size in bytes
        /// </summary>
        [DataMember(IsRequired = true, Name = "size")]
        public int size { get; set; }

        /// <summary>
        /// Gets or sets the MIME type of the file (e.g., "image/jpeg", "text/plain")
        /// </summary>
        [DataMember(IsRequired = true, Name = "fileType")]
        public string fileType { get; set; }

        /// <summary>
        /// Gets or sets the SHA256 hash of the file for integrity verification
        /// </summary>
        [DataMember(IsRequired = false, Name = "sha256")]
        public string sha256 { get; set; }

        /// <summary>
        /// Gets or sets the base64-encoded preview/thumbnail of the file (for images)
        /// </summary>
        [DataMember(IsRequired = false, Name = "preview")]
        public string preview { get; set; }

        /// <summary>
        /// Gets or sets additional metadata key-value pairs for the file
        /// </summary>
        [DataMember(IsRequired = false, Name = "metadata")]
        public Dictionary<string, string> metadata { get; set; }
    }

    /// <summary>
    /// Represents an upload request containing device information and file details
    /// </summary>
    [DataContract]
    public struct UploadRequest
    {
        /// <summary>
        /// Gets or sets the device information of the sender
        /// </summary>
        [DataMember(IsRequired = true, Name = "info")]
        public Device info { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of files to be uploaded, keyed by file ID
        /// </summary>
        [DataMember(IsRequired = true, Name = "files")]
        public Dictionary<string, UploadObject> files { get; set; }
    }

    /// <summary>
    /// Represents the response to an upload request indicating acceptance status
    /// </summary>
    [DataContract]
    public struct UploadResponse
    {
        /// <summary>
        /// Gets or sets the unique session identifier for this upload transaction
        /// </summary>
        [DataMember(IsRequired = true, Name = "sessionId")]
        public string sessionId { get; set; }

        /// <summary>
        /// Gets or sets the acceptance status for each file, keyed by file ID
        /// </summary>
        [DataMember(IsRequired = true, Name = "files")]
        public Dictionary<string, string> files { get; set; }
    }
}
