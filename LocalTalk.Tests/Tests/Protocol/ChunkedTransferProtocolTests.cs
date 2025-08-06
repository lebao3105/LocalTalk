using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LocalTalk.Tests.TestFramework;
using Shared.Protocol;
using Shared.Platform;
using Shared.FileSystem;

namespace LocalTalk.Tests.Tests.Protocol
{
    [TestClass]
    public class ChunkedTransferProtocolTests
    {
        private ChunkedTransferProtocol _protocol;
        private MockStorageFile _mockFile;
        private TransferRequest _testRequest;

        [TestInitialize]
        public void Setup()
        {
            ResetProtocolSingleton();
            _protocol = ChunkedTransferProtocol.Instance;
            
            _mockFile = new MockStorageFile("test.txt", "C:\\test.txt", 1024 * 10); // 10KB file
            _testRequest = new TransferRequest
            {
                FileName = "test.txt",
                FileSize = 1024 * 10,
                Direction = TransferDirection.Upload,
                SourceFile = _mockFile,
                DestinationPath = "C:\\destination\\test.txt",
                ChunkSize = 1024 // 1KB chunks
            };
        }

        [TestCleanup]
        public void Cleanup()
        {
            ResetProtocolSingleton();
        }

        #region Transfer Session Tests

        [TestMethod]
        public async Task StartTransferAsync_WithValidRequest_CreatesSession()
        {
            // Act
            var session = await _protocol.StartTransferAsync(_testRequest);

            // Assert
            Assert.IsNotNull(session);
            Assert.IsNotNull(session.SessionId);
            Assert.AreEqual(_testRequest.FileName, session.Request.FileName);
            Assert.AreEqual(_testRequest.FileSize, session.Request.FileSize);
            Assert.AreEqual(TransferStatus.Active, session.Status);
            Assert.AreEqual(10, session.TotalChunks); // 10KB / 1KB = 10 chunks
            Assert.AreEqual(0, session.CompletedChunks);
            Assert.AreEqual(0, session.FailedChunks);
        }

        [TestMethod]
        public async Task StartTransferAsync_WithDownloadRequest_InitializesCorrectly()
        {
            // Arrange
            _testRequest.Direction = TransferDirection.Download;

            // Act
            var session = await _protocol.StartTransferAsync(_testRequest);

            // Assert
            Assert.IsNotNull(session);
            Assert.AreEqual(TransferDirection.Download, session.Request.Direction);
            Assert.AreEqual(TransferStatus.Active, session.Status);
        }

        [TestMethod]
        public async Task StartTransferAsync_CalculatesOptimalChunkSize()
        {
            // Arrange
            _testRequest.ChunkSize = null; // Let protocol determine optimal size
            _testRequest.FileSize = 1024 * 1024; // 1MB file

            // Act
            var session = await _protocol.StartTransferAsync(_testRequest);

            // Assert
            Assert.IsNotNull(session);
            Assert.IsTrue(session.ChunkSize > 0);
            Assert.IsTrue(session.TotalChunks > 0);
            Assert.AreEqual((int)Math.Ceiling((double)_testRequest.FileSize / session.ChunkSize), session.TotalChunks);
        }

        #endregion

        #region Chunk Transfer Tests

        [TestMethod]
        public async Task TransferChunkAsync_WithValidChunk_ReturnsSuccess()
        {
            // Arrange
            var session = await _protocol.StartTransferAsync(_testRequest);
            var chunkData = TestHelpers.GenerateTestData(1024);

            // Act
            var result = await _protocol.TransferChunkAsync(session.SessionId, 0, chunkData);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(session.SessionId, result.SessionId);
            Assert.AreEqual(0, result.ChunkIndex);
            Assert.IsNull(result.ErrorMessage);
        }

        [TestMethod]
        public async Task TransferChunkAsync_WithInvalidSessionId_ReturnsError()
        {
            // Act
            var result = await _protocol.TransferChunkAsync("invalid-session", 0, new byte[1024]);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("Session not found", result.ErrorMessage);
        }

        [TestMethod]
        public async Task TransferChunkAsync_UpdatesSessionStatistics()
        {
            // Arrange
            var session = await _protocol.StartTransferAsync(_testRequest);
            var chunkData = TestHelpers.GenerateTestData(1024);

            // Act
            var result = await _protocol.TransferChunkAsync(session.SessionId, 0, chunkData);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, session.CompletedChunks);
            Assert.AreEqual(ChunkState.Completed, session.ChunkStates[0]);
            Assert.IsTrue(session.LastActivity > session.StartTime);
        }

        [TestMethod]
        public async Task TransferChunkAsync_WithAllChunksCompleted_CompletesTransfer()
        {
            // Arrange
            var session = await _protocol.StartTransferAsync(_testRequest);
            var chunkData = TestHelpers.GenerateTestData(1024);
            
            TransferCompletedEventArgs completedArgs = null;
            _protocol.TransferCompleted += (sender, args) => completedArgs = args;

            // Act - Transfer all chunks
            for (int i = 0; i < session.TotalChunks; i++)
            {
                await _protocol.TransferChunkAsync(session.SessionId, i, chunkData);
            }

            // Assert
            Assert.AreEqual(session.TotalChunks, session.CompletedChunks);
            Assert.AreEqual(TransferStatus.Completed, session.Status);
            Assert.IsNotNull(completedArgs);
            Assert.IsTrue(completedArgs.Success);
            Assert.AreEqual(session.SessionId, completedArgs.SessionId);
        }

        #endregion

        #region Parallel Transfer Tests

        [TestMethod]
        public async Task TransferChunksParallelAsync_WithMultipleChunks_TransfersInParallel()
        {
            // Arrange
            var session = await _protocol.StartTransferAsync(_testRequest);
            var chunkRequests = new List<ChunkTransferRequest>();
            
            for (int i = 0; i < 3; i++)
            {
                chunkRequests.Add(new ChunkTransferRequest
                {
                    ChunkIndex = i,
                    ChunkData = TestHelpers.GenerateTestData(1024)
                });
            }

            // Act
            var results = await _protocol.TransferChunksParallelAsync(session.SessionId, chunkRequests);

            // Assert
            Assert.AreEqual(3, results.Count);
            Assert.IsTrue(results.All(r => r.Success));
            Assert.AreEqual(3, session.CompletedChunks);
        }

        [TestMethod]
        public async Task TransferChunksParallelAsync_WithInvalidSession_ReturnsErrors()
        {
            // Arrange
            var chunkRequests = new List<ChunkTransferRequest>
            {
                new ChunkTransferRequest { ChunkIndex = 0, ChunkData = new byte[1024] }
            };

            // Act
            var results = await _protocol.TransferChunksParallelAsync("invalid-session", chunkRequests);

            // Assert
            Assert.AreEqual(1, results.Count);
            Assert.IsFalse(results[0].Success);
            Assert.AreEqual("Session not found", results[0].ErrorMessage);
        }

        #endregion

        #region Progress Tracking Tests

        [TestMethod]
        public async Task TransferChunkAsync_RaisesProgressEvent()
        {
            // Arrange
            var session = await _protocol.StartTransferAsync(_testRequest);
            var chunkData = TestHelpers.GenerateTestData(1024);
            
            TransferProgressEventArgs progressArgs = null;
            _protocol.TransferProgress += (sender, args) => progressArgs = args;

            // Act
            await _protocol.TransferChunkAsync(session.SessionId, 0, chunkData);

            // Assert
            Assert.IsNotNull(progressArgs);
            Assert.AreEqual(session.SessionId, progressArgs.SessionId);
            Assert.AreEqual(1, progressArgs.CompletedChunks);
            Assert.AreEqual(session.TotalChunks, progressArgs.TotalChunks);
            Assert.IsTrue(progressArgs.Progress > 0);
        }

        [TestMethod]
        public async Task TransferChunkAsync_RaisesChunkTransferredEvent()
        {
            // Arrange
            var session = await _protocol.StartTransferAsync(_testRequest);
            var chunkData = TestHelpers.GenerateTestData(1024);
            
            ChunkTransferEventArgs chunkArgs = null;
            _protocol.ChunkTransferred += (sender, args) => chunkArgs = args;

            // Act
            await _protocol.TransferChunkAsync(session.SessionId, 0, chunkData);

            // Assert
            Assert.IsNotNull(chunkArgs);
            Assert.AreEqual(session.SessionId, chunkArgs.SessionId);
            Assert.AreEqual(0, chunkArgs.ChunkIndex);
            Assert.IsTrue(chunkArgs.Success);
            Assert.AreEqual(1024, chunkArgs.ChunkSize);
        }

        #endregion

        #region Session Management Tests

        [TestMethod]
        public async Task GetActiveSessionsAsync_ReturnsActiveSessions()
        {
            // Arrange
            var session1 = await _protocol.StartTransferAsync(_testRequest);
            var session2 = await _protocol.StartTransferAsync(_testRequest);

            // Act
            var activeSessions = await _protocol.GetActiveSessionsAsync();

            // Assert
            Assert.AreEqual(2, activeSessions.Count());
            Assert.IsTrue(activeSessions.Any(s => s.SessionId == session1.SessionId));
            Assert.IsTrue(activeSessions.Any(s => s.SessionId == session2.SessionId));
        }

        [TestMethod]
        public async Task GetSessionAsync_WithValidId_ReturnsSession()
        {
            // Arrange
            var session = await _protocol.StartTransferAsync(_testRequest);

            // Act
            var retrievedSession = await _protocol.GetSessionAsync(session.SessionId);

            // Assert
            Assert.IsNotNull(retrievedSession);
            Assert.AreEqual(session.SessionId, retrievedSession.SessionId);
            Assert.AreEqual(session.Request.FileName, retrievedSession.Request.FileName);
        }

        [TestMethod]
        public async Task GetSessionAsync_WithInvalidId_ReturnsNull()
        {
            // Act
            var session = await _protocol.GetSessionAsync("invalid-session-id");

            // Assert
            Assert.IsNull(session);
        }

        [TestMethod]
        public async Task CancelTransferAsync_WithValidSession_CancelsTransfer()
        {
            // Arrange
            var session = await _protocol.StartTransferAsync(_testRequest);

            // Act
            var result = await _protocol.CancelTransferAsync(session.SessionId);

            // Assert
            Assert.IsTrue(result);
            var cancelledSession = await _protocol.GetSessionAsync(session.SessionId);
            Assert.AreEqual(TransferStatus.Cancelled, cancelledSession.Status);
        }

        #endregion

        #region Error Handling Tests

        [TestMethod]
        public async Task TransferChunkAsync_WithException_HandlesGracefully()
        {
            // Arrange
            var session = await _protocol.StartTransferAsync(_testRequest);
            
            // Act - Pass null chunk data to trigger error
            var result = await _protocol.TransferChunkAsync(session.SessionId, 0, null);

            // Assert
            Assert.IsNotNull(result);
            // The result might be success or failure depending on implementation
            // but it should not throw an exception
        }

        [TestMethod]
        public async Task StartTransferAsync_WithNullRequest_HandlesGracefully()
        {
            // Act & Assert
            await TestHelpers.AssertThrowsAsync<ArgumentNullException>(
                () => _protocol.StartTransferAsync(null));
        }

        #endregion

        #region Chunk Manager Tests

        [TestMethod]
        public void ChunkManager_CreateChunk_CreatesCorrectChunk()
        {
            // Arrange
            var manager = new ChunkManager();
            var sourceData = TestHelpers.GenerateTestData(1024);

            // Act
            var chunk = manager.CreateChunk(sourceData, 0, 512);

            // Assert
            Assert.AreEqual(512, chunk.Length);
            Assert.IsTrue(TestHelpers.ByteArraysEqual(
                sourceData.Take(512).ToArray(), 
                chunk));
        }

        [TestMethod]
        public void ChunkManager_CalculateChecksum_ReturnsConsistentHash()
        {
            // Arrange
            var manager = new ChunkManager();
            var chunkData = TestHelpers.GenerateTestData(1024);

            // Act
            var checksum1 = manager.CalculateChunkChecksum(chunkData);
            var checksum2 = manager.CalculateChunkChecksum(chunkData);

            // Assert
            Assert.IsNotNull(checksum1);
            Assert.IsNotNull(checksum2);
            Assert.AreEqual(checksum1, checksum2);
        }

        [TestMethod]
        public void ChunkManager_ValidateChunk_WithCorrectChecksum_ReturnsTrue()
        {
            // Arrange
            var manager = new ChunkManager();
            var chunkData = TestHelpers.GenerateTestData(1024);
            var checksum = manager.CalculateChunkChecksum(chunkData);

            // Act
            var isValid = manager.ValidateChunk(chunkData, checksum);

            // Assert
            Assert.IsTrue(isValid);
        }

        [TestMethod]
        public void ChunkManager_ValidateChunk_WithIncorrectChecksum_ReturnsFalse()
        {
            // Arrange
            var manager = new ChunkManager();
            var chunkData = TestHelpers.GenerateTestData(1024);
            var incorrectChecksum = "invalid-checksum";

            // Act
            var isValid = manager.ValidateChunk(chunkData, incorrectChecksum);

            // Assert
            Assert.IsFalse(isValid);
        }

        #endregion

        #region Helper Methods

        private void ResetProtocolSingleton()
        {
            // Use reflection to reset the singleton instance for testing
            var field = typeof(ChunkedTransferProtocol).GetField("_instance", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            field?.SetValue(null, null);
        }

        #endregion
    }
}
