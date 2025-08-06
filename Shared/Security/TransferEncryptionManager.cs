using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Shared.Platform;

namespace Shared.Security
{
    /// <summary>
    /// Manages end-to-end encryption for file transfers with secure key exchange
    /// </summary>
    public class TransferEncryptionManager : IDisposable
    {
        private static TransferEncryptionManager _instance;
        private readonly ConcurrentDictionary<string, EncryptionSession> _encryptionSessions;
        private readonly ICryptographyProvider _cryptoProvider;
        private readonly object _lockObject = new object();
        private bool _disposed;

        public static TransferEncryptionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TransferEncryptionManager();
                }
                return _instance;
            }
        }

        public event EventHandler<KeyExchangeEventArgs> KeyExchangeCompleted;
        public event EventHandler<EncryptionErrorEventArgs> EncryptionError;

        private TransferEncryptionManager()
        {
            _encryptionSessions = new ConcurrentDictionary<string, EncryptionSession>();
            _cryptoProvider = PlatformFactory.Current.GetCryptographyProvider();
        }

        /// <summary>
        /// Initiates a secure key exchange for a transfer session
        /// </summary>
        /// <param name="sessionId">Unique identifier for the transfer session</param>
        /// <param name="remoteEndpoint">Remote endpoint for the key exchange</param>
        /// <returns>Key exchange result containing public key information</returns>
        /// <exception cref="ArgumentNullException">Thrown when sessionId or remoteEndpoint is null or empty</exception>
        /// <exception cref="InvalidOperationException">Thrown when session already exists or manager is disposed</exception>
        public async Task<KeyExchangeResult> InitiateKeyExchangeAsync(string sessionId, string remoteEndpoint)
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentNullException(nameof(sessionId), "Session ID cannot be null or empty");

            if (string.IsNullOrWhiteSpace(remoteEndpoint))
                throw new ArgumentNullException(nameof(remoteEndpoint), "Remote endpoint cannot be null or empty");

            if (_disposed)
                throw new ObjectDisposedException(nameof(TransferEncryptionManager));

            if (_encryptionSessions.ContainsKey(sessionId))
                throw new InvalidOperationException($"Encryption session with ID '{sessionId}' already exists");

            try
            {
                // Generate ephemeral key pair for ECDH with enhanced security validation
                using (var ecdh = ECDiffieHellman.Create())
                {
                    // Use stronger curve for enhanced security
                    ecdh.KeySize = 384; // Use P-384 curve for better security

                    // Validate that the curve was set correctly
                    if (ecdh.KeySize < 256)
                        throw new InvalidOperationException("Failed to set minimum required key size for ECDH");

                    var publicKey = ecdh.PublicKey.ToByteArray();

                    // Validate public key generation
                    if (publicKey == null || publicKey.Length == 0)
                        throw new InvalidOperationException("Failed to generate valid ECDH public key");

                    // Ensure public key meets minimum security requirements
                    if (publicKey.Length < 64) // Minimum for P-256
                        throw new InvalidOperationException("Generated public key does not meet minimum security requirements");

                    var publicKeyBase64 = Convert.ToBase64String(publicKey);

                    // Export and validate private key
                    var privateKey = ecdh.ExportECPrivateKey();
                    if (privateKey == null || privateKey.Length == 0)
                        throw new InvalidOperationException("Failed to export valid ECDH private key");

                    // Create encryption session with enhanced security metadata
                    var session = new EncryptionSession
                    {
                        SessionId = sessionId,
                        RemoteEndpoint = remoteEndpoint,
                        LocalPrivateKey = privateKey,
                        LocalPublicKey = publicKey,
                        Status = EncryptionStatus.KeyExchangePending,
                        CreatedAt = DateTime.Now,
                        KeyStrength = DetermineKeyStrength(ecdh.KeySize),
                        SecurityLevel = SecurityLevel.High // Default to high security
                    };

                    _encryptionSessions[sessionId] = session;

                    return new KeyExchangeResult
                    {
                        Success = true,
                        SessionId = sessionId,
                        PublicKey = publicKeyBase64,
                        KeyExchangeId = Guid.NewGuid().ToString()
                    };
                }
            }
            catch (Exception ex)
            {
                OnEncryptionError(new EncryptionErrorEventArgs
                {
                    SessionId = sessionId,
                    ErrorMessage = $"Key exchange initiation failed: {ex.Message}",
                    Exception = ex
                });

                return new KeyExchangeResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Completes the key exchange with the remote party's public key
        /// </summary>
        public async Task<bool> CompleteKeyExchangeAsync(string sessionId, string remotePublicKeyBase64)
        {
            try
            {
                if (!_encryptionSessions.TryGetValue(sessionId, out var session))
                {
                    throw new InvalidOperationException("Encryption session not found");
                }

                var remotePublicKey = Convert.FromBase64String(remotePublicKeyBase64);

                // Perform ECDH key agreement
                using (var ecdh = ECDiffieHellman.Create())
                {
                    ecdh.ImportECPrivateKey(session.LocalPrivateKey, out _);

                    using (var remoteEcdh = ECDiffieHellman.Create())
                    {
                        remoteEcdh.ImportSubjectPublicKeyInfo(remotePublicKey, out _);

                        // Derive shared secret
                        var sharedSecret = ecdh.DeriveKeyMaterial(remoteEcdh.PublicKey);

                        // Derive encryption keys using HKDF
                        var keys = DeriveEncryptionKeys(sharedSecret, sessionId);

                        session.EncryptionKey = keys.EncryptionKey;
                        session.MacKey = keys.MacKey;
                        session.RemotePublicKey = remotePublicKey;
                        session.Status = EncryptionStatus.Ready;

                        OnKeyExchangeCompleted(new KeyExchangeEventArgs
                        {
                            SessionId = sessionId,
                            Success = true
                        });

                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                OnEncryptionError(new EncryptionErrorEventArgs
                {
                    SessionId = sessionId,
                    ErrorMessage = $"Key exchange completion failed: {ex.Message}",
                    Exception = ex
                });

                return false;
            }
        }

        /// <summary>
        /// Encrypts data for transfer
        /// </summary>
        /// <param name="sessionId">The session identifier</param>
        /// <param name="data">The data to encrypt</param>
        /// <returns>Encrypted data with MAC</returns>
        /// <exception cref="ArgumentNullException">Thrown when sessionId or data is null</exception>
        /// <exception cref="ArgumentException">Thrown when data is empty</exception>
        /// <exception cref="InvalidOperationException">Thrown when session is not ready or manager is disposed</exception>
        public async Task<EncryptedData> EncryptDataAsync(string sessionId, byte[] data)
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentNullException(nameof(sessionId), "Session ID cannot be null or empty");

            if (data == null)
                throw new ArgumentNullException(nameof(data), "Data cannot be null");

            if (data.Length == 0)
                throw new ArgumentException("Data cannot be empty", nameof(data));

            if (_disposed)
                throw new ObjectDisposedException(nameof(TransferEncryptionManager));

            try
            {
                if (!_encryptionSessions.TryGetValue(sessionId, out var session) || session.Status != EncryptionStatus.Ready)
                {
                    throw new InvalidOperationException($"Encryption session '{sessionId}' is not ready");
                }

                // Generate random IV
                var iv = new byte[16];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(iv);
                }

                // Encrypt data using AES-256-GCM
                using (var aes = new AesGcm(session.EncryptionKey))
                {
                    var ciphertext = new byte[data.Length];
                    var tag = new byte[16]; // GCM authentication tag

                    aes.Encrypt(iv, data, ciphertext, tag);

                    // Create encrypted data package
                    var encryptedData = new EncryptedData
                    {
                        SessionId = sessionId,
                        IV = iv,
                        Ciphertext = ciphertext,
                        AuthenticationTag = tag,
                        Timestamp = DateTime.Now
                    };

                    // Calculate HMAC for additional integrity protection
                    encryptedData.HMAC = CalculateHMAC(session.MacKey, encryptedData);

                    return encryptedData;
                }
            }
            catch (Exception ex)
            {
                OnEncryptionError(new EncryptionErrorEventArgs
                {
                    SessionId = sessionId,
                    ErrorMessage = $"Data encryption failed: {ex.Message}",
                    Exception = ex
                });

                throw;
            }
        }

        /// <summary>
        /// Decrypts received data
        /// </summary>
        public async Task<byte[]> DecryptDataAsync(string sessionId, EncryptedData encryptedData)
        {
            try
            {
                if (!_encryptionSessions.TryGetValue(sessionId, out var session) || session.Status != EncryptionStatus.Ready)
                {
                    throw new InvalidOperationException("Encryption session not ready");
                }

                // Verify HMAC
                var expectedHmac = CalculateHMAC(session.MacKey, encryptedData);
                if (!ConstantTimeEquals(expectedHmac, encryptedData.HMAC))
                {
                    throw new CryptographicException("HMAC verification failed - data may have been tampered with");
                }

                // Decrypt data using AES-256-GCM
                using (var aes = new AesGcm(session.EncryptionKey))
                {
                    var plaintext = new byte[encryptedData.Ciphertext.Length];

                    aes.Decrypt(encryptedData.IV, encryptedData.Ciphertext, encryptedData.AuthenticationTag, plaintext);

                    return plaintext;
                }
            }
            catch (Exception ex)
            {
                OnEncryptionError(new EncryptionErrorEventArgs
                {
                    SessionId = sessionId,
                    ErrorMessage = $"Data decryption failed: {ex.Message}",
                    Exception = ex
                });

                throw;
            }
        }

        /// <summary>
        /// Creates an encrypted stream for large file transfers
        /// </summary>
        /// <param name="sessionId">The session identifier</param>
        /// <param name="baseStream">The base stream to encrypt</param>
        /// <param name="forWriting">Whether the stream is for writing (true) or reading (false)</param>
        /// <returns>An encrypted stream wrapper</returns>
        /// <exception cref="ArgumentNullException">Thrown when sessionId or baseStream is null</exception>
        /// <exception cref="InvalidOperationException">Thrown when session is not ready or manager is disposed</exception>
        public Stream CreateEncryptedStream(string sessionId, Stream baseStream, bool forWriting)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentNullException(nameof(sessionId), "Session ID cannot be null or empty");

            if (baseStream == null)
                throw new ArgumentNullException(nameof(baseStream), "Base stream cannot be null");

            if (_disposed)
                throw new ObjectDisposedException(nameof(TransferEncryptionManager));

            if (!_encryptionSessions.TryGetValue(sessionId, out var session) || session.Status != EncryptionStatus.Ready)
            {
                throw new InvalidOperationException($"Encryption session '{sessionId}' is not ready");
            }

            try
            {
                return new EncryptedStream(baseStream, session.EncryptionKey, session.MacKey, forWriting);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create encrypted stream for session '{sessionId}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Removes an encryption session
        /// </summary>
        /// <param name="sessionId">The session identifier to remove</param>
        /// <exception cref="ArgumentNullException">Thrown when sessionId is null or empty</exception>
        public void RemoveSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentNullException(nameof(sessionId), "Session ID cannot be null or empty");

            if (_disposed)
            {
                // If disposed, just log and return - don't throw
                System.Diagnostics.Debug.WriteLine($"Attempted to remove session '{sessionId}' from disposed TransferEncryptionManager");
                return;
            }

            try
            {
                if (_encryptionSessions.TryRemove(sessionId, out var session))
                {
                    session?.Dispose();
                    System.Diagnostics.Debug.WriteLine($"Removed encryption session '{sessionId}'");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Encryption session '{sessionId}' not found for removal");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing encryption session '{sessionId}': {ex.Message}");
                // Don't rethrow - session cleanup should be resilient
            }
        }

        /// <summary>
        /// Derives encryption keys from shared secret using HKDF
        /// </summary>
        private EncryptionKeys DeriveEncryptionKeys(byte[] sharedSecret, string sessionId)
        {
            var salt = Encoding.UTF8.GetBytes($"LocalTalk-{sessionId}");
            var info = Encoding.UTF8.GetBytes("LocalTalk-FileTransfer-v1");

            // Use HKDF to derive 64 bytes (32 for encryption, 32 for MAC)
            var keyMaterial = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 64, salt, info);

            return new EncryptionKeys
            {
                EncryptionKey = keyMaterial[0..32],
                MacKey = keyMaterial[32..64]
            };
        }

        /// <summary>
        /// Calculates HMAC for encrypted data
        /// </summary>
        private byte[] CalculateHMAC(byte[] key, EncryptedData data)
        {
            using (var hmac = new HMACSHA256(key))
            {
                using (var ms = new MemoryStream())
                {
                    ms.Write(data.IV);
                    ms.Write(data.Ciphertext);
                    ms.Write(data.AuthenticationTag);
                    ms.Write(BitConverter.GetBytes(data.Timestamp.ToBinary()));

                    ms.Position = 0;
                    return hmac.ComputeHash(ms);
                }
            }
        }

        /// <summary>
        /// Constant-time comparison to prevent timing attacks
        /// </summary>
        private bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;

            int result = 0;
            for (int i = 0; i < a.Length; i++)
            {
                result |= a[i] ^ b[i];
            }

            return result == 0;
        }

        private void OnKeyExchangeCompleted(KeyExchangeEventArgs args)
        {
            KeyExchangeCompleted?.Invoke(this, args);
        }

        private void OnEncryptionError(EncryptionErrorEventArgs args)
        {
            EncryptionError?.Invoke(this, args);
        }

        /// <summary>
        /// Determines the key strength based on key size
        /// </summary>
        private KeyStrength DetermineKeyStrength(int keySize)
        {
            return keySize switch
            {
                >= 384 => KeyStrength.VeryHigh,
                >= 256 => KeyStrength.High,
                >= 224 => KeyStrength.Medium,
                _ => KeyStrength.Low
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var session in _encryptionSessions.Values)
                {
                    session.Dispose();
                }
                _encryptionSessions.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Encryption session information
    /// </summary>
    internal class EncryptionSession : IDisposable
    {
        public string SessionId { get; set; }
        public string RemoteEndpoint { get; set; }
        public byte[] LocalPrivateKey { get; set; }
        public byte[] LocalPublicKey { get; set; }
        public byte[] RemotePublicKey { get; set; }
        public byte[] EncryptionKey { get; set; }
        public byte[] MacKey { get; set; }
        public EncryptionStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public KeyStrength KeyStrength { get; set; }
        public SecurityLevel SecurityLevel { get; set; }

        public void Dispose()
        {
            // Clear sensitive data
            if (LocalPrivateKey != null)
            {
                Array.Clear(LocalPrivateKey, 0, LocalPrivateKey.Length);
            }
            if (EncryptionKey != null)
            {
                Array.Clear(EncryptionKey, 0, EncryptionKey.Length);
            }
            if (MacKey != null)
            {
                Array.Clear(MacKey, 0, MacKey.Length);
            }
        }
    }

    /// <summary>
    /// Derived encryption keys
    /// </summary>
    internal class EncryptionKeys
    {
        public byte[] EncryptionKey { get; set; }
        public byte[] MacKey { get; set; }
    }

    /// <summary>
    /// Encryption status enumeration
    /// </summary>
    public enum EncryptionStatus
    {
        KeyExchangePending,
        Ready,
        Failed
    }

    /// <summary>
    /// Key strength enumeration for security assessment
    /// </summary>
    public enum KeyStrength
    {
        Low,
        Medium,
        High,
        VeryHigh
    }

    /// <summary>
    /// Key exchange result
    /// </summary>
    public class KeyExchangeResult
    {
        public bool Success { get; set; }
        public string SessionId { get; set; }
        public string PublicKey { get; set; }
        public string KeyExchangeId { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Encrypted data package
    /// </summary>
    public class EncryptedData
    {
        public string SessionId { get; set; }
        public byte[] IV { get; set; }
        public byte[] Ciphertext { get; set; }
        public byte[] AuthenticationTag { get; set; }
        public byte[] HMAC { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Key exchange event arguments
    /// </summary>
    public class KeyExchangeEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Encryption error event arguments
    /// </summary>
    public class EncryptionErrorEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
    }
}
