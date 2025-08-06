using Shared.Platform;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Shared.Security
{
    /// <summary>
    /// Manages SSL/TLS certificates for secure HTTPS communication
    /// </summary>
    public class CertificateManager
    {
        private static CertificateManager _instance;
        private readonly ICryptographyProvider _cryptoProvider;
        private readonly Dictionary<string, ICertificate> _certificateCache;
        private ICertificate _serverCertificate;

        public static CertificateManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CertificateManager();
                }
                return _instance;
            }
        }

        private CertificateManager()
        {
            _cryptoProvider = PlatformFactory.Current.GetCryptographyProvider();
            _certificateCache = new Dictionary<string, ICertificate>();
        }

        /// <summary>
        /// Gets or creates the server certificate for HTTPS
        /// </summary>
        public async Task<ICertificate> GetServerCertificateAsync()
        {
            if (_serverCertificate == null)
            {
                await CreateServerCertificateAsync();
            }
            return _serverCertificate;
        }

        /// <summary>
        /// Creates a new self-signed certificate for the server
        /// </summary>
        private async Task CreateServerCertificateAsync()
        {
            await Task.Run(() =>
            {
                var deviceName = Settings.DeviceName ?? "LocalTalk";
                var subjectName = $"CN={deviceName}, O=LocalTalk, C=US";
                
                _serverCertificate = _cryptoProvider.GenerateSelfSignedCertificate(subjectName);
                
                // Cache the certificate
                _certificateCache[deviceName] = _serverCertificate;
                
                System.Diagnostics.Debug.WriteLine($"Generated server certificate: {_serverCertificate.Subject}");
                System.Diagnostics.Debug.WriteLine($"Certificate thumbprint: {_serverCertificate.Thumbprint}");
            });
        }

        /// <summary>
        /// Gets the fingerprint for the current server certificate
        /// </summary>
        public async Task<string> GetServerFingerprintAsync()
        {
            var certificate = await GetServerCertificateAsync();
            return _cryptoProvider.GetCertificateFingerprint(certificate);
        }

        /// <summary>
        /// Validates a certificate fingerprint against known certificates
        /// </summary>
        public bool ValidateCertificateFingerprint(string fingerprint, string deviceName)
        {
            if (string.IsNullOrEmpty(fingerprint) || string.IsNullOrEmpty(deviceName))
            {
                return false;
            }

            // Check if we have a cached certificate for this device
            if (_certificateCache.TryGetValue(deviceName, out var cachedCert))
            {
                var cachedFingerprint = _cryptoProvider.GetCertificateFingerprint(cachedCert);
                return string.Equals(fingerprint, cachedFingerprint, StringComparison.OrdinalIgnoreCase);
            }

            // For first-time connections, we'll need to implement trust-on-first-use (TOFU)
            return false;
        }

        /// <summary>
        /// Adds a trusted certificate for a device (Trust-On-First-Use)
        /// </summary>
        public void TrustCertificate(string deviceName, string fingerprint)
        {
            if (string.IsNullOrEmpty(deviceName) || string.IsNullOrEmpty(fingerprint))
            {
                return;
            }

            // Create a mock certificate entry for the trusted fingerprint
            var trustedCert = new TrustedCertificateInfo(deviceName, fingerprint);
            
            // Store in persistent settings
            var trustedCerts = GetTrustedCertificates();
            trustedCerts[deviceName] = fingerprint;
            SaveTrustedCertificates(trustedCerts);

            System.Diagnostics.Debug.WriteLine($"Trusted certificate for {deviceName}: {fingerprint}");
        }

        /// <summary>
        /// Removes trust for a device certificate
        /// </summary>
        public void RemoveTrustedCertificate(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                return;
            }

            var trustedCerts = GetTrustedCertificates();
            if (trustedCerts.Remove(deviceName))
            {
                SaveTrustedCertificates(trustedCerts);
                _certificateCache.Remove(deviceName);
                
                System.Diagnostics.Debug.WriteLine($"Removed trusted certificate for {deviceName}");
            }
        }

        /// <summary>
        /// Gets all trusted certificate fingerprints
        /// </summary>
        public Dictionary<string, string> GetTrustedCertificates()
        {
            var trustedCerts = new Dictionary<string, string>();
            
            try
            {
                // Load from persistent storage
                var serializedCerts = Settings.GetSetting<string>("TrustedCertificates");
                if (!string.IsNullOrEmpty(serializedCerts))
                {
                    var pairs = serializedCerts.Split(';');
                    foreach (var pair in pairs)
                    {
                        var parts = pair.Split('=');
                        if (parts.Length == 2)
                        {
                            trustedCerts[parts[0]] = parts[1];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading trusted certificates: {ex.Message}");
            }
            
            return trustedCerts;
        }

        /// <summary>
        /// Saves trusted certificates to persistent storage
        /// </summary>
        private void SaveTrustedCertificates(Dictionary<string, string> trustedCerts)
        {
            try
            {
                var serialized = string.Join(";", 
                    System.Linq.Enumerable.Select(trustedCerts, kvp => $"{kvp.Key}={kvp.Value}"));
                Settings.SetSetting("TrustedCertificates", serialized);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving trusted certificates: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a certificate is expired
        /// </summary>
        public bool IsCertificateExpired(ICertificate certificate)
        {
            return DateTime.Now > certificate.NotAfter;
        }

        /// <summary>
        /// Checks if a certificate will expire soon (within 30 days)
        /// </summary>
        public bool IsCertificateExpiringSoon(ICertificate certificate)
        {
            return DateTime.Now.AddDays(30) > certificate.NotAfter;
        }

        /// <summary>
        /// Regenerates the server certificate (e.g., when device name changes)
        /// </summary>
        public async Task RegenerateServerCertificateAsync()
        {
            _serverCertificate = null;
            await CreateServerCertificateAsync();
        }

        /// <summary>
        /// Clears all cached certificates
        /// </summary>
        public void ClearCertificateCache()
        {
            _certificateCache.Clear();
            _serverCertificate = null;
        }
    }

    /// <summary>
    /// Information about a trusted certificate
    /// </summary>
    internal class TrustedCertificateInfo
    {
        public string DeviceName { get; }
        public string Fingerprint { get; }
        public DateTime TrustedDate { get; }

        public TrustedCertificateInfo(string deviceName, string fingerprint)
        {
            DeviceName = deviceName;
            Fingerprint = fingerprint;
            TrustedDate = DateTime.Now;
        }
    }

    /// <summary>
    /// Certificate validation result
    /// </summary>
    public enum CertificateValidationResult
    {
        Valid,
        Expired,
        Untrusted,
        Invalid,
        NotFound
    }

    /// <summary>
    /// Certificate validation context
    /// </summary>
    public class CertificateValidationContext
    {
        public string DeviceName { get; set; }
        public string Fingerprint { get; set; }
        public ICertificate Certificate { get; set; }
        public CertificateValidationResult Result { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsFirstConnection { get; set; }
    }
}
