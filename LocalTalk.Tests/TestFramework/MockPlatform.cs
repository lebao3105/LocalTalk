using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shared.Platform;
using Shared.FileSystem;

namespace LocalTalk.Tests.TestFramework
{
    /// <summary>
    /// Mock platform abstraction for testing
    /// </summary>
    public class MockPlatform : IPlatformAbstraction
    {
        private IFilePicker _filePicker;
        private IHttpServer _httpServer;
        private bool _supportsFilePickers = true;
        private bool _supportsHttpServer = true;
        private bool _supportsMulticastUdp = true;
        private bool _supportsCertificateGeneration = true;

        public void SetFilePicker(IFilePicker filePicker)
        {
            _filePicker = filePicker;
        }

        public void SetHttpServer(IHttpServer httpServer)
        {
            _httpServer = httpServer;
        }

        public void SetFilePickerSupport(bool supported)
        {
            _supportsFilePickers = supported;
        }

        public void SetHttpServerSupport(bool supported)
        {
            _supportsHttpServer = supported;
        }

        public void SetMulticastUdpSupport(bool supported)
        {
            _supportsMulticastUdp = supported;
        }

        public void SetCertificateGenerationSupport(bool supported)
        {
            _supportsCertificateGeneration = supported;
        }

        public IFilePicker GetFilePicker()
        {
            return _filePicker ?? new MockFilePicker();
        }

        public IUdpSocket CreateUdpSocket()
        {
            return new MockUdpSocket();
        }

        public IHttpServer CreateHttpServer()
        {
            return _httpServer ?? new MockHttpServer();
        }

        public IHttpListener CreateHttpListener()
        {
            return new MockHttpListener();
        }

        public Task<string> GetDeviceNameAsync()
        {
            return Task.FromResult("MockDevice");
        }

        public Task<string> GetDeviceIdAsync()
        {
            return Task.FromResult("mock-device-id");
        }

        public Task<IEnumerable<NetworkInterface>> GetNetworkInterfacesAsync()
        {
            return Task.FromResult<IEnumerable<NetworkInterface>>(new List<NetworkInterface>
            {
                new MockNetworkInterface("WiFi", "192.168.1.100", true)
            });
        }

        public Task<bool> IsNetworkAvailableAsync()
        {
            return Task.FromResult(true);
        }

        public Task ShowNotificationAsync(string title, string message)
        {
            return Task.CompletedTask;
        }

        public Task<bool> RequestPermissionAsync(string permission)
        {
            return Task.FromResult(true);
        }

        public Task<string> GetAppVersionAsync()
        {
            return Task.FromResult("1.0.0-test");
        }

        public Task<PlatformInfo> GetPlatformInfoAsync()
        {
            return Task.FromResult(new PlatformInfo
            {
                Platform = "Test",
                Version = "1.0",
                Architecture = "x64",
                DeviceType = "Desktop"
            });
        }
    }

    /// <summary>
    /// Mock file picker for testing
    /// </summary>
    public class MockFilePicker : IFilePicker
    {
        private IStorageFile _singleFileResult;
        private IEnumerable<IStorageFile> _multipleFilesResult;
        private IStorageFolder _folderResult;
        private Exception _exception;
        private string[] _appliedFilters;

        public string[] AppliedFilters => _appliedFilters;

        public void SetSingleFileResult(IStorageFile file)
        {
            _singleFileResult = file;
        }

        public void SetMultipleFilesResult(IEnumerable<IStorageFile> files)
        {
            _multipleFilesResult = files;
        }

        public void SetFolderResult(IStorageFolder folder)
        {
            _folderResult = folder;
        }

        public void SetException(Exception exception)
        {
            _exception = exception;
        }

        public Task<IStorageFile> PickSingleFileAsync()
        {
            if (_exception != null)
                throw _exception;
            return Task.FromResult(_singleFileResult);
        }

        public Task<IEnumerable<IStorageFile>> PickMultipleFilesAsync()
        {
            if (_exception != null)
                throw _exception;
            return Task.FromResult(_multipleFilesResult);
        }

        public Task<IStorageFolder> PickFolderAsync()
        {
            if (_exception != null)
                throw _exception;
            return Task.FromResult(_folderResult);
        }

        public void SetFileTypeFilter(params string[] extensions)
        {
            _appliedFilters = extensions;
        }
    }

    /// <summary>
    /// Mock storage file for testing
    /// </summary>
    public class MockStorageFile : IStorageFile
    {
        public string Name { get; }
        public string Path { get; }
        public long Size { get; }
        public DateTime DateModified { get; }
        public string ContentType { get; }

        private readonly byte[] _content;

        public MockStorageFile(string name, string path, long size, string contentType = "text/plain")
        {
            Name = name;
            Path = path;
            Size = size;
            DateModified = DateTime.Now;
            ContentType = contentType;
            _content = new byte[size];
        }

        public Task<byte[]> ReadAllBytesAsync()
        {
            return Task.FromResult(_content);
        }

        public Task<System.IO.Stream> OpenReadAsync()
        {
            return Task.FromResult<System.IO.Stream>(new System.IO.MemoryStream(_content));
        }

        public Task<string> ComputeHashAsync(HashAlgorithmType algorithm)
        {
            return Task.FromResult("mock-hash-" + algorithm.ToString().ToLower());
        }
    }

    /// <summary>
    /// Mock storage folder for testing
    /// </summary>
    public class MockStorageFolder : IStorageFolder
    {
        public string Name { get; }
        public string Path { get; }

        public MockStorageFolder(string name, string path)
        {
            Name = name;
            Path = path;
        }

        public Task<IEnumerable<IStorageFile>> GetFilesAsync()
        {
            return Task.FromResult<IEnumerable<IStorageFile>>(new List<IStorageFile>());
        }

        public Task<IEnumerable<IStorageFolder>> GetFoldersAsync()
        {
            return Task.FromResult<IEnumerable<IStorageFolder>>(new List<IStorageFolder>());
        }
    }

    /// <summary>
    /// Mock security analyzer for testing
    /// </summary>
    public class MockSecurityAnalyzer : FileSystemSecurityAnalyzer
    {
        private SecurityLevel _securityLevel = SecurityLevel.Low;
        private List<string> _securityThreats = new List<string>();
        private Func<string, FileSystemAnalysisResult> _customAnalysis;

        public void SetAnalysisResult(SecurityLevel level, List<string> threats)
        {
            _securityLevel = level;
            _securityThreats = threats;
        }

        public void SetCustomAnalysis(Func<string, FileSystemAnalysisResult> analysis)
        {
            _customAnalysis = analysis;
        }

        public override Task<FileSystemAnalysisResult> AnalyzePathAsync(string path)
        {
            if (_customAnalysis != null)
            {
                return Task.FromResult(_customAnalysis(path));
            }

            return Task.FromResult(new FileSystemAnalysisResult
            {
                SecurityLevel = _securityLevel,
                SecurityThreats = new List<string>(_securityThreats),
                AnalyzedPath = path,
                AnalysisTimestamp = DateTime.Now
            });
        }
    }

    /// <summary>
    /// Mock UDP socket for testing
    /// </summary>
    public class MockUdpSocket : IUdpSocket
    {
        public event EventHandler<UdpMessageReceivedEventArgs> MessageReceived;

        public Task BindAsync(int port)
        {
            return Task.CompletedTask;
        }

        public Task JoinMulticastGroupAsync(string address)
        {
            return Task.CompletedTask;
        }

        public Task SendAsync(byte[] data, string address, int port)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }

        public void SimulateMessageReceived(byte[] data, string address, int port)
        {
            MessageReceived?.Invoke(this, new UdpMessageReceivedEventArgs
            {
                Data = data,
                RemoteAddress = address,
                RemotePort = port
            });
        }
    }

    /// <summary>
    /// Mock HTTP listener for testing
    /// </summary>
    public class MockHttpListener : IHttpListener
    {
        public event EventHandler<HttpRequestEventArgs> RequestReceived;

        public Task StartAsync(int port)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }

        public void SimulateRequest(string method, string url, Dictionary<string, string> headers = null, byte[] body = null)
        {
            RequestReceived?.Invoke(this, new HttpRequestEventArgs
            {
                Method = method,
                Url = url,
                Headers = headers ?? new Dictionary<string, string>(),
                Body = body
            });
        }
    }

    /// <summary>
    /// Mock network interface for testing
    /// </summary>
    public class MockNetworkInterface : NetworkInterface
    {
        public MockNetworkInterface(string name, string ipAddress, bool isConnected)
        {
            Name = name;
            IPAddress = ipAddress;
            IsConnected = isConnected;
        }
    }

    /// <summary>
    /// Mock HTTP server for testing
    /// </summary>
    public class MockHttpServer : IHttpServer
    {
        public event EventHandler<HttpRequestEventArgs> RequestReceived;

        public bool IsStarted { get; private set; }
        public int Port { get; private set; }
        public bool UseHttps { get; private set; }

        private Exception _startException;

        public void SetStartException(Exception exception)
        {
            _startException = exception;
        }

        public Task StartAsync(int port, bool useHttps = false)
        {
            if (_startException != null)
                throw _startException;

            Port = port;
            UseHttps = useHttps;
            IsStarted = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsStarted = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            IsStarted = false;
        }

        public void SimulateRequest(MockHttpRequest request, MockHttpResponse response)
        {
            var args = new HttpRequestEventArgs
            {
                Request = request,
                Response = response
            };
            RequestReceived?.Invoke(this, args);
        }
    }

    /// <summary>
    /// Mock HTTP request for testing
    /// </summary>
    public class MockHttpRequest : IHttpRequest
    {
        public string Method { get; set; }
        public string Path { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        public byte[] Body { get; set; } = new byte[0];
        public string RemoteAddress { get; set; }
        public string QueryString { get; set; }
    }

    /// <summary>
    /// Mock HTTP response for testing
    /// </summary>
    public class MockHttpResponse : IHttpResponse
    {
        public int StatusCode { get; set; } = 200;
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        public byte[] Body { get; private set; } = new byte[0];
        public bool IsCompleted { get; private set; }

        public Task WriteAsync(string content)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            Body = bytes;
            return Task.CompletedTask;
        }

        public Task WriteAsync(byte[] data)
        {
            Body = data;
            return Task.CompletedTask;
        }

        public Task CompleteAsync()
        {
            IsCompleted = true;
            return Task.CompletedTask;
        }
    }
}
