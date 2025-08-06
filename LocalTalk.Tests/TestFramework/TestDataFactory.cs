using System;
using System.Collections.Generic;
using Shared.Models;
using Shared.Protocol;

namespace LocalTalk.Tests.TestFramework
{
    /// <summary>
    /// Factory for creating test data objects
    /// </summary>
    public static class TestDataFactory
    {
        /// <summary>
        /// Creates a test device with default values
        /// </summary>
        public static Device CreateTestDevice(string alias = "TestDevice", string ip = "192.168.1.100", int port = 53317)
        {
            return new Device
            {
                alias = alias,
                ip = ip,
                port = port,
                fingerprint = Guid.NewGuid().ToString(),
                version = "2.0",
                deviceModel = "TestModel",
                deviceType = "desktop",
                download = true
            };
        }

        /// <summary>
        /// Creates a list of test devices
        /// </summary>
        public static List<Device> CreateTestDevices(int count = 3)
        {
            var devices = new List<Device>();
            for (int i = 0; i < count; i++)
            {
                devices.Add(CreateTestDevice($"TestDevice{i + 1}", $"192.168.1.{100 + i}", 53317 + i));
            }
            return devices;
        }

        /// <summary>
        /// Creates a test transfer request
        /// </summary>
        public static TransferRequest CreateTestTransferRequest(string fileName = "test.txt", long fileSize = 1024)
        {
            return new TransferRequest
            {
                SessionId = Guid.NewGuid().ToString(),
                FileName = fileName,
                FileSize = fileSize,
                Direction = TransferDirection.Upload,
                DestinationIP = "192.168.1.100",
                DestinationPort = 53317,
                ChunkSize = 8192
            };
        }

        /// <summary>
        /// Creates a test transfer session
        /// </summary>
        public static TransferSession CreateTestTransferSession(TransferRequest request = null)
        {
            request = request ?? CreateTestTransferRequest();
            
            return new TransferSession
            {
                SessionId = request.SessionId,
                Request = request,
                StartTime = DateTime.Now,
                Status = TransferStatus.Initializing,
                ChunkSize = request.ChunkSize ?? 8192,
                TotalChunks = (int)Math.Ceiling((double)request.FileSize / (request.ChunkSize ?? 8192))
            };
        }

        /// <summary>
        /// Creates test file metadata
        /// </summary>
        public static Dictionary<string, object> CreateTestFileMetadata(string fileName = "test.txt", long fileSize = 1024)
        {
            return new Dictionary<string, object>
            {
                ["fileName"] = fileName,
                ["size"] = fileSize,
                ["fileType"] = "text/plain",
                ["lastModified"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["preview"] = null
            };
        }

        /// <summary>
        /// Creates test upload request data
        /// </summary>
        public static Dictionary<string, object> CreateTestUploadRequest(Dictionary<string, object> files = null)
        {
            files = files ?? new Dictionary<string, object>
            {
                ["test.txt"] = CreateTestFileMetadata()
            };

            return new Dictionary<string, object>
            {
                ["info"] = new Dictionary<string, object>
                {
                    ["alias"] = "TestSender",
                    ["version"] = "2.0",
                    ["deviceModel"] = "TestModel",
                    ["deviceType"] = "desktop",
                    ["fingerprint"] = Guid.NewGuid().ToString()
                },
                ["files"] = files
            };
        }

        /// <summary>
        /// Creates test device info for registration
        /// </summary>
        public static Dictionary<string, object> CreateTestDeviceInfo(string alias = "TestDevice")
        {
            return new Dictionary<string, object>
            {
                ["alias"] = alias,
                ["version"] = "2.0",
                ["deviceModel"] = "TestModel",
                ["deviceType"] = "desktop",
                ["fingerprint"] = Guid.NewGuid().ToString(),
                ["port"] = 53317,
                ["protocol"] = "http",
                ["download"] = true
            };
        }

        /// <summary>
        /// Creates test HTTP request headers
        /// </summary>
        public static Dictionary<string, string> CreateTestHeaders()
        {
            return new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["User-Agent"] = "LocalTalk-Test/1.0",
                ["Accept"] = "application/json"
            };
        }

        /// <summary>
        /// Creates test error response
        /// </summary>
        public static Dictionary<string, object> CreateTestErrorResponse(string message = "Test error", string code = "TEST_ERROR")
        {
            return new Dictionary<string, object>
            {
                ["message"] = message,
                ["code"] = code,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        /// <summary>
        /// Creates test success response
        /// </summary>
        public static Dictionary<string, object> CreateTestSuccessResponse(object data = null)
        {
            return new Dictionary<string, object>
            {
                ["success"] = true,
                ["data"] = data,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }
    }
}
