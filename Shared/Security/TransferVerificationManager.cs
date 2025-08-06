using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Security
{
    /// <summary>
    /// Comprehensive transfer verification using cryptographic hashes, digital signatures, and integrity validation
    /// </summary>
    public class TransferVerificationManager : IDisposable
    {
        private static TransferVerificationManager _instance;
        private readonly ConcurrentDictionary<string, VerificationSession> _verificationSessions;
        private readonly TransferVerificationConfiguration _config;
        private bool _disposed;

        public static TransferVerificationManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TransferVerificationManager();
                }
                return _instance;
            }
        }

        public event EventHandler<VerificationStartedEventArgs> VerificationStarted;
        public event EventHandler<VerificationProgressEventArgs> VerificationProgress;
        public event EventHandler<VerificationCompletedEventArgs> VerificationCompleted;
        public event EventHandler<VerificationFailedEventArgs> VerificationFailed;

        private TransferVerificationManager()
        {
            _verificationSessions = new ConcurrentDictionary<string, VerificationSession>();
            _config = new TransferVerificationConfiguration();
        }

        /// <summary>
        /// Starts verification for a transfer
        /// </summary>
        public async Task<string> StartVerificationAsync(VerificationRequest request)
        {
            var sessionId = Guid.NewGuid().ToString();
            var session = new VerificationSession
            {
                SessionId = sessionId,
                Request = request,
                StartTime = DateTime.Now,
                Status = VerificationStatus.InProgress,
                HashAlgorithms = request.HashAlgorithms ?? new[] { HashAlgorithmType.SHA256 },
                ComputedHashes = new Dictionary<HashAlgorithmType, string>(),
                ChunkHashes = new List<ChunkHash>()
            };

            _verificationSessions[sessionId] = session;

            OnVerificationStarted(new VerificationStartedEventArgs
            {
                SessionId = sessionId,
                FileName = request.FileName,
                FileSize = request.FileSize,
                HashAlgorithms = session.HashAlgorithms
            });

            // Start verification process
            _ = Task.Run(async () => await PerformVerificationAsync(session));

            return sessionId;
        }

        /// <summary>
        /// Verifies a file chunk during transfer
        /// </summary>
        public async Task<ChunkVerificationResult> VerifyChunkAsync(string sessionId, int chunkIndex, byte[] chunkData, string expectedHash = null)
        {
            if (!_verificationSessions.TryGetValue(sessionId, out var session))
            {
                return new ChunkVerificationResult
                {
                    Success = false,
                    ErrorMessage = "Verification session not found"
                };
            }

            try
            {
                // Calculate chunk hash
                var chunkHash = await CalculateHashAsync(chunkData, HashAlgorithmType.SHA256);
                
                var chunkVerification = new ChunkHash
                {
                    ChunkIndex = chunkIndex,
                    Hash = chunkHash,
                    Size = chunkData.Length,
                    Timestamp = DateTime.Now
                };

                session.ChunkHashes.Add(chunkVerification);

                // Verify against expected hash if provided
                var isValid = expectedHash == null || string.Equals(chunkHash, expectedHash, StringComparison.OrdinalIgnoreCase);

                OnVerificationProgress(new VerificationProgressEventArgs
                {
                    SessionId = sessionId,
                    ChunkIndex = chunkIndex,
                    ChunkHash = chunkHash,
                    IsValid = isValid,
                    Progress = (double)(chunkIndex + 1) / session.Request.TotalChunks * 100
                });

                return new ChunkVerificationResult
                {
                    Success = isValid,
                    ChunkIndex = chunkIndex,
                    ComputedHash = chunkHash,
                    ExpectedHash = expectedHash,
                    ErrorMessage = isValid ? null : "Chunk hash mismatch"
                };
            }
            catch (Exception ex)
            {
                return new ChunkVerificationResult
                {
                    Success = false,
                    ChunkIndex = chunkIndex,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Completes verification for a transfer
        /// </summary>
        public async Task<VerificationResult> CompleteVerificationAsync(string sessionId, string filePath = null)
        {
            if (!_verificationSessions.TryGetValue(sessionId, out var session))
            {
                return new VerificationResult
                {
                    Success = false,
                    ErrorMessage = "Verification session not found"
                };
            }

            try
            {
                session.EndTime = DateTime.Now;
                session.Status = VerificationStatus.Completed;

                // Calculate final file hashes if file path provided
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    await CalculateFileHashesAsync(session, filePath);
                }

                // Verify chunk integrity
                var chunkIntegrityResult = VerifyChunkIntegrity(session);
                
                // Create verification result
                var result = new VerificationResult
                {
                    SessionId = sessionId,
                    Success = chunkIntegrityResult.Success,
                    ComputedHashes = session.ComputedHashes,
                    ChunkCount = session.ChunkHashes.Count,
                    TotalSize = session.ChunkHashes.Sum(c => c.Size),
                    Duration = session.EndTime - session.StartTime,
                    ErrorMessage = chunkIntegrityResult.ErrorMessage
                };

                // Verify against expected hashes if provided
                if (session.Request.ExpectedHashes != null)
                {
                    result.Success = result.Success && VerifyExpectedHashes(session, result);
                }

                // Generate digital signature if requested
                if (session.Request.GenerateSignature && result.Success)
                {
                    result.DigitalSignature = await GenerateDigitalSignatureAsync(session);
                }

                OnVerificationCompleted(new VerificationCompletedEventArgs
                {
                    SessionId = sessionId,
                    Result = result
                });

                return result;
            }
            catch (Exception ex)
            {
                session.Status = VerificationStatus.Failed;
                
                var result = new VerificationResult
                {
                    SessionId = sessionId,
                    Success = false,
                    ErrorMessage = ex.Message
                };

                OnVerificationFailed(new VerificationFailedEventArgs
                {
                    SessionId = sessionId,
                    ErrorMessage = ex.Message,
                    Exception = ex
                });

                return result;
            }
            finally
            {
                _verificationSessions.TryRemove(sessionId, out _);
            }
        }

        /// <summary>
        /// Verifies a digital signature
        /// </summary>
        public async Task<bool> VerifyDigitalSignatureAsync(string filePath, string signature, string publicKey)
        {
            try
            {
                // This is a simplified implementation - in practice, you'd use proper digital signature algorithms
                var fileHash = await CalculateFileHashAsync(filePath, HashAlgorithmType.SHA256);
                var expectedSignature = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{fileHash}:{publicKey}"));
                
                return string.Equals(signature, expectedSignature, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Digital signature verification failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Performs the main verification process
        /// </summary>
        private async Task PerformVerificationAsync(VerificationSession session)
        {
            try
            {
                // If file path is provided, calculate file hashes immediately
                if (!string.IsNullOrEmpty(session.Request.FilePath) && File.Exists(session.Request.FilePath))
                {
                    await CalculateFileHashesAsync(session, session.Request.FilePath);
                }

                // Verification will be completed when CompleteVerificationAsync is called
            }
            catch (Exception ex)
            {
                session.Status = VerificationStatus.Failed;
                OnVerificationFailed(new VerificationFailedEventArgs
                {
                    SessionId = session.SessionId,
                    ErrorMessage = ex.Message,
                    Exception = ex
                });
            }
        }

        /// <summary>
        /// Calculates file hashes for all configured algorithms
        /// </summary>
        private async Task CalculateFileHashesAsync(VerificationSession session, string filePath)
        {
            foreach (var algorithm in session.HashAlgorithms)
            {
                var hash = await CalculateFileHashAsync(filePath, algorithm);
                session.ComputedHashes[algorithm] = hash;
            }
        }

        /// <summary>
        /// Calculates hash for a file
        /// </summary>
        private async Task<string> CalculateFileHashAsync(string filePath, HashAlgorithmType algorithmType)
        {
            using var stream = File.OpenRead(filePath);
            return await CalculateHashAsync(stream, algorithmType);
        }

        /// <summary>
        /// Calculates hash for data
        /// </summary>
        private async Task<string> CalculateHashAsync(byte[] data, HashAlgorithmType algorithmType)
        {
            using var stream = new MemoryStream(data);
            return await CalculateHashAsync(stream, algorithmType);
        }

        /// <summary>
        /// Calculates hash for a stream
        /// </summary>
        private async Task<string> CalculateHashAsync(Stream stream, HashAlgorithmType algorithmType)
        {
            HashAlgorithm hashAlgorithm = algorithmType switch
            {
                HashAlgorithmType.MD5 => MD5.Create(),
                HashAlgorithmType.SHA1 => SHA1.Create(),
                HashAlgorithmType.SHA256 => SHA256.Create(),
                HashAlgorithmType.SHA384 => SHA384.Create(),
                HashAlgorithmType.SHA512 => SHA512.Create(),
                _ => SHA256.Create()
            };

            using (hashAlgorithm)
            {
                var hashBytes = await Task.Run(() => hashAlgorithm.ComputeHash(stream));
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
        }

        /// <summary>
        /// Verifies chunk integrity
        /// </summary>
        private ChunkIntegrityResult VerifyChunkIntegrity(VerificationSession session)
        {
            try
            {
                // Check for missing chunks
                var expectedChunkCount = session.Request.TotalChunks;
                var actualChunkCount = session.ChunkHashes.Count;
                
                if (actualChunkCount != expectedChunkCount)
                {
                    return new ChunkIntegrityResult
                    {
                        Success = false,
                        ErrorMessage = $"Chunk count mismatch: expected {expectedChunkCount}, got {actualChunkCount}"
                    };
                }

                // Check for duplicate chunks
                var duplicateChunks = session.ChunkHashes
                    .GroupBy(c => c.ChunkIndex)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                if (duplicateChunks.Any())
                {
                    return new ChunkIntegrityResult
                    {
                        Success = false,
                        ErrorMessage = $"Duplicate chunks found: {string.Join(", ", duplicateChunks)}"
                    };
                }

                // Check for sequential chunk indices
                var sortedChunks = session.ChunkHashes.OrderBy(c => c.ChunkIndex).ToList();
                for (int i = 0; i < sortedChunks.Count; i++)
                {
                    if (sortedChunks[i].ChunkIndex != i)
                    {
                        return new ChunkIntegrityResult
                        {
                            Success = false,
                            ErrorMessage = $"Missing chunk at index {i}"
                        };
                    }
                }

                return new ChunkIntegrityResult { Success = true };
            }
            catch (Exception ex)
            {
                return new ChunkIntegrityResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Verifies against expected hashes
        /// </summary>
        private bool VerifyExpectedHashes(VerificationSession session, VerificationResult result)
        {
            foreach (var expectedHash in session.Request.ExpectedHashes)
            {
                if (result.ComputedHashes.TryGetValue(expectedHash.Key, out var computedHash))
                {
                    if (!string.Equals(computedHash, expectedHash.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        result.ErrorMessage = $"Hash mismatch for {expectedHash.Key}: expected {expectedHash.Value}, got {computedHash}";
                        return false;
                    }
                }
                else
                {
                    result.ErrorMessage = $"Missing computed hash for {expectedHash.Key}";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Generates a digital signature for the verification session
        /// </summary>
        private async Task<string> GenerateDigitalSignatureAsync(VerificationSession session)
        {
            try
            {
                // Simplified digital signature - in practice, use proper cryptographic signing
                var signatureData = string.Join("|", session.ComputedHashes.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
                var signatureBytes = Encoding.UTF8.GetBytes($"{signatureData}:{DateTime.Now:O}");
                return Convert.ToBase64String(signatureBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Digital signature generation failed: {ex.Message}");
                return null;
            }
        }

        private void OnVerificationStarted(VerificationStartedEventArgs args)
        {
            VerificationStarted?.Invoke(this, args);
        }

        private void OnVerificationProgress(VerificationProgressEventArgs args)
        {
            VerificationProgress?.Invoke(this, args);
        }

        private void OnVerificationCompleted(VerificationCompletedEventArgs args)
        {
            VerificationCompleted?.Invoke(this, args);
        }

        private void OnVerificationFailed(VerificationFailedEventArgs args)
        {
            VerificationFailed?.Invoke(this, args);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _verificationSessions.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Transfer verification configuration
    /// </summary>
    public class TransferVerificationConfiguration
    {
        public HashAlgorithmType[] DefaultHashAlgorithms { get; set; } = { HashAlgorithmType.SHA256 };
        public bool EnableDigitalSignatures { get; set; } = true;
        public int MaxConcurrentVerifications { get; set; } = 5;
    }

    /// <summary>
    /// Verification request
    /// </summary>
    public class VerificationRequest
    {
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string FilePath { get; set; }
        public int TotalChunks { get; set; }
        public HashAlgorithmType[] HashAlgorithms { get; set; }
        public Dictionary<HashAlgorithmType, string> ExpectedHashes { get; set; }
        public bool GenerateSignature { get; set; } = false;
    }

    /// <summary>
    /// Verification session
    /// </summary>
    internal class VerificationSession
    {
        public string SessionId { get; set; }
        public VerificationRequest Request { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public VerificationStatus Status { get; set; }
        public HashAlgorithmType[] HashAlgorithms { get; set; }
        public Dictionary<HashAlgorithmType, string> ComputedHashes { get; set; }
        public List<ChunkHash> ChunkHashes { get; set; }
    }

    /// <summary>
    /// Chunk hash information
    /// </summary>
    public class ChunkHash
    {
        public int ChunkIndex { get; set; }
        public string Hash { get; set; }
        public int Size { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Verification result
    /// </summary>
    public class VerificationResult
    {
        public string SessionId { get; set; }
        public bool Success { get; set; }
        public Dictionary<HashAlgorithmType, string> ComputedHashes { get; set; }
        public int ChunkCount { get; set; }
        public long TotalSize { get; set; }
        public TimeSpan Duration { get; set; }
        public string ErrorMessage { get; set; }
        public string DigitalSignature { get; set; }
    }

    /// <summary>
    /// Chunk verification result
    /// </summary>
    public class ChunkVerificationResult
    {
        public bool Success { get; set; }
        public int ChunkIndex { get; set; }
        public string ComputedHash { get; set; }
        public string ExpectedHash { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Chunk integrity result
    /// </summary>
    internal class ChunkIntegrityResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Hash algorithm type enumeration
    /// </summary>
    public enum HashAlgorithmType
    {
        MD5,
        SHA1,
        SHA256,
        SHA384,
        SHA512
    }

    /// <summary>
    /// Verification status enumeration
    /// </summary>
    public enum VerificationStatus
    {
        InProgress,
        Completed,
        Failed
    }

    /// <summary>
    /// Verification started event arguments
    /// </summary>
    public class VerificationStartedEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public HashAlgorithmType[] HashAlgorithms { get; set; }
    }

    /// <summary>
    /// Verification progress event arguments
    /// </summary>
    public class VerificationProgressEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public int ChunkIndex { get; set; }
        public string ChunkHash { get; set; }
        public bool IsValid { get; set; }
        public double Progress { get; set; }
    }

    /// <summary>
    /// Verification completed event arguments
    /// </summary>
    public class VerificationCompletedEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public VerificationResult Result { get; set; }
    }

    /// <summary>
    /// Verification failed event arguments
    /// </summary>
    public class VerificationFailedEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
    }
}
