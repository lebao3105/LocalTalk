using System;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shared.Platform;
using Shared.Security;
using Shared.Workflows;

namespace LocalTalk.Tests.Tests.Platform
{
    /// <summary>
    /// Tests for thread safety improvements in singleton classes
    /// </summary>
    [TestClass]
    public class ThreadSafetyTests
    {
        [TestMethod]
        public void FirewallManager_Singleton_IsThreadSafe()
        {
            // Arrange
            const int threadCount = 10;
            var instances = new FirewallManager[threadCount];
            var tasks = new Task[threadCount];

            // Act - Create multiple threads trying to access singleton
            for (int i = 0; i < threadCount; i++)
            {
                int index = i;
                tasks[i] = Task.Run(() =>
                {
                    instances[index] = FirewallManager.Instance;
                });
            }

            Task.WaitAll(tasks);

            // Assert - All instances should be the same reference
            for (int i = 1; i < threadCount; i++)
            {
                Assert.AreSame(instances[0], instances[i], 
                    "All FirewallManager instances should be the same reference");
            }
        }

        [TestMethod]
        public void SecurityAnalyzer_Singleton_IsThreadSafe()
        {
            // Arrange
            const int threadCount = 10;
            var instances = new SecurityAnalyzer[threadCount];
            var tasks = new Task[threadCount];

            // Act - Create multiple threads trying to access singleton
            for (int i = 0; i < threadCount; i++)
            {
                int index = i;
                tasks[i] = Task.Run(() =>
                {
                    instances[index] = SecurityAnalyzer.Instance;
                });
            }

            Task.WaitAll(tasks);

            // Assert - All instances should be the same reference
            for (int i = 1; i < threadCount; i++)
            {
                Assert.AreSame(instances[0], instances[i], 
                    "All SecurityAnalyzer instances should be the same reference");
            }
        }

        [TestMethod]
        public void NetworkInterfaceManager_Singleton_IsThreadSafe()
        {
            // Arrange
            const int threadCount = 10;
            var instances = new NetworkInterfaceManager[threadCount];
            var tasks = new Task[threadCount];

            // Act - Create multiple threads trying to access singleton
            for (int i = 0; i < threadCount; i++)
            {
                int index = i;
                tasks[i] = Task.Run(() =>
                {
                    instances[index] = NetworkInterfaceManager.Instance;
                });
            }

            Task.WaitAll(tasks);

            // Assert - All instances should be the same reference
            for (int i = 1; i < threadCount; i++)
            {
                Assert.AreSame(instances[0], instances[i], 
                    "All NetworkInterfaceManager instances should be the same reference");
            }
        }

        [TestMethod]
        public void ProgressTracker_Singleton_IsThreadSafe()
        {
            // Arrange
            const int threadCount = 10;
            var instances = new ProgressTracker[threadCount];
            var tasks = new Task[threadCount];

            // Act - Create multiple threads trying to access singleton
            for (int i = 0; i < threadCount; i++)
            {
                int index = i;
                tasks[i] = Task.Run(() =>
                {
                    instances[index] = ProgressTracker.Instance;
                });
            }

            Task.WaitAll(tasks);

            // Assert - All instances should be the same reference
            for (int i = 1; i < threadCount; i++)
            {
                Assert.AreSame(instances[0], instances[i], 
                    "All ProgressTracker instances should be the same reference");
            }
        }

        [TestMethod]
        public void FirewallManager_InputValidation_ThrowsOnInvalidPort()
        {
            // Arrange
            var manager = FirewallManager.Instance;

            // Act & Assert
            Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(
                () => manager.OpenPortAsync(0),
                "Should throw ArgumentOutOfRangeException for port 0");

            Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(
                () => manager.OpenPortAsync(65536),
                "Should throw ArgumentOutOfRangeException for port 65536");

            Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => manager.OpenPortAsync(8080, Shared.Platform.PortProtocol.TCP, null),
                "Should throw ArgumentNullException for null description");
        }

        [TestMethod]
        public void SecurityAnalyzer_InputValidation_ThrowsOnNullParameters()
        {
            // Arrange
            var analyzer = SecurityAnalyzer.Instance;
            var headers = new System.Collections.Generic.Dictionary<string, string>();

            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(
                () => analyzer.AnalyzeRequest(null, "/test", headers, null),
                "Should throw ArgumentNullException for null remoteAddress");

            Assert.ThrowsException<ArgumentNullException>(
                () => analyzer.AnalyzeRequest("127.0.0.1", null, headers, null),
                "Should throw ArgumentNullException for null path");

            Assert.ThrowsException<ArgumentNullException>(
                () => analyzer.AnalyzeRequest("127.0.0.1", "/test", null, null),
                "Should throw ArgumentNullException for null headers");
        }

        [TestMethod]
        public void NetworkInterfaceManager_InputValidation_ThrowsOnNullInterfaceName()
        {
            // Arrange
            var manager = NetworkInterfaceManager.Instance;

            // Act & Assert
            Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => manager.IsInterfaceAvailableAsync(null),
                "Should throw ArgumentNullException for null interface name");

            Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => manager.IsInterfaceAvailableAsync(""),
                "Should throw ArgumentNullException for empty interface name");
        }

        [TestMethod]
        public void ProgressTracker_InputValidation_ThrowsOnInvalidParameters()
        {
            // Arrange
            var tracker = ProgressTracker.Instance;

            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(
                () => tracker.StartOperation(null, "Test operation"),
                "Should throw ArgumentNullException for null operation ID");

            Assert.ThrowsException<ArgumentNullException>(
                () => tracker.StartOperation("test", null),
                "Should throw ArgumentNullException for null description");

            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => tracker.StartOperation("test", "Test operation", 0),
                "Should throw ArgumentOutOfRangeException for zero weight");

            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => tracker.StartOperation("test", "Test operation", -1),
                "Should throw ArgumentOutOfRangeException for negative weight");
        }
    }
}
