#if WINDOWS_UWP
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System.Profile;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.ApplicationModel.Resources;
using System.Text;

namespace Shared.Platform
{
    /// <summary>
    /// UWP platform implementation
    /// </summary>
    public class UwpPlatform : IPlatformAbstraction
    {
        public string GetDeviceType()
        {
            return AnalyticsInfo.DeviceForm.EndsWith("Mobile") ? "mobile" : "desktop";
        }

        public string GetDeviceModel()
        {
            return "Universal Windows";
        }

        public async Task<IEnumerable<INetworkInterface>> GetNetworkInterfacesAsync()
        {
            var interfaces = new List<INetworkInterface>();
            
            var hostNames = NetworkInformation.GetHostNames();
            foreach (var hostName in hostNames.Where(h => h.IPInformation != null && h.Type == HostNameType.Ipv4))
            {
                interfaces.Add(new UwpNetworkInterface(hostName));
            }
            
            return await Task.FromResult(interfaces);
        }

        public IUdpSocket CreateUdpSocket()
        {
            return new UwpUdpSocket();
        }

        public IHttpServer CreateHttpServer()
        {
            return new UwpHttpServer();
        }

        public IHttpClient CreateHttpClient()
        {
            return new UwpHttpClient();
        }

        public IFilePicker GetFilePicker()
        {
            return new UwpFilePicker();
        }

        public IStorageManager GetStorageManager()
        {
            return new UwpStorageManager();
        }

        public ICryptographyProvider GetCryptographyProvider()
        {
            return new UwpCryptographyProvider();
        }

        public ILocalizationManager GetLocalizationManager()
        {
            return new UwpLocalizationManager();
        }

        public void RunOnUIThread(Action action)
        {
            var dispatcher = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher;
            if (dispatcher.HasThreadAccess)
            {
                action();
            }
            else
            {
                _ = dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => action());
            }
        }

        public Shared.Localization.IResourceProvider GetResourceProvider()
        {
            return new Shared.Localization.DefaultResourceProvider();
        }
    }

    /// <summary>
    /// UWP network interface implementation
    /// </summary>
    internal class UwpNetworkInterface : INetworkInterface
    {
        private readonly HostName _hostName;

        public UwpNetworkInterface(HostName hostName)
        {
            _hostName = hostName;
        }

        public string Name => _hostName.IPInformation?.NetworkAdapter?.NetworkAdapterId.ToString() ?? "Unknown";
        public string IpAddress => _hostName.CanonicalName;
        public bool IsConnected => true;
        public NetworkInterfaceType Type => GetNetworkType();

        private NetworkInterfaceType GetNetworkType()
        {
            var adapter = _hostName.IPInformation?.NetworkAdapter;
            if (adapter == null) return NetworkInterfaceType.Other;

            // Simplified type detection
            return NetworkInterfaceType.WiFi;
        }
    }

    /// <summary>
    /// UWP UDP socket implementation
    /// </summary>
    internal class UwpUdpSocket : IUdpSocket
    {
        private DatagramSocket _socket;
        private bool _disposed;

        public event EventHandler<UdpMessageReceivedEventArgs> MessageReceived;

        public async Task BindAsync(int port)
        {
            _socket = new DatagramSocket();
            _socket.MessageReceived += OnMessageReceived;
            await _socket.BindServiceNameAsync(port.ToString());
        }

        public async Task JoinMulticastGroupAsync(string address)
        {
            var hostName = new HostName(address);
            _socket.JoinMulticastGroup(hostName);
            await Task.CompletedTask;
        }

        public async Task SendAsync(byte[] data, string address, int port)
        {
            var hostName = new HostName(address);
            var outputStream = await _socket.GetOutputStreamAsync(hostName, port.ToString());
            var writer = new DataWriter(outputStream);
            writer.WriteBytes(data);
            await writer.StoreAsync();
        }

        private void OnMessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args)
        {
            try
            {
                var reader = args.GetDataReader();
                var data = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(data);

                MessageReceived?.Invoke(this, new UdpMessageReceivedEventArgs
                {
                    Data = data,
                    RemoteAddress = args.RemoteAddress.CanonicalName,
                    RemotePort = int.Parse(args.RemotePort)
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UDP message receive error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _socket?.Dispose();
            }
        }
    }

    /// <summary>
    /// UWP HTTP server implementation (simplified)
    /// </summary>
    internal class UwpHttpServer : IHttpServer
    {
        private StreamSocketListener _listener;
        private bool _isRunning;

        public event EventHandler<HttpRequestEventArgs> RequestReceived;
        public bool IsRunning => _isRunning;

        public async Task StartAsync(int port, bool useHttps = false)
        {
            _listener = new StreamSocketListener();
            _listener.ConnectionReceived += OnConnectionReceived;
            
            await _listener.BindServiceNameAsync(port.ToString());
            _isRunning = true;
        }

        public async Task StopAsync()
        {
            _isRunning = false;
            _listener?.Dispose();
            await Task.CompletedTask;
        }

        private async void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            try
            {
                // Simplified HTTP request parsing
                var request = new UwpHttpRequest(args.Socket);
                var response = new UwpHttpResponse(args.Socket);
                
                RequestReceived?.Invoke(this, new HttpRequestEventArgs
                {
                    Request = request,
                    Response = response
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HTTP connection error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _listener?.Dispose();
        }
    }

    /// <summary>
    /// UWP HTTP request implementation (simplified)
    /// </summary>
    internal class UwpHttpRequest : IHttpRequest
    {
        public string Method { get; set; } = "GET";
        public string Path { get; set; } = "/";
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> QueryParameters { get; set; } = new Dictionary<string, string>();
        public byte[] Body { get; set; } = new byte[0];
        public string RemoteAddress { get; set; }

        public UwpHttpRequest(StreamSocket socket)
        {
            RemoteAddress = socket.Information.RemoteAddress.CanonicalName;
            // TODO: Parse actual HTTP request
        }
    }

    /// <summary>
    /// UWP HTTP response implementation (simplified)
    /// </summary>
    internal class UwpHttpResponse : IHttpResponse
    {
        private readonly StreamSocket _socket;
        private readonly DataWriter _writer;

        public int StatusCode { get; set; } = 200;
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>();

        public UwpHttpResponse(StreamSocket socket)
        {
            _socket = socket;
            _writer = new DataWriter(socket.OutputStream);
        }

        public async Task WriteAsync(byte[] data)
        {
            _writer.WriteBytes(data);
            await _writer.StoreAsync();
        }

        public async Task WriteAsync(string text)
        {
            var data = Encoding.UTF8.GetBytes(text);
            await WriteAsync(data);
        }

        public async Task CompleteAsync()
        {
            await _writer.FlushAsync();
            _writer.Dispose();
            _socket.Dispose();
        }
    }

    /// <summary>
    /// UWP HTTP client implementation
    /// </summary>
    internal class UwpHttpClient : IHttpClient
    {
        private readonly Windows.Web.Http.HttpClient _httpClient;

        public UwpHttpClient()
        {
            _httpClient = new Windows.Web.Http.HttpClient();
        }

        public async Task<IHttpClientResponse> GetAsync(string url)
        {
            var response = await _httpClient.GetAsync(new Uri(url));
            var content = await response.Content.ReadAsBufferAsync();
            
            return new UwpHttpClientResponse
            {
                StatusCode = (int)response.StatusCode,
                Headers = response.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
                Content = content.ToArray(),
                IsSuccessStatusCode = response.IsSuccessStatusCode
            };
        }

        public async Task<IHttpClientResponse> PostAsync(string url, byte[] data, string contentType = "application/json")
        {
            var buffer = CryptographicBuffer.CreateFromByteArray(data);
            var content = new Windows.Web.Http.HttpBufferContent(buffer);
            content.Headers.ContentType = new Windows.Web.Http.Headers.HttpMediaTypeHeaderValue(contentType);
            
            var response = await _httpClient.PostAsync(new Uri(url), content);
            var responseContent = await response.Content.ReadAsBufferAsync();
            
            return new UwpHttpClientResponse
            {
                StatusCode = (int)response.StatusCode,
                Headers = response.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
                Content = responseContent.ToArray(),
                IsSuccessStatusCode = response.IsSuccessStatusCode
            };
        }

        public async Task<IHttpClientResponse> PostAsync(string url, string data, string contentType = "application/json")
        {
            return await PostAsync(url, Encoding.UTF8.GetBytes(data), contentType);
        }

        public void SetTimeout(TimeSpan timeout)
        {
            // UWP HttpClient doesn't have a direct timeout property
            // Would need to use CancellationToken for timeout
        }

        public void SetHeader(string name, string value)
        {
            _httpClient.DefaultRequestHeaders[name] = value;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    internal class UwpHttpClientResponse : IHttpClientResponse
    {
        public int StatusCode { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        public byte[] Content { get; set; }
        public string ContentAsString => Encoding.UTF8.GetString(Content);
        public bool IsSuccessStatusCode { get; set; }
    }

    /// <summary>
    /// UWP file picker implementation
    /// </summary>
    internal class UwpFilePicker : IFilePicker
    {
        private readonly FileOpenPicker _picker;

        public UwpFilePicker()
        {
            _picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
        }

        public async Task<IEnumerable<IStorageFile>> PickMultipleFilesAsync()
        {
            var files = await _picker.PickMultipleFilesAsync();
            return files.Select(f => new UwpStorageFile(f));
        }

        public async Task<IStorageFile> PickSingleFileAsync()
        {
            var file = await _picker.PickSingleFileAsync();
            return file != null ? new UwpStorageFile(file) : null;
        }

        public async Task<IStorageFolder> PickFolderAsync()
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            return folder != null ? new UwpStorageFolder(folder) : null;
        }

        public void SetFileTypeFilter(params string[] extensions)
        {
            _picker.FileTypeFilter.Clear();
            foreach (var ext in extensions)
            {
                _picker.FileTypeFilter.Add(ext.StartsWith(".") ? ext : "." + ext);
            }
        }
    }

    /// <summary>
    /// UWP storage file implementation
    /// </summary>
    internal class UwpStorageFile : IStorageFile
    {
        private readonly StorageFile _file;

        public UwpStorageFile(StorageFile file)
        {
            _file = file;
        }

        public string Name => _file.Name;
        public string Path => _file.Path;
        public long Size => (long)_file.GetBasicPropertiesAsync().GetAwaiter().GetResult().Size;
        public DateTime DateModified => _file.DateCreated.DateTime;
        public string ContentType => _file.ContentType;

        public async Task<byte[]> ReadAllBytesAsync()
        {
            var buffer = await FileIO.ReadBufferAsync(_file);
            return buffer.ToArray();
        }

        public async Task<System.IO.Stream> OpenReadAsync()
        {
            var stream = await _file.OpenReadAsync();
            return stream.AsStreamForRead();
        }

        public async Task<string> ComputeHashAsync(HashAlgorithmType algorithm)
        {
            var data = await ReadAllBytesAsync();
            var crypto = new UwpCryptographyProvider();
            return crypto.ComputeHash(data, algorithm);
        }
    }

    /// <summary>
    /// UWP storage folder implementation
    /// </summary>
    internal class UwpStorageFolder : IStorageFolder
    {
        private readonly StorageFolder _folder;

        public UwpStorageFolder(StorageFolder folder)
        {
            _folder = folder;
        }

        public string Name => _folder.Name;
        public string Path => _folder.Path;

        public async Task<IEnumerable<IStorageFile>> GetFilesAsync()
        {
            var files = await _folder.GetFilesAsync();
            return files.Select(f => new UwpStorageFile(f));
        }

        public async Task<IEnumerable<IStorageFolder>> GetFoldersAsync()
        {
            var folders = await _folder.GetFoldersAsync();
            return folders.Select(f => new UwpStorageFolder(f));
        }
    }

    /// <summary>
    /// UWP storage manager implementation
    /// </summary>
    internal class UwpStorageManager : IStorageManager
    {
        public async Task<IStorageFolder> GetDownloadsFolderAsync()
        {
            var folder = await DownloadsFolder.CreateFolderAsync("LocalTalk", CreationCollisionOption.OpenIfExists);
            return new UwpStorageFolder(folder);
        }

        public async Task<IStorageFolder> GetDocumentsFolderAsync()
        {
            var folder = KnownFolders.DocumentsLibrary;
            return new UwpStorageFolder(folder);
        }

        public async Task<IStorageFolder> GetPicturesFolderAsync()
        {
            var folder = KnownFolders.PicturesLibrary;
            return new UwpStorageFolder(folder);
        }

        public async Task<long> GetAvailableSpaceAsync()
        {
            try
            {
                var properties = await ApplicationData.Current.LocalFolder.GetBasicPropertiesAsync();
                // UWP doesn't provide direct access to available space
                // Return a reasonable default
                return 1024L * 1024L * 1024L; // 1GB
            }
            catch
            {
                return 1024L * 1024L * 100L; // 100MB fallback
            }
        }

        public async Task<IStorageFile> CreateFileAsync(string path, byte[] content)
        {
            var folder = ApplicationData.Current.LocalFolder;
            var file = await folder.CreateFileAsync(System.IO.Path.GetFileName(path), CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteBytesAsync(file, content);
            return new UwpStorageFile(file);
        }
    }

    /// <summary>
    /// UWP cryptography provider implementation
    /// </summary>
    internal class UwpCryptographyProvider : ICryptographyProvider
    {
        public string GenerateRandomString(int length)
        {
            var buffer = CryptographicBuffer.GenerateRandom((uint)(length * 4));
            var bytes = buffer.ToArray();

            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
            var result = new StringBuilder(length);

            for (int i = 0; i < length; i++)
            {
                var index = BitConverter.ToUInt32(bytes, i * 4) % (uint)chars.Length;
                result.Append(chars[(int)index]);
            }

            return result.ToString();
        }

        public byte[] GenerateRandomBytes(int length)
        {
            var buffer = CryptographicBuffer.GenerateRandom((uint)length);
            return buffer.ToArray();
        }

        public string ComputeHash(byte[] data, HashAlgorithmType algorithm)
        {
            string algorithmName;
            switch (algorithm)
            {
                case HashAlgorithmType.SHA256:
                    algorithmName = HashAlgorithmNames.Sha256;
                    break;
                case HashAlgorithmType.SHA1:
                    algorithmName = HashAlgorithmNames.Sha1;
                    break;
                case HashAlgorithmType.MD5:
                    algorithmName = HashAlgorithmNames.Md5;
                    break;
                default:
                    throw new ArgumentException("Unsupported hash algorithm");
            }

            var provider = HashAlgorithmProvider.OpenAlgorithm(algorithmName);
            var buffer = CryptographicBuffer.CreateFromByteArray(data);
            var hash = provider.HashData(buffer);
            return CryptographicBuffer.EncodeToHexString(hash).ToLowerInvariant();
        }

        public string ComputeHash(string data, HashAlgorithmType algorithm)
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            return ComputeHash(bytes, algorithm);
        }

        public ICertificate GenerateSelfSignedCertificate(string subjectName)
        {
            // UWP certificate generation would require more complex implementation
            // Return a mock certificate for now
            return new UwpCertificate(subjectName);
        }

        public string GetCertificateFingerprint(ICertificate certificate)
        {
            return ComputeHash(certificate.RawData, HashAlgorithmType.SHA256);
        }
    }

    /// <summary>
    /// UWP certificate implementation (mock)
    /// </summary>
    internal class UwpCertificate : ICertificate
    {
        public string Subject { get; }
        public string Thumbprint { get; }
        public DateTime NotBefore { get; }
        public DateTime NotAfter { get; }
        public byte[] RawData { get; }

        public UwpCertificate(string subject)
        {
            Subject = subject;
            NotBefore = DateTime.Now;
            NotAfter = DateTime.Now.AddYears(1);
            RawData = Encoding.UTF8.GetBytes($"MOCK_CERT_{subject}_{DateTime.Now.Ticks}");

            var provider = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha256);
            var buffer = CryptographicBuffer.CreateFromByteArray(RawData);
            var hash = provider.HashData(buffer);
            Thumbprint = CryptographicBuffer.EncodeToHexString(hash).ToLowerInvariant();
        }
    }

    /// <summary>
    /// UWP localization manager implementation
    /// </summary>
    internal class UwpLocalizationManager : ILocalizationManager
    {
        private readonly ResourceLoader _resourceLoader;
        private string _currentLanguage;

        public string CurrentLanguage => _currentLanguage ?? Windows.Globalization.ApplicationLanguages.Languages.FirstOrDefault() ?? "en-US";

        public IEnumerable<string> AvailableLanguages => Windows.Globalization.ApplicationLanguages.Languages;

        public UwpLocalizationManager()
        {
            _resourceLoader = ResourceLoader.GetForCurrentView();
            _currentLanguage = CurrentLanguage;
        }

        public string GetString(string key)
        {
            try
            {
                var value = _resourceLoader.GetString(key);
                return string.IsNullOrEmpty(value) ? key : value;
            }
            catch
            {
                return key;
            }
        }

        public string GetString(string key, params object[] args)
        {
            var format = GetString(key);
            try
            {
                return string.Format(format, args);
            }
            catch
            {
                return format;
            }
        }

        public void SetLanguage(string languageCode)
        {
            _currentLanguage = languageCode;
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = languageCode;
        }
    }
}
#endif
