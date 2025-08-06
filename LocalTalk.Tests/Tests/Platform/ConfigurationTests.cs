using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shared.Platform;
using Shared.Security;

namespace LocalTalk.Tests.Tests.Platform
{
    /// <summary>
    /// Tests for the configuration management system
    /// </summary>
    [TestClass]
    public class ConfigurationTests
    {
        private MemoryConfiguration _config;

        [TestInitialize]
        public void Setup()
        {
            _config = new MemoryConfiguration();
        }

        [TestMethod]
        public void Configuration_SetAndGetValue_WorksCorrectly()
        {
            // Act
            _config.SetValue("TestKey", "TestValue");
            var result = _config.GetValue("TestKey");

            // Assert
            Assert.AreEqual("TestValue", result);
        }

        [TestMethod]
        public void Configuration_GetValueWithDefault_ReturnsDefault()
        {
            // Act
            var result = _config.GetValue("NonExistentKey", "DefaultValue");

            // Assert
            Assert.AreEqual("DefaultValue", result);
        }

        [TestMethod]
        public void Configuration_GetValueGeneric_ConvertsTypes()
        {
            // Arrange
            _config.SetValue("IntKey", "42");
            _config.SetValue("BoolKey", "true");
            _config.SetValue("DoubleKey", "3.14");

            // Act
            var intValue = _config.GetValue<int>("IntKey");
            var boolValue = _config.GetValue<bool>("BoolKey");
            var doubleValue = _config.GetValue<double>("DoubleKey");

            // Assert
            Assert.AreEqual(42, intValue);
            Assert.AreEqual(true, boolValue);
            Assert.AreEqual(3.14, doubleValue, 0.001);
        }

        [TestMethod]
        public void Configuration_GetValueGeneric_ReturnsDefaultOnConversionError()
        {
            // Arrange
            _config.SetValue("InvalidInt", "not-a-number");

            // Act
            var result = _config.GetValue<int>("InvalidInt", 999);

            // Assert
            Assert.AreEqual(999, result);
        }

        [TestMethod]
        public void Configuration_HasKey_WorksCorrectly()
        {
            // Arrange
            _config.SetValue("ExistingKey", "Value");

            // Act & Assert
            Assert.IsTrue(_config.HasKey("ExistingKey"));
            Assert.IsFalse(_config.HasKey("NonExistentKey"));
        }

        [TestMethod]
        public void Configuration_RemoveKey_WorksCorrectly()
        {
            // Arrange
            _config.SetValue("KeyToRemove", "Value");
            Assert.IsTrue(_config.HasKey("KeyToRemove"));

            // Act
            var removed = _config.RemoveKey("KeyToRemove");

            // Assert
            Assert.IsTrue(removed);
            Assert.IsFalse(_config.HasKey("KeyToRemove"));
        }

        [TestMethod]
        public void Configuration_GetAllKeys_ReturnsAllKeys()
        {
            // Arrange
            _config.SetValue("Key1", "Value1");
            _config.SetValue("Key2", "Value2");
            _config.SetValue("Key3", "Value3");

            // Act
            var keys = _config.GetAllKeys().ToList();

            // Assert
            Assert.AreEqual(3, keys.Count);
            Assert.IsTrue(keys.Contains("Key1"));
            Assert.IsTrue(keys.Contains("Key2"));
            Assert.IsTrue(keys.Contains("Key3"));
        }

        [TestMethod]
        public void Configuration_ConfigurationChanged_FiresOnSetValue()
        {
            // Arrange
            ConfigurationChangedEventArgs capturedArgs = null;
            _config.ConfigurationChanged += (sender, args) => capturedArgs = args;

            // Act
            _config.SetValue("TestKey", "TestValue");

            // Assert
            Assert.IsNotNull(capturedArgs);
            Assert.AreEqual("TestKey", capturedArgs.Key);
            Assert.AreEqual("TestValue", capturedArgs.NewValue);
            Assert.AreEqual(ConfigurationChangeType.Added, capturedArgs.ChangeType);
        }

        [TestMethod]
        public void Configuration_ConfigurationChanged_FiresOnModifyValue()
        {
            // Arrange
            _config.SetValue("TestKey", "OriginalValue");
            ConfigurationChangedEventArgs capturedArgs = null;
            _config.ConfigurationChanged += (sender, args) => capturedArgs = args;

            // Act
            _config.SetValue("TestKey", "ModifiedValue");

            // Assert
            Assert.IsNotNull(capturedArgs);
            Assert.AreEqual("TestKey", capturedArgs.Key);
            Assert.AreEqual("OriginalValue", capturedArgs.OldValue);
            Assert.AreEqual("ModifiedValue", capturedArgs.NewValue);
            Assert.AreEqual(ConfigurationChangeType.Modified, capturedArgs.ChangeType);
        }

        [TestMethod]
        public void Configuration_ConfigurationChanged_FiresOnRemoveKey()
        {
            // Arrange
            _config.SetValue("TestKey", "TestValue");
            ConfigurationChangedEventArgs capturedArgs = null;
            _config.ConfigurationChanged += (sender, args) => capturedArgs = args;

            // Act
            _config.RemoveKey("TestKey");

            // Assert
            Assert.IsNotNull(capturedArgs);
            Assert.AreEqual("TestKey", capturedArgs.Key);
            Assert.AreEqual("TestValue", capturedArgs.OldValue);
            Assert.IsNull(capturedArgs.NewValue);
            Assert.AreEqual(ConfigurationChangeType.Removed, capturedArgs.ChangeType);
        }

        [TestMethod]
        public void ConfigurationManager_LoadDefaults_SetsExpectedValues()
        {
            // Arrange
            ConfigurationManager.SetConfiguration(_config);

            // Act
            ConfigurationManager.LoadDefaults();

            // Assert
            Assert.AreEqual("Info", _config.GetValue("Logging.MinimumLevel"));
            Assert.AreEqual("100", _config.GetValue("Security.MaxRequestsPerMinute"));
            Assert.AreEqual("true", _config.GetValue("Security.EnableSqlInjectionDetection"));
            Assert.AreEqual("30", _config.GetValue("Network.InterfaceCacheTimeoutSeconds"));
        }

        [TestMethod]
        public void SecurityConfiguration_LoadFrom_LoadsCorrectly()
        {
            // Arrange
            _config.SetValue("Security.MaxRequestsPerMinute", "200");
            _config.SetValue("Security.EnableSqlInjectionDetection", "false");
            _config.SetValue("Security.ThreatCacheExpiryMinutes", "120");

            var securityConfig = new SecurityConfiguration();

            // Act
            securityConfig.LoadFrom(_config);

            // Assert
            Assert.AreEqual(200, securityConfig.MaxRequestsPerMinute);
            Assert.AreEqual(false, securityConfig.EnableSqlInjectionDetection);
            Assert.AreEqual(120, securityConfig.ThreatCacheExpiryMinutes);
        }

        [TestMethod]
        public void SecurityConfiguration_SaveTo_SavesCorrectly()
        {
            // Arrange
            var securityConfig = new SecurityConfiguration
            {
                MaxRequestsPerMinute = 150,
                EnableSqlInjectionDetection = false,
                ThreatCacheExpiryMinutes = 90
            };

            // Act
            securityConfig.SaveTo(_config);

            // Assert
            Assert.AreEqual("150", _config.GetValue("Security.MaxRequestsPerMinute"));
            Assert.AreEqual("False", _config.GetValue("Security.EnableSqlInjectionDetection"));
            Assert.AreEqual("90", _config.GetValue("Security.ThreatCacheExpiryMinutes"));
        }

        [TestMethod]
        public void SecurityConfiguration_Validate_ReturnsValidForGoodConfig()
        {
            // Arrange
            var securityConfig = new SecurityConfiguration
            {
                MaxRequestsPerMinute = 100,
                ThreatCacheExpiryMinutes = 60,
                MaxPayloadSize = 1024 * 1024,
                MaxHeaderSize = 8192
            };

            // Act
            var result = securityConfig.Validate();

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(0, result.Errors.Count);
        }

        [TestMethod]
        public void SecurityConfiguration_Validate_ReturnsErrorsForBadConfig()
        {
            // Arrange
            var securityConfig = new SecurityConfiguration
            {
                MaxRequestsPerMinute = -1,
                ThreatCacheExpiryMinutes = 0,
                MaxPayloadSize = -100,
                MaxHeaderSize = -50
            };

            // Act
            var result = securityConfig.Validate();

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Count > 0);
        }

        [TestMethod]
        public void SecurityConfiguration_Validate_ReturnsWarningsForHighValues()
        {
            // Arrange
            var securityConfig = new SecurityConfiguration
            {
                MaxRequestsPerMinute = 15000 // Very high value
            };

            // Act
            var result = securityConfig.Validate();

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(result.Warnings.Count > 0);
        }
    }
}
