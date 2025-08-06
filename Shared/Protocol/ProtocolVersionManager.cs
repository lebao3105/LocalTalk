using Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Shared.Protocol
{
    /// <summary>
    /// Manages protocol version negotiation and compatibility
    /// </summary>
    public class ProtocolVersionManager
    {
        private static ProtocolVersionManager _instance;
        private readonly Dictionary<string, ProtocolVersion> _supportedVersions;
        private readonly ProtocolVersion _currentVersion;

        public static ProtocolVersionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ProtocolVersionManager();
                }
                return _instance;
            }
        }

        private ProtocolVersionManager()
        {
            _supportedVersions = new Dictionary<string, ProtocolVersion>();
            InitializeSupportedVersions();
            _currentVersion = _supportedVersions["2.1"];
        }

        /// <summary>
        /// Gets the current protocol version
        /// </summary>
        public ProtocolVersion CurrentVersion => _currentVersion;

        /// <summary>
        /// Gets all supported protocol versions
        /// </summary>
        public IEnumerable<ProtocolVersion> SupportedVersions => _supportedVersions.Values;

        /// <summary>
        /// Initializes supported protocol versions
        /// </summary>
        private void InitializeSupportedVersions()
        {
            // LocalSend Protocol v1.0 (Legacy)
            _supportedVersions["1.0"] = new ProtocolVersion
            {
                Version = "1.0",
                MajorVersion = 1,
                MinorVersion = 0,
                IsDeprecated = true,
                SupportedFeatures = new[]
                {
                    ProtocolFeature.BasicFileTransfer,
                    ProtocolFeature.DeviceDiscovery
                },
                RequiredHeaders = new[] { "Content-Type" },
                OptionalHeaders = new[] { "User-Agent" },
                MaxPayloadSize = 1024 * 1024 * 50, // 50MB
                SupportedMethods = new[] { "GET", "POST" }
            };

            // LocalSend Protocol v2.0 (Current in original LocalSend)
            _supportedVersions["2.0"] = new ProtocolVersion
            {
                Version = "2.0",
                MajorVersion = 2,
                MinorVersion = 0,
                IsDeprecated = false,
                SupportedFeatures = new[]
                {
                    ProtocolFeature.BasicFileTransfer,
                    ProtocolFeature.DeviceDiscovery,
                    ProtocolFeature.SessionManagement,
                    ProtocolFeature.MultipleFiles,
                    ProtocolFeature.ProgressTracking
                },
                RequiredHeaders = new[] { "Content-Type", "X-LocalSend-Version" },
                OptionalHeaders = new[] { "User-Agent", "Authorization" },
                MaxPayloadSize = 1024 * 1024 * 100, // 100MB
                SupportedMethods = new[] { "GET", "POST", "OPTIONS" }
            };

            // LocalSend Protocol v2.1 (Enhanced with security)
            _supportedVersions["2.1"] = new ProtocolVersion
            {
                Version = "2.1",
                MajorVersion = 2,
                MinorVersion = 1,
                IsDeprecated = false,
                SupportedFeatures = new[]
                {
                    ProtocolFeature.BasicFileTransfer,
                    ProtocolFeature.DeviceDiscovery,
                    ProtocolFeature.SessionManagement,
                    ProtocolFeature.MultipleFiles,
                    ProtocolFeature.ProgressTracking,
                    ProtocolFeature.ReplayProtection,
                    ProtocolFeature.EnhancedSecurity,
                    ProtocolFeature.ChunkedTransfer
                },
                RequiredHeaders = new[] { "Content-Type", "X-LocalSend-Version" },
                OptionalHeaders = new[] { "User-Agent", "Authorization", "X-Timestamp", "X-Nonce" },
                MaxPayloadSize = long.MaxValue, // No practical limit
                SupportedMethods = new[] { "GET", "POST", "PUT", "DELETE", "OPTIONS" }
            };
        }

        /// <summary>
        /// Negotiates the best protocol version with a remote device
        /// </summary>
        public ProtocolNegotiationResult NegotiateVersion(Device remoteDevice)
        {
            var result = new ProtocolNegotiationResult
            {
                RemoteDevice = remoteDevice,
                RequestedVersion = remoteDevice.version.ToString(),
                Success = false
            };

            try
            {
                // Parse remote device version
                var remoteVersionStr = remoteDevice.version.ToString();
                var remoteMajor = (int)Math.Floor(remoteDevice.version);
                var remoteMinor = (int)((remoteDevice.version - remoteMajor) * 10);

                // Find best compatible version
                var compatibleVersions = _supportedVersions.Values
                    .Where(v => IsCompatible(v, remoteMajor, remoteMinor))
                    .OrderByDescending(v => v.MajorVersion)
                    .ThenByDescending(v => v.MinorVersion)
                    .ToList();

                if (compatibleVersions.Any())
                {
                    result.NegotiatedVersion = compatibleVersions.First();
                    result.Success = true;
                    result.CompatibilityLevel = DetermineCompatibilityLevel(result.NegotiatedVersion, remoteMajor, remoteMinor);
                }
                else
                {
                    result.ErrorMessage = $"No compatible protocol version found for remote version {remoteVersionStr}";
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error during version negotiation: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Checks if a protocol version is compatible with remote version
        /// </summary>
        private bool IsCompatible(ProtocolVersion localVersion, int remoteMajor, int remoteMinor)
        {
            // Major version must match for compatibility
            if (localVersion.MajorVersion != remoteMajor)
            {
                return false;
            }

            // Minor version compatibility: we support equal or lower minor versions
            return localVersion.MinorVersion >= remoteMinor;
        }

        /// <summary>
        /// Determines the compatibility level between versions
        /// </summary>
        private CompatibilityLevel DetermineCompatibilityLevel(ProtocolVersion negotiatedVersion, int remoteMajor, int remoteMinor)
        {
            if (negotiatedVersion.MajorVersion == remoteMajor && negotiatedVersion.MinorVersion == remoteMinor)
            {
                return CompatibilityLevel.Full;
            }
            else if (negotiatedVersion.MajorVersion == remoteMajor)
            {
                return CompatibilityLevel.Partial;
            }
            else
            {
                return CompatibilityLevel.Limited;
            }
        }

        /// <summary>
        /// Gets a protocol version by version string
        /// </summary>
        public ProtocolVersion GetVersion(string versionString)
        {
            _supportedVersions.TryGetValue(versionString, out var version);
            return version;
        }

        /// <summary>
        /// Checks if a feature is supported in a specific version
        /// </summary>
        public bool IsFeatureSupported(string versionString, ProtocolFeature feature)
        {
            var version = GetVersion(versionString);
            return version?.SupportedFeatures.Contains(feature) == true;
        }

        /// <summary>
        /// Gets the minimum version that supports a specific feature
        /// </summary>
        public ProtocolVersion GetMinimumVersionForFeature(ProtocolFeature feature)
        {
            return _supportedVersions.Values
                .Where(v => v.SupportedFeatures.Contains(feature))
                .OrderBy(v => v.MajorVersion)
                .ThenBy(v => v.MinorVersion)
                .FirstOrDefault();
        }

        /// <summary>
        /// Validates if a request is compatible with the negotiated version
        /// </summary>
        public ValidationResult ValidateRequest(ProtocolVersion version, string method, Dictionary<string, string> headers)
        {
            var result = new ValidationResult { IsValid = true };

            // Check if method is supported
            if (!version.SupportedMethods.Contains(method.ToUpperInvariant()))
            {
                result.IsValid = false;
                result.Errors.Add($"Method {method} not supported in version {version.Version}");
            }

            // Check required headers
            foreach (var requiredHeader in version.RequiredHeaders)
            {
                if (!headers.ContainsKey(requiredHeader))
                {
                    result.IsValid = false;
                    result.Errors.Add($"Required header {requiredHeader} missing for version {version.Version}");
                }
            }

            // Validate version header if present
            if (headers.TryGetValue("X-LocalSend-Version", out var versionHeader))
            {
                if (versionHeader != version.Version)
                {
                    result.Warnings.Add($"Version header mismatch: expected {version.Version}, got {versionHeader}");
                }
            }

            return result;
        }

        /// <summary>
        /// Adds version-specific headers to a request
        /// </summary>
        public void AddVersionHeaders(ProtocolVersion version, Dictionary<string, string> headers)
        {
            headers["X-LocalSend-Version"] = version.Version;
            
            // Add version-specific headers
            if (version.SupportedFeatures.Contains(ProtocolFeature.ReplayProtection))
            {
                // Replay protection headers will be added by ReplayAttackDetector
            }

            if (version.SupportedFeatures.Contains(ProtocolFeature.EnhancedSecurity))
            {
                headers["X-Security-Level"] = "enhanced";
            }
        }

        /// <summary>
        /// Gets feature compatibility information
        /// </summary>
        public FeatureCompatibilityInfo GetFeatureCompatibility(ProtocolVersion localVersion, ProtocolVersion remoteVersion)
        {
            var info = new FeatureCompatibilityInfo
            {
                LocalVersion = localVersion,
                RemoteVersion = remoteVersion
            };

            var allFeatures = Enum.GetValues(typeof(ProtocolFeature)).Cast<ProtocolFeature>();
            
            foreach (var feature in allFeatures)
            {
                var localSupports = localVersion.SupportedFeatures.Contains(feature);
                var remoteSupports = remoteVersion.SupportedFeatures.Contains(feature);

                if (localSupports && remoteSupports)
                {
                    info.SupportedFeatures.Add(feature);
                }
                else if (localSupports && !remoteSupports)
                {
                    info.LocalOnlyFeatures.Add(feature);
                }
                else if (!localSupports && remoteSupports)
                {
                    info.RemoteOnlyFeatures.Add(feature);
                }
            }

            return info;
        }
    }

    /// <summary>
    /// Protocol version information
    /// </summary>
    public class ProtocolVersion
    {
        public string Version { get; set; }
        public int MajorVersion { get; set; }
        public int MinorVersion { get; set; }
        public bool IsDeprecated { get; set; }
        public ProtocolFeature[] SupportedFeatures { get; set; }
        public string[] RequiredHeaders { get; set; }
        public string[] OptionalHeaders { get; set; }
        public long MaxPayloadSize { get; set; }
        public string[] SupportedMethods { get; set; }
    }

    /// <summary>
    /// Protocol features enumeration
    /// </summary>
    public enum ProtocolFeature
    {
        BasicFileTransfer,
        DeviceDiscovery,
        SessionManagement,
        MultipleFiles,
        ProgressTracking,
        ReplayProtection,
        EnhancedSecurity,
        ChunkedTransfer,
        FileResume,
        Compression,
        Encryption,
        Thumbnails,
        Metadata
    }

    /// <summary>
    /// Protocol negotiation result
    /// </summary>
    public class ProtocolNegotiationResult
    {
        public Device RemoteDevice { get; set; }
        public string RequestedVersion { get; set; }
        public ProtocolVersion NegotiatedVersion { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public CompatibilityLevel CompatibilityLevel { get; set; }
    }

    /// <summary>
    /// Compatibility levels
    /// </summary>
    public enum CompatibilityLevel
    {
        None,
        Limited,
        Partial,
        Full
    }

    /// <summary>
    /// Request validation result
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Feature compatibility information
    /// </summary>
    public class FeatureCompatibilityInfo
    {
        public ProtocolVersion LocalVersion { get; set; }
        public ProtocolVersion RemoteVersion { get; set; }
        public List<ProtocolFeature> SupportedFeatures { get; set; } = new List<ProtocolFeature>();
        public List<ProtocolFeature> LocalOnlyFeatures { get; set; } = new List<ProtocolFeature>();
        public List<ProtocolFeature> RemoteOnlyFeatures { get; set; } = new List<ProtocolFeature>();
    }
}
