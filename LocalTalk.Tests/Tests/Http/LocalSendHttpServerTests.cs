using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LocalTalk.Tests.TestFramework;
using Shared.Http;
using Shared.Platform;
using Shared.Security;

namespace LocalTalk.Tests.Tests.Http
{
    [TestClass]
    public class LocalSendHttpServerTests
    {
        private LocalSendHttpServer _server;
        private MockHttpServer _mockHttpServer;
        private MockPlatform _mockPlatform;

        [TestInitialize]
        public void Setup()
        {
            _mockPlatform = new MockPlatform();
            _mockHttpServer = new MockHttpServer();
            _mockPlatform.SetHttpServer(_mockHttpServer);
            
            // Set platform factory to use our mock
            SetMockPlatform(_mockPlatform);
            
            _server = new LocalSendHttpServer();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _server?.Dispose();
            ResetPlatformFactory();
        }

        #region Server Lifecycle Tests

        [TestMethod]
        public async Task StartAsync_WithValidPort_StartsServer()
        {
            // Act
            await _server.StartAsync(53317, false);

            // Assert
            Assert.IsTrue(_server.IsRunning);
            Assert.AreEqual(53317, _server.Port);
            Assert.IsFalse(_server.UseHttps);
            Assert.IsTrue(_mockHttpServer.IsStarted);
        }

        [TestMethod]
        public async Task StartAsync_WithHttpsEnabled_ConfiguresHttps()
        {
            // Arrange
            _mockPlatform.SetCertificateGenerationSupport(true);

            // Act
            await _server.StartAsync(53317, true);

            // Assert
            Assert.IsTrue(_server.IsRunning);
            Assert.IsTrue(_server.UseHttps);
        }

        [TestMethod]
        public async Task StartAsync_WhenAlreadyRunning_ThrowsException()
        {
            // Arrange
            await _server.StartAsync(53317, false);

            // Act & Assert
            await TestHelpers.AssertThrowsAsync<InvalidOperationException>(
                () => _server.StartAsync(53318, false));
        }

        [TestMethod]
        public async Task StartAsync_OnUnsupportedPlatform_ThrowsException()
        {
            // Arrange
            _mockPlatform.SetHttpServerSupport(false);

            // Act & Assert
            await TestHelpers.AssertThrowsAsync<NotSupportedException>(
                () => _server.StartAsync(53317, false));
        }

        [TestMethod]
        public async Task StopAsync_WhenRunning_StopsServer()
        {
            // Arrange
            await _server.StartAsync(53317, false);

            // Act
            await _server.StopAsync();

            // Assert
            Assert.IsFalse(_server.IsRunning);
            Assert.IsFalse(_mockHttpServer.IsStarted);
        }

        [TestMethod]
        public async Task StopAsync_WhenNotRunning_DoesNotThrow()
        {
            // Act & Assert - Should not throw
            await _server.StopAsync();
            Assert.IsFalse(_server.IsRunning);
        }

        #endregion

        #region Route Handling Tests

        [TestMethod]
        public async Task HandleRequest_WithValidInfoRoute_ReturnsDeviceInfo()
        {
            // Arrange
            await _server.StartAsync(53317, false);
            var request = CreateMockRequest("GET", "/api/localsend/v2/info");
            var response = new MockHttpResponse();

            // Act
            _mockHttpServer.SimulateRequest(request, response);
            await TestHelpers.WaitForConditionAsync(() => response.IsCompleted, TimeSpan.FromSeconds(1));

            // Assert
            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual("application/json", response.Headers["Content-Type"]);
            Assert.IsTrue(response.Body.Length > 0);
        }

        [TestMethod]
        public async Task HandleRequest_WithValidHealthRoute_ReturnsHealthInfo()
        {
            // Arrange
            await _server.StartAsync(53317, false);
            var request = CreateMockRequest("GET", "/health");
            var response = new MockHttpResponse();

            // Act
            _mockHttpServer.SimulateRequest(request, response);
            await TestHelpers.WaitForConditionAsync(() => response.IsCompleted, TimeSpan.FromSeconds(1));

            // Assert
            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual("application/json", response.Headers["Content-Type"]);
            Assert.IsTrue(response.Body.Length > 0);
        }

        [TestMethod]
        public async Task HandleRequest_WithInvalidRoute_Returns404()
        {
            // Arrange
            await _server.StartAsync(53317, false);
            var request = CreateMockRequest("GET", "/invalid/route");
            var response = new MockHttpResponse();

            // Act
            _mockHttpServer.SimulateRequest(request, response);
            await TestHelpers.WaitForConditionAsync(() => response.IsCompleted, TimeSpan.FromSeconds(1));

            // Assert
            Assert.AreEqual(404, response.StatusCode);
        }

        [TestMethod]
        public async Task HandleRequest_WithInvalidMethod_Returns405()
        {
            // Arrange
            await _server.StartAsync(53317, false);
            var request = CreateMockRequest("DELETE", "/api/localsend/v2/info");
            var response = new MockHttpResponse();

            // Act
            _mockHttpServer.SimulateRequest(request, response);
            await TestHelpers.WaitForConditionAsync(() => response.IsCompleted, TimeSpan.FromSeconds(1));

            // Assert
            Assert.AreEqual(405, response.StatusCode);
        }

        #endregion

        #region Security Tests

        [TestMethod]
        public async Task HandleRequest_WithSecurityThreat_BlocksRequest()
        {
            // Arrange
            await _server.StartAsync(53317, false);
            var request = CreateMockRequest("GET", "/api/localsend/v2/info");
            request.RemoteAddress = "192.168.1.999"; // Invalid IP to trigger security block
            var response = new MockHttpResponse();

            // Act
            _mockHttpServer.SimulateRequest(request, response);
            await TestHelpers.WaitForConditionAsync(() => response.IsCompleted, TimeSpan.FromSeconds(1));

            // Assert
            Assert.AreEqual(403, response.StatusCode);
        }

        [TestMethod]
        public async Task HandleRequest_WithReplayAttack_BlocksRequest()
        {
            // Arrange
            await _server.StartAsync(53317, false);
            var request = CreateMockRequest("POST", "/api/localsend/v2/register");
            request.Headers["X-Timestamp"] = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString(); // Old timestamp
            var response = new MockHttpResponse();

            // Act
            _mockHttpServer.SimulateRequest(request, response);
            await TestHelpers.WaitForConditionAsync(() => response.IsCompleted, TimeSpan.FromSeconds(1));

            // Assert
            Assert.AreEqual(409, response.StatusCode);
        }

        #endregion

        #region Error Handling Tests

        [TestMethod]
        public async Task HandleRequest_WithException_Returns500()
        {
            // Arrange
            await _server.StartAsync(53317, false);
            var request = CreateMockRequest("POST", "/api/localsend/v2/register");
            request.Body = new byte[] { 0xFF, 0xFE }; // Invalid JSON to trigger exception
            var response = new MockHttpResponse();

            // Act
            _mockHttpServer.SimulateRequest(request, response);
            await TestHelpers.WaitForConditionAsync(() => response.IsCompleted, TimeSpan.FromSeconds(1));

            // Assert
            Assert.AreEqual(500, response.StatusCode);
        }

        [TestMethod]
        public async Task StartAsync_WithException_RaisesErrorEvent()
        {
            // Arrange
            _mockHttpServer.SetStartException(new InvalidOperationException("Test exception"));
            
            ServerErrorEventArgs errorArgs = null;
            _server.ErrorOccurred += (sender, args) => errorArgs = args;

            // Act & Assert
            await TestHelpers.AssertThrowsAsync<InvalidOperationException>(
                () => _server.StartAsync(53317, false));

            Assert.IsNotNull(errorArgs);
            Assert.IsTrue(errorArgs.Message.Contains("Test exception"));
        }

        #endregion

        #region Upload/Download Route Tests

        [TestMethod]
        public async Task HandleRequest_PrepareUpload_ProcessesCorrectly()
        {
            // Arrange
            await _server.StartAsync(53317, false);
            var uploadRequest = TestDataFactory.CreateTestUploadRequest();
            var requestBody = System.Text.Encoding.UTF8.GetBytes(Internet.SerializeObject(uploadRequest));
            var request = CreateMockRequest("POST", "/api/localsend/v2/prepare-upload", requestBody);
            var response = new MockHttpResponse();

            // Act
            _mockHttpServer.SimulateRequest(request, response);
            await TestHelpers.WaitForConditionAsync(() => response.IsCompleted, TimeSpan.FromSeconds(1));

            // Assert
            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual("application/json", response.Headers["Content-Type"]);
        }

        [TestMethod]
        public async Task HandleRequest_Upload_ProcessesFileData()
        {
            // Arrange
            await _server.StartAsync(53317, false);
            
            // First prepare upload
            var uploadRequest = TestDataFactory.CreateTestUploadRequest();
            var prepareBody = System.Text.Encoding.UTF8.GetBytes(Internet.SerializeObject(uploadRequest));
            var prepareRequest = CreateMockRequest("POST", "/api/localsend/v2/prepare-upload", prepareBody);
            var prepareResponse = new MockHttpResponse();
            
            _mockHttpServer.SimulateRequest(prepareRequest, prepareResponse);
            await TestHelpers.WaitForConditionAsync(() => prepareResponse.IsCompleted, TimeSpan.FromSeconds(1));

            // Then upload file
            var fileData = TestHelpers.GenerateTestData(1024);
            var uploadFileRequest = CreateMockRequest("POST", "/api/localsend/v2/upload?sessionId=test&fileId=test.txt", fileData);
            var uploadResponse = new MockHttpResponse();

            // Act
            _mockHttpServer.SimulateRequest(uploadFileRequest, uploadResponse);
            await TestHelpers.WaitForConditionAsync(() => uploadResponse.IsCompleted, TimeSpan.FromSeconds(1));

            // Assert
            Assert.AreEqual(200, uploadResponse.StatusCode);
        }

        #endregion

        #region Helper Methods

        private MockHttpRequest CreateMockRequest(string method, string path, byte[] body = null)
        {
            return new MockHttpRequest
            {
                Method = method,
                Path = path,
                Headers = new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/json",
                    ["User-Agent"] = "LocalTalk-Test/1.0"
                },
                Body = body ?? new byte[0],
                RemoteAddress = "192.168.1.100"
            };
        }

        private void SetMockPlatform(MockPlatform platform)
        {
            // Use reflection to set the platform factory for testing
            var field = typeof(PlatformFactory).GetField("_current", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            field?.SetValue(null, platform);
        }

        private void ResetPlatformFactory()
        {
            // Reset platform factory after testing
            var field = typeof(PlatformFactory).GetField("_current", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            field?.SetValue(null, null);
        }

        #endregion
    }
}
