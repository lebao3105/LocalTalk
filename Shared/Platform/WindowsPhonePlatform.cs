#if WINDOWS_PHONE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Security.Cryptography;
using System.IO;
using System.IO.IsolatedStorage;
using Microsoft.Phone.Info;
using System.Windows;

namespace Shared.Platform
{
    /// <summary>
    /// Windows Phone 8.x platform implementation
    /// </summary>
    public class WindowsPhonePlatform : IPlatformAbstraction
    {
        public string GetDeviceType()
        {
            return "mobile";
        }

        public string GetDeviceModel()
        {
            try
            {
                return DeviceStatus.DeviceName ?? "Windows Phone";
            }
            catch
            {
                return "Windows Phone";
            }
        }

        public async Task<IEnumerable<INetworkInterface>> GetNetworkInterfacesAsync()
        {
            return await Task.FromResult(new List<INetworkInterface>
            {
                new WindowsPhoneNetworkInterface()
            });
        }

        public IUdpSocket CreateUdpSocket()
        {
            return new WindowsPhoneUdpSocket();
        }

        public IHttpServer CreateHttpServer()
        {
            return new WindowsPhoneHttpServer();
        }

        public IHttpClient CreateHttpClient()
        {
            return new WindowsPhoneHttpClient();
        }

        public IFilePicker GetFilePicker()
        {
            return new WindowsPhoneFilePicker();
        }

        public IStorageManager GetStorageManager()
        {
            return new WindowsPhoneStorageManager();
        }

        public ICryptographyProvider GetCryptographyProvider()
        {
            return new WindowsPhoneCryptographyProvider();
        }

        public ILocalizationManager GetLocalizationManager()
        {
            return new WindowsPhoneLocalizationManager();
        }

        public void RunOnUIThread(Action action)
        {
            if (System.Windows.Deployment.Current.Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                System.Windows.Deployment.Current.Dispatcher.BeginInvoke(action);
            }
        }

        public Shared.Localization.IResourceProvider GetResourceProvider()
        {
            return new Shared.Localization.DefaultResourceProvider();
        }
    }

    /// <summary>
    /// Windows Phone network interface implementation
    /// </summary>
    internal class WindowsPhoneNetworkInterface : INetworkInterface
    {
        public string Name => "Default";
        public string IpAddress => GetLocalIPAddress();
        public bool IsConnected => true; // Simplified for WP8
        public NetworkInterfaceType Type => NetworkInterfaceType.WiFi;

        private string GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }
    }

    /// <summary>
    /// Windows Phone UDP socket implementation
    /// </summary>
    internal class WindowsPhoneUdpSocket : IUdpSocket
    {
        private UdpClient _udpClient;
        private bool _disposed;

        public event EventHandler<UdpMessageReceivedEventArgs> MessageReceived;

        public async Task BindAsync(int port)
        {
            _udpClient = new UdpClient(port);
            
            // Start listening for messages
            _ = Task.Run(async () =>
            {
                while (!_disposed)
                {
                    try
                    {
                        var result = await _udpClient.ReceiveAsync();
                        MessageReceived?.Invoke(this, new UdpMessageReceivedEventArgs
                        {
                            Data = result.Buffer,
                            RemoteAddress = result.RemoteEndPoint.Address.ToString(),
                            RemotePort = result.RemoteEndPoint.Port
                        });
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"UDP receive error: {ex.Message}");
                    }
                }
            });
        }

        public async Task JoinMulticastGroupAsync(string address)
        {
            var multicastAddress = IPAddress.Parse(address);
            _udpClient.JoinMulticastGroup(multicastAddress);
            await Task.CompletedTask;
        }

        public async Task SendAsync(byte[] data, string address, int port)
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(address), port);
            await _udpClient.SendAsync(data, data.Length, endpoint);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _udpClient?.Close();
                _udpClient?.Dispose();
            }
        }
    }

    /// <summary>
    /// Windows Phone HTTP server implementation (simplified)
    /// </summary>
    internal class WindowsPhoneHttpServer : IHttpServer
    {
        private HttpListener _listener;
        private bool _isRunning;

        public event EventHandler<HttpRequestEventArgs> RequestReceived;
        public bool IsRunning => _isRunning;

        public async Task StartAsync(int port, bool useHttps = false)
        {
            // Note: HttpListener is not available on Windows Phone 8.x
            // This is a placeholder implementation
            _isRunning = true;
            await Task.CompletedTask;
            
            // TODO: Implement alternative HTTP server for WP8
            throw new NotSupportedException("HTTP server not supported on Windows Phone 8.x");
        }

        public async Task StopAsync()
        {
            _isRunning = false;
            await Task.CompletedTask;
        }

        public void Dispose()
        {
            _listener?.Stop();
            _listener?.Close();
        }
    }

    /// <summary>
    /// Windows Phone HTTP client implementation
    /// </summary>
    internal class WindowsPhoneHttpClient : IHttpClient
    {
        private readonly System.Net.Http.HttpClient _httpClient;

        public WindowsPhoneHttpClient()
        {
            _httpClient = new System.Net.Http.HttpClient();
        }

        public async Task<IHttpClientResponse> GetAsync(string url)
        {
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsByteArrayAsync();
            
            return new WindowsPhoneHttpClientResponse
            {
                StatusCode = (int)response.StatusCode,
                Headers = response.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
                Content = content,
                IsSuccessStatusCode = response.IsSuccessStatusCode
            };
        }

        public async Task<IHttpClientResponse> PostAsync(string url, byte[] data, string contentType = "application/json")
        {
            var content = new System.Net.Http.ByteArrayContent(data);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            
            var response = await _httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsByteArrayAsync();
            
            return new WindowsPhoneHttpClientResponse
            {
                StatusCode = (int)response.StatusCode,
                Headers = response.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
                Content = responseContent,
                IsSuccessStatusCode = response.IsSuccessStatusCode
            };
        }

        public async Task<IHttpClientResponse> PostAsync(string url, string data, string contentType = "application/json")
        {
            return await PostAsync(url, Encoding.UTF8.GetBytes(data), contentType);
        }

        public void SetTimeout(TimeSpan timeout)
        {
            _httpClient.Timeout = timeout;
        }

        public void SetHeader(string name, string value)
        {
            _httpClient.DefaultRequestHeaders.Add(name, value);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    internal class WindowsPhoneHttpClientResponse : IHttpClientResponse
    {
        public int StatusCode { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        public byte[] Content { get; set; }
        public string ContentAsString => Encoding.UTF8.GetString(Content, 0, Content.Length);
        public bool IsSuccessStatusCode { get; set; }
    }

    /// <summary>
    /// Windows Phone file picker implementation (limited)
    /// </summary>
    internal class WindowsPhoneFilePicker : IFilePicker
    {
        public async Task<IEnumerable<IStorageFile>> PickMultipleFilesAsync()
        {
            // Windows Phone 8.x has limited file picker capabilities
            // This would require using Microsoft.Phone.Tasks.PhotoChooserTask or similar
            await Task.CompletedTask;
            throw new NotSupportedException("Multiple file picking not supported on Windows Phone 8.x");
        }

        public async Task<IStorageFile> PickSingleFileAsync()
        {
            await Task.CompletedTask;
            throw new NotSupportedException("File picking requires UI integration on Windows Phone 8.x");
        }

        public async Task<IStorageFolder> PickFolderAsync()
        {
            await Task.CompletedTask;
            throw new NotSupportedException("Folder picking not supported on Windows Phone 8.x");
        }

        public void SetFileTypeFilter(params string[] extensions)
        {
            // Not supported on WP8
        }
    }

    /// <summary>
    /// Windows Phone storage manager implementation
    /// </summary>
    internal class WindowsPhoneStorageManager : IStorageManager
    {
        public async Task<IStorageFolder> GetDownloadsFolderAsync()
        {
            await Task.CompletedTask;
            return new WindowsPhoneStorageFolder("Downloads", "/Downloads");
        }

        public async Task<IStorageFolder> GetDocumentsFolderAsync()
        {
            await Task.CompletedTask;
            return new WindowsPhoneStorageFolder("Documents", "/Documents");
        }

        public async Task<IStorageFolder> GetPicturesFolderAsync()
        {
            await Task.CompletedTask;
            return new WindowsPhoneStorageFolder("Pictures", "/Pictures");
        }

        public async Task<long> GetAvailableSpaceAsync()
        {
            await Task.CompletedTask;
            try
            {
                using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    return store.AvailableFreeSpace;
                }
            }
            catch
            {
                return 1024 * 1024 * 100; // 100MB fallback
            }
        }

        public async Task<IStorageFile> CreateFileAsync(string path, byte[] content)
        {
            await Task.CompletedTask;
            using (var store = IsolatedStorageFile.GetUserStoreForApplication())
            {
                using (var stream = store.CreateFile(path))
                {
                    await stream.WriteAsync(content, 0, content.Length);
                }
            }
            return new WindowsPhoneStorageFile(Path.GetFileName(path), path, content.Length);
        }
    }

    /// <summary>
    /// Windows Phone storage file implementation
    /// </summary>
    internal class WindowsPhoneStorageFile : IStorageFile
    {
        public string Name { get; }
        public string Path { get; }
        public long Size { get; }
        public DateTime DateModified { get; }
        public string ContentType { get; }

        public WindowsPhoneStorageFile(string name, string path, long size)
        {
            Name = name;
            Path = path;
            Size = size;
            DateModified = DateTime.Now;
            ContentType = GetContentType(name);
        }

        private string GetContentType(string fileName)
        {
            var extension = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".png":
                    return "image/png";
                case ".txt":
                    return "text/plain";
                case ".pdf":
                    return "application/pdf";
                default:
                    return "application/octet-stream";
            }
        }

        public async Task<byte[]> ReadAllBytesAsync()
        {
            using (var store = IsolatedStorageFile.GetUserStoreForApplication())
            {
                using (var stream = store.OpenFile(Path, FileMode.Open))
                {
                    var buffer = new byte[stream.Length];
                    await stream.ReadAsync(buffer, 0, buffer.Length);
                    return buffer;
                }
            }
        }

        public async Task<Stream> OpenReadAsync()
        {
            await Task.CompletedTask;
            var store = IsolatedStorageFile.GetUserStoreForApplication();
            return store.OpenFile(Path, FileMode.Open);
        }

        public async Task<string> ComputeHashAsync(HashAlgorithmType algorithm)
        {
            var data = await ReadAllBytesAsync();
            var crypto = new WindowsPhoneCryptographyProvider();
            return crypto.ComputeHash(data, algorithm);
        }
    }

    /// <summary>
    /// Windows Phone storage folder implementation
    /// </summary>
    internal class WindowsPhoneStorageFolder : IStorageFolder
    {
        public string Name { get; }
        public string Path { get; }

        public WindowsPhoneStorageFolder(string name, string path)
        {
            Name = name;
            Path = path;
        }

        public async Task<IEnumerable<IStorageFile>> GetFilesAsync()
        {
            await Task.CompletedTask;
            var files = new List<IStorageFile>();

            try
            {
                using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    var fileNames = store.GetFileNames(Path + "/*");
                    foreach (var fileName in fileNames)
                    {
                        var filePath = System.IO.Path.Combine(Path, fileName);
                        using (var stream = store.OpenFile(filePath, FileMode.Open))
                        {
                            files.Add(new WindowsPhoneStorageFile(fileName, filePath, stream.Length));
                        }
                    }
                }
            }
            catch { }

            return files;
        }

        public async Task<IEnumerable<IStorageFolder>> GetFoldersAsync()
        {
            await Task.CompletedTask;
            var folders = new List<IStorageFolder>();

            try
            {
                using (var store = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    var directoryNames = store.GetDirectoryNames(Path + "/*");
                    foreach (var dirName in directoryNames)
                    {
                        var dirPath = System.IO.Path.Combine(Path, dirName);
                        folders.Add(new WindowsPhoneStorageFolder(dirName, dirPath));
                    }
                }
            }
            catch { }

            return folders;
        }
    }

    /// <summary>
    /// Windows Phone cryptography provider implementation
    /// </summary>
    internal class WindowsPhoneCryptographyProvider : ICryptographyProvider
    {
        private static readonly Random _random = new Random();

        public string GenerateRandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
            var result = new StringBuilder(length);

            for (int i = 0; i < length; i++)
            {
                result.Append(chars[_random.Next(chars.Length)]);
            }

            return result.ToString();
        }

        public byte[] GenerateRandomBytes(int length)
        {
            var buffer = new byte[length];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(buffer);
            }
            return buffer;
        }

        public string ComputeHash(byte[] data, HashAlgorithmType algorithm)
        {
            HashAlgorithm hashAlgorithm;

            switch (algorithm)
            {
                case HashAlgorithmType.SHA256:
                    hashAlgorithm = SHA256.Create();
                    break;
                case HashAlgorithmType.SHA1:
                    hashAlgorithm = SHA1.Create();
                    break;
                case HashAlgorithmType.MD5:
                    hashAlgorithm = MD5.Create();
                    break;
                default:
                    throw new ArgumentException("Unsupported hash algorithm");
            }

            using (hashAlgorithm)
            {
                var hash = hashAlgorithm.ComputeHash(data);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        public string ComputeHash(string data, HashAlgorithmType algorithm)
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            return ComputeHash(bytes, algorithm);
        }

        public ICertificate GenerateSelfSignedCertificate(string subjectName)
        {
            // Self-signed certificate generation is not supported on Windows Phone 8.x
            // Return a mock certificate for compatibility
            return new WindowsPhoneCertificate(subjectName);
        }

        public string GetCertificateFingerprint(ICertificate certificate)
        {
            return ComputeHash(certificate.RawData, HashAlgorithmType.SHA256);
        }
    }

    /// <summary>
    /// Windows Phone certificate implementation (mock)
    /// </summary>
    internal class WindowsPhoneCertificate : ICertificate
    {
        public string Subject { get; }
        public string Thumbprint { get; }
        public DateTime NotBefore { get; }
        public DateTime NotAfter { get; }
        public byte[] RawData { get; }

        public WindowsPhoneCertificate(string subject)
        {
            Subject = subject;
            NotBefore = DateTime.Now;
            NotAfter = DateTime.Now.AddYears(1);
            RawData = Encoding.UTF8.GetBytes($"MOCK_CERT_{subject}_{DateTime.Now.Ticks}");

            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(RawData);
                Thumbprint = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    /// <summary>
    /// Windows Phone localization manager implementation
    /// </summary>
    internal class WindowsPhoneLocalizationManager : ILocalizationManager
    {
        private readonly Dictionary<string, string> _strings = new Dictionary<string, string>();
        private string _currentLanguage = "en-US";

        public string CurrentLanguage => _currentLanguage;

        public IEnumerable<string> AvailableLanguages => new[] { "en-US" };

        public WindowsPhoneLocalizationManager()
        {
            LoadDefaultStrings();
        }

        private void LoadDefaultStrings()
        {
            _strings["app_name"] = "LocalTalk";
            _strings["welcome"] = "Welcome";
            _strings["send"] = "Send";
            _strings["receive"] = "Receive";
            _strings["settings"] = "Settings";
            _strings["about"] = "About";
        }

        public string GetString(string key)
        {
            return _strings.TryGetValue(key, out var value) ? value : key;
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
            // TODO: Load language-specific resources
        }
    }
}
#endif
