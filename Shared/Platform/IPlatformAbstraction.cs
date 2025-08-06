using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Shared.Platform
{
    /// <summary>
    /// Platform abstraction interface to handle differences between Windows Phone and UWP
    /// </summary>
    public interface IPlatformAbstraction
    {
        /// <summary>
        /// Gets the device type (mobile, desktop, etc.)
        /// </summary>
        string GetDeviceType();

        /// <summary>
        /// Gets the device model name
        /// </summary>
        string GetDeviceModel();

        /// <summary>
        /// Gets available network interfaces
        /// </summary>
        Task<IEnumerable<INetworkInterface>> GetNetworkInterfacesAsync();

        /// <summary>
        /// Creates a UDP socket for multicast operations
        /// </summary>
        IUdpSocket CreateUdpSocket();

        /// <summary>
        /// Creates an HTTP server
        /// </summary>
        IHttpServer CreateHttpServer();

        /// <summary>
        /// Creates an HTTP client
        /// </summary>
        IHttpClient CreateHttpClient();

        /// <summary>
        /// Gets file picker implementation
        /// </summary>
        IFilePicker GetFilePicker();

        /// <summary>
        /// Gets storage manager implementation
        /// </summary>
        IStorageManager GetStorageManager();

        /// <summary>
        /// Gets cryptography provider
        /// </summary>
        ICryptographyProvider GetCryptographyProvider();

        /// <summary>
        /// Gets localization manager
        /// </summary>
        ILocalizationManager GetLocalizationManager();

        /// <summary>
        /// Gets network interface manager
        /// </summary>
        INetworkInterfaceManager GetNetworkInterfaceManager();

        /// <summary>
        /// Runs an action on the UI thread
        /// </summary>
        void RunOnUIThread(Action action);

        /// <summary>
        /// Gets the resource provider for localization
        /// </summary>
        Shared.Localization.IResourceProvider GetResourceProvider();
    }

    /// <summary>
    /// Network interface abstraction
    /// </summary>
    public interface INetworkInterface
    {
        string Name { get; }
        string IpAddress { get; }
        bool IsConnected { get; }
        NetworkInterfaceType Type { get; }
    }

    /// <summary>
    /// Network interface types
    /// </summary>
    public enum NetworkInterfaceType
    {
        Ethernet,
        WiFi,
        Cellular,
        Other
    }

    /// <summary>
    /// UDP socket abstraction
    /// </summary>
    public interface IUdpSocket : IDisposable
    {
        event EventHandler<UdpMessageReceivedEventArgs> MessageReceived;
        Task BindAsync(int port);
        Task JoinMulticastGroupAsync(string address);
        Task SendAsync(byte[] data, string address, int port);
    }

    public class UdpMessageReceivedEventArgs : EventArgs
    {
        public byte[] Data { get; set; }
        public string RemoteAddress { get; set; }
        public int RemotePort { get; set; }
    }

    /// <summary>
    /// HTTP server abstraction
    /// </summary>
    public interface IHttpServer : IDisposable
    {
        event EventHandler<HttpRequestEventArgs> RequestReceived;
        Task StartAsync(int port, bool useHttps = false);
        Task StopAsync();
        bool IsRunning { get; }
    }

    public class HttpRequestEventArgs : EventArgs
    {
        public IHttpRequest Request { get; set; }
        public IHttpResponse Response { get; set; }
    }

    /// <summary>
    /// HTTP request abstraction
    /// </summary>
    public interface IHttpRequest
    {
        string Method { get; }
        string Path { get; }
        Dictionary<string, string> Headers { get; }
        Dictionary<string, string> QueryParameters { get; }
        byte[] Body { get; }
        string RemoteAddress { get; }
    }

    /// <summary>
    /// HTTP response abstraction
    /// </summary>
    public interface IHttpResponse
    {
        int StatusCode { get; set; }
        Dictionary<string, string> Headers { get; }
        Task WriteAsync(byte[] data);
        Task WriteAsync(string text);
        Task CompleteAsync();
    }

    /// <summary>
    /// HTTP client abstraction
    /// </summary>
    public interface IHttpClient : IDisposable
    {
        Task<IHttpClientResponse> GetAsync(string url);
        Task<IHttpClientResponse> PostAsync(string url, byte[] data, string contentType = "application/json");
        Task<IHttpClientResponse> PostAsync(string url, string data, string contentType = "application/json");
        void SetTimeout(TimeSpan timeout);
        void SetHeader(string name, string value);
    }

    public interface IHttpClientResponse
    {
        int StatusCode { get; }
        Dictionary<string, string> Headers { get; }
        byte[] Content { get; }
        string ContentAsString { get; }
        bool IsSuccessStatusCode { get; }
    }

    /// <summary>
    /// File picker abstraction
    /// </summary>
    public interface IFilePicker
    {
        Task<IEnumerable<IStorageFile>> PickMultipleFilesAsync();
        Task<IStorageFile> PickSingleFileAsync();
        Task<IStorageFolder> PickFolderAsync();
        void SetFileTypeFilter(params string[] extensions);
    }

    /// <summary>
    /// Storage file abstraction
    /// </summary>
    public interface IStorageFile
    {
        string Name { get; }
        string Path { get; }
        long Size { get; }
        DateTime DateModified { get; }
        string ContentType { get; }
        Task<byte[]> ReadAllBytesAsync();
        Task<System.IO.Stream> OpenReadAsync();
        Task<string> ComputeHashAsync(HashAlgorithmType algorithm);
    }

    /// <summary>
    /// Storage folder abstraction
    /// </summary>
    public interface IStorageFolder
    {
        string Name { get; }
        string Path { get; }
        Task<IEnumerable<IStorageFile>> GetFilesAsync();
        Task<IEnumerable<IStorageFolder>> GetFoldersAsync();
    }

    /// <summary>
    /// Storage manager abstraction
    /// </summary>
    public interface IStorageManager
    {
        Task<IStorageFolder> GetDownloadsFolderAsync();
        Task<IStorageFolder> GetDocumentsFolderAsync();
        Task<IStorageFolder> GetPicturesFolderAsync();
        Task<long> GetAvailableSpaceAsync();
        Task<IStorageFile> CreateFileAsync(string path, byte[] content);
    }

    /// <summary>
    /// Cryptography provider abstraction
    /// </summary>
    public interface ICryptographyProvider
    {
        string GenerateRandomString(int length);
        byte[] GenerateRandomBytes(int length);
        string ComputeHash(byte[] data, HashAlgorithmType algorithm);
        string ComputeHash(string data, HashAlgorithmType algorithm);
        ICertificate GenerateSelfSignedCertificate(string subjectName);
        string GetCertificateFingerprint(ICertificate certificate);
    }

    public enum HashAlgorithmType
    {
        SHA256,
        SHA1,
        MD5
    }

    /// <summary>
    /// Certificate abstraction
    /// </summary>
    public interface ICertificate
    {
        string Subject { get; }
        string Thumbprint { get; }
        DateTime NotBefore { get; }
        DateTime NotAfter { get; }
        byte[] RawData { get; }
    }

    /// <summary>
    /// Localization manager abstraction
    /// </summary>
    public interface ILocalizationManager
    {
        string GetString(string key);
        string GetString(string key, params object[] args);
        void SetLanguage(string languageCode);
        string CurrentLanguage { get; }
        IEnumerable<string> AvailableLanguages { get; }
    }

    /// <summary>
    /// Network interface manager abstraction
    /// </summary>
    public interface INetworkInterfaceManager
    {
        Task<IEnumerable<INetworkInterface>> GetAllInterfacesAsync();
        Task<IEnumerable<INetworkInterface>> GetActiveInterfacesAsync();
        Task<INetworkInterface> GetPreferredInterfaceAsync();
        Task<bool> IsInterfaceAvailableAsync(string interfaceName);
        event EventHandler<NetworkInterfaceChangedEventArgs> InterfaceChanged;
        Task<NetworkBindingResult> BindToInterfaceAsync(string interfaceName, int port);
        Task<NetworkBindingResult> BindToAllInterfacesAsync(int port);
        Task UnbindFromInterfaceAsync(string interfaceName);
        Task UnbindFromAllInterfacesAsync();
    }

    /// <summary>
    /// Network interface changed event arguments
    /// </summary>
    public class NetworkInterfaceChangedEventArgs : EventArgs
    {
        public INetworkInterface Interface { get; set; }
        public NetworkInterfaceChangeType ChangeType { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Network interface change types
    /// </summary>
    public enum NetworkInterfaceChangeType
    {
        Added,
        Removed,
        StatusChanged,
        AddressChanged
    }

    /// <summary>
    /// Network binding result
    /// </summary>
    public class NetworkBindingResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public INetworkInterface BoundInterface { get; set; }
        public int BoundPort { get; set; }
        public string LocalEndpoint { get; set; }
    }
}
