using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shared.Platform;

namespace LocalTalk.Tests.Tests.Platform
{
    /// <summary>
    /// Tests for the logging framework
    /// </summary>
    [TestClass]
    public class LoggingTests
    {
        private TestLogger _testLogger;
        private LoggerFactory _factory;

        [TestInitialize]
        public void Setup()
        {
            _testLogger = new TestLogger("Test");
            _factory = new LoggerFactory(categoryName => new TestLogger(categoryName));
        }

        [TestMethod]
        public void Logger_LogsAtCorrectLevel()
        {
            // Arrange
            _testLogger.MinimumLevel = LogLevel.Warning;

            // Act
            _testLogger.Debug("Debug message");
            _testLogger.Info("Info message");
            _testLogger.Warning("Warning message");
            _testLogger.Error("Error message");

            // Assert
            Assert.AreEqual(2, _testLogger.LoggedEntries.Count);
            Assert.AreEqual(LogLevel.Warning, _testLogger.LoggedEntries[0].Level);
            Assert.AreEqual(LogLevel.Error, _testLogger.LoggedEntries[1].Level);
        }

        [TestMethod]
        public void Logger_HandlesExceptions()
        {
            // Arrange
            var exception = new InvalidOperationException("Test exception");

            // Act
            _testLogger.Error("Error with exception", exception);

            // Assert
            Assert.AreEqual(1, _testLogger.LoggedEntries.Count);
            Assert.AreEqual(exception, _testLogger.LoggedEntries[0].Exception);
        }

        [TestMethod]
        public void LoggerFactory_CreatesSameInstanceForSameCategory()
        {
            // Act
            var logger1 = _factory.CreateLogger("TestCategory");
            var logger2 = _factory.CreateLogger("TestCategory");

            // Assert
            Assert.AreSame(logger1, logger2);
        }

        [TestMethod]
        public void LoggerFactory_CreatesDifferentInstancesForDifferentCategories()
        {
            // Act
            var logger1 = _factory.CreateLogger("Category1");
            var logger2 = _factory.CreateLogger("Category2");

            // Assert
            Assert.AreNotSame(logger1, logger2);
        }

        [TestMethod]
        public void LogManager_GetLogger_ReturnsValidLogger()
        {
            // Act
            var logger = LogManager.GetLogger("TestCategory");

            // Assert
            Assert.IsNotNull(logger);
            Assert.IsTrue(logger.IsEnabled(LogLevel.Info));
        }

        [TestMethod]
        public void LogManager_GetLoggerGeneric_ReturnsValidLogger()
        {
            // Act
            var logger = LogManager.GetLogger<LoggingTests>();

            // Assert
            Assert.IsNotNull(logger);
        }

        [TestMethod]
        public void CompositeLogger_WritesToAllLoggers()
        {
            // Arrange
            var logger1 = new TestLogger("Test1");
            var logger2 = new TestLogger("Test2");
            var composite = new CompositeLogger("Composite", logger1, logger2);

            // Act
            composite.Info("Test message");

            // Assert
            Assert.AreEqual(1, logger1.LoggedEntries.Count);
            Assert.AreEqual(1, logger2.LoggedEntries.Count);
            Assert.AreEqual("Test message", logger1.LoggedEntries[0].Message);
            Assert.AreEqual("Test message", logger2.LoggedEntries[0].Message);
        }

        [TestMethod]
        public void Logger_IsEnabled_RespectsMinimumLevel()
        {
            // Arrange
            _testLogger.MinimumLevel = LogLevel.Warning;

            // Act & Assert
            Assert.IsFalse(_testLogger.IsEnabled(LogLevel.Debug));
            Assert.IsFalse(_testLogger.IsEnabled(LogLevel.Info));
            Assert.IsTrue(_testLogger.IsEnabled(LogLevel.Warning));
            Assert.IsTrue(_testLogger.IsEnabled(LogLevel.Error));
            Assert.IsTrue(_testLogger.IsEnabled(LogLevel.Critical));
        }

        [TestMethod]
        public void LogEntry_ToString_FormatsCorrectly()
        {
            // Arrange
            var entry = new LogEntry
            {
                Timestamp = new DateTime(2024, 8, 4, 12, 0, 0),
                Level = LogLevel.Error,
                Category = "TestCategory",
                Message = "Test message",
                ThreadId = 123
            };

            // Act
            var result = entry.ToString();

            // Assert
            Assert.IsTrue(result.Contains("[2024-08-04 12:00:00.000]"));
            Assert.IsTrue(result.Contains("[Error]"));
            Assert.IsTrue(result.Contains("[TestCategory]"));
            Assert.IsTrue(result.Contains("[Thread:123]"));
            Assert.IsTrue(result.Contains("Test message"));
        }

        [TestMethod]
        public void LogEntry_ToString_IncludesException()
        {
            // Arrange
            var exception = new InvalidOperationException("Test exception");
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = LogLevel.Error,
                Category = "TestCategory",
                Message = "Test message",
                Exception = exception,
                ThreadId = 123
            };

            // Act
            var result = entry.ToString();

            // Assert
            Assert.IsTrue(result.Contains("Test message"));
            Assert.IsTrue(result.Contains("Exception: System.InvalidOperationException: Test exception"));
        }
    }

    /// <summary>
    /// Test logger implementation for unit testing
    /// </summary>
    public class TestLogger : Logger
    {
        public List<LogEntry> LoggedEntries { get; } = new List<LogEntry>();

        public TestLogger(string categoryName) : base(categoryName)
        {
        }

        protected override void WriteLog(LogEntry entry)
        {
            LoggedEntries.Add(entry);
        }

        public void Clear()
        {
            LoggedEntries.Clear();
        }
    }
}
