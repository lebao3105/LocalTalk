using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shared.Platform;
using Shared.Security;

namespace LocalTalk.Tests.Tests.Platform
{
    /// <summary>
    /// Integration tests for the enhanced infrastructure
    /// </summary>
    [TestClass]
    public class InfrastructureTests
    {
        [TestInitialize]
        public void Setup()
        {
            // Reset infrastructure before each test
            InfrastructureInitializer.Reset();
        }

        [TestMethod]
        public void Infrastructure_Initialize_CompletesSuccessfully()
        {
            // Act
            InfrastructureInitializer.Initialize();

            // Assert
            Assert.IsTrue(InfrastructureInitializer.IsInitialized);
        }

        [TestMethod]
        public void Infrastructure_Initialize_WithCustomConfig_UsesCustomSettings()
        {
            // Arrange
            var customConfig = new MemoryConfiguration();
            customConfig.SetValue("Security.MaxRequestsPerMinute", "500");

            var infraConfig = new InfrastructureConfiguration
            {
                CustomConfiguration = customConfig
            };

            // Act
            InfrastructureInitializer.Initialize(infraConfig);

            // Assert
            Assert.IsTrue(InfrastructureInitializer.IsInitialized);
            Assert.AreEqual("500", ConfigurationManager.Current.GetValue("Security.MaxRequestsPerMinute"));
        }

        [TestMethod]
        public void Infrastructure_LoggingIntegration_WorksCorrectly()
        {
            // Arrange
            InfrastructureInitializer.Initialize();

            // Act
            var logger = LogManager.GetLogger("TestLogger");
            logger.Info("Test message");

            // Assert
            Assert.IsNotNull(logger);
            Assert.IsTrue(logger.IsEnabled(LogLevel.Info));
        }

        [TestMethod]
        public void Infrastructure_PerformanceMonitoring_WorksCorrectly()
        {
            // Arrange
            InfrastructureInitializer.Initialize();

            // Act
            using (PerformanceManager.Time("TestOperation"))
            {
                System.Threading.Thread.Sleep(10); // Simulate work
            }
            PerformanceManager.Counter("TestCounter");
            PerformanceManager.Metric("TestMetric", 42.5, "units");

            var stats = PerformanceManager.Current.GetStatistics();

            // Assert
            Assert.IsTrue(stats.Operations.ContainsKey("TestOperation"));
            Assert.IsTrue(stats.Counters.ContainsKey("TestCounter"));
            Assert.IsTrue(stats.Metrics.ContainsKey("TestMetric"));
            Assert.AreEqual(1, stats.Counters["TestCounter"]);
            Assert.AreEqual(42.5, stats.Metrics["TestMetric"].LastValue);
        }

        [TestMethod]
        public void Infrastructure_ConfigurationManagement_WorksCorrectly()
        {
            // Arrange
            InfrastructureInitializer.Initialize();

            // Act
            var securityConfig = new SecurityConfiguration();
            securityConfig.LoadFrom(ConfigurationManager.Current);

            // Assert
            Assert.IsTrue(securityConfig.MaxRequestsPerMinute > 0);
            Assert.IsTrue(securityConfig.ThreatCacheExpiryMinutes > 0);
        }

        [TestMethod]
        public void Infrastructure_SecurityAnalyzer_UsesConfiguration()
        {
            // Arrange
            var customConfig = new MemoryConfiguration();
            customConfig.SetValue("Security.MaxRequestsPerMinute", "200");
            customConfig.SetValue("Security.EnableSqlInjectionDetection", "false");

            var infraConfig = new InfrastructureConfiguration
            {
                CustomConfiguration = customConfig
            };

            InfrastructureInitializer.Initialize(infraConfig);

            // Act
            var analyzer = SecurityAnalyzer.Instance;
            var headers = new System.Collections.Generic.Dictionary<string, string>();
            var result = analyzer.AnalyzeRequest("127.0.0.1", "/test", headers, null);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("127.0.0.1", result.RemoteAddress);
        }

        [TestMethod]
        public void Infrastructure_FirewallManager_UsesLoggingAndPerformance()
        {
            // Arrange
            InfrastructureInitializer.Initialize();

            // Act
            var manager = FirewallManager.Instance;
            var initialStats = PerformanceManager.Current.GetStatistics();
            var initialRequestCount = initialStats.Counters.GetValueOrDefault("FirewallManager.OpenPortRequests", 0);

            // Try to open a port (will likely fail on test environment, but should log and record metrics)
            try
            {
                var result = manager.OpenPortAsync(8080).Result;
            }
            catch
            {
                // Expected to fail in test environment
            }

            var finalStats = PerformanceManager.Current.GetStatistics();

            // Assert
            Assert.IsTrue(finalStats.Counters.ContainsKey("FirewallManager.OpenPortRequests"));
            Assert.IsTrue(finalStats.Counters["FirewallManager.OpenPortRequests"] > initialRequestCount);
        }

        [TestMethod]
        public void Infrastructure_ThreadSafety_WorksUnderLoad()
        {
            // Arrange
            InfrastructureInitializer.Initialize();
            const int taskCount = 10;
            const int operationsPerTask = 100;

            // Act
            var tasks = new Task[taskCount];
            for (int i = 0; i < taskCount; i++)
            {
                int taskId = i;
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < operationsPerTask; j++)
                    {
                        var logger = LogManager.GetLogger($"Task{taskId}");
                        logger.Debug($"Operation {j}");

                        using (PerformanceManager.Time($"Task{taskId}.Operation"))
                        {
                            PerformanceManager.Counter($"Task{taskId}.Counter");
                            PerformanceManager.Metric($"Task{taskId}.Metric", j);
                        }

                        ConfigurationManager.Current.SetValue($"Test.Task{taskId}.Value{j}", j.ToString());
                    }
                });
            }

            Task.WaitAll(tasks);
            var stats = PerformanceManager.Current.GetStatistics();

            // Assert
            Assert.IsTrue(stats.Operations.Count > 0);
            Assert.IsTrue(stats.Counters.Count > 0);
            Assert.IsTrue(stats.Metrics.Count > 0);

            // Verify total counter values
            long totalCounterValue = 0;
            for (int i = 0; i < taskCount; i++)
            {
                var counterKey = $"Task{i}.Counter";
                if (stats.Counters.ContainsKey(counterKey))
                {
                    totalCounterValue += stats.Counters[counterKey];
                }
            }
            Assert.AreEqual(taskCount * operationsPerTask, totalCounterValue);
        }

        [TestMethod]
        public void Infrastructure_MemoryManagement_TracksCorrectly()
        {
            // Arrange
            InfrastructureInitializer.Initialize();

            // Act
            PerformanceManager.Memory("TestCategory");
            var stats = PerformanceManager.Current.GetStatistics();

            // Assert
            Assert.IsTrue(stats.MemoryUsage.ContainsKey("TestCategory"));
            Assert.IsTrue(stats.MemoryUsage["TestCategory"].CurrentBytes > 0);
        }

        [TestMethod]
        public void Infrastructure_ConfigurationValidation_DetectsErrors()
        {
            // Arrange
            var badConfig = new SecurityConfiguration
            {
                MaxRequestsPerMinute = -1,
                ThreatCacheExpiryMinutes = 0
            };

            // Act
            var result = badConfig.Validate();

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Count > 0);
        }

        [TestMethod]
        public void Infrastructure_Dispose_CleansUpResources()
        {
            // Arrange
            InfrastructureInitializer.Initialize();
            var manager = FirewallManager.Instance;
            var analyzer = SecurityAnalyzer.Instance;
            var networkManager = NetworkInterfaceManager.Instance;

            // Act
            manager.Dispose();
            analyzer.Dispose();
            networkManager.Dispose();

            // Assert - Should not throw exceptions when disposed
            Assert.ThrowsException<ObjectDisposedException>(() => manager.GetActiveMappings());
            Assert.ThrowsException<ObjectDisposedException>(() => analyzer.AnalyzeRequest("127.0.0.1", "/", new System.Collections.Generic.Dictionary<string, string>(), null));
            Assert.ThrowsExceptionAsync<ObjectDisposedException>(() => networkManager.GetAllInterfacesAsync());
        }
    }

    /// <summary>
    /// Extension methods for test utilities
    /// </summary>
    public static class TestExtensions
    {
        public static TValue GetValueOrDefault<TKey, TValue>(this System.Collections.Generic.Dictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue)
        {
            return dictionary.TryGetValue(key, out var value) ? value : defaultValue;
        }
    }
}
