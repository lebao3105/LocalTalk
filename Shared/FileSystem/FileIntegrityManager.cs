using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Threading;
using Shared.Platform;

namespace Shared.FileSystem
{
    /// <summary>
    /// File integrity verification using checksums, hash validation, and corruption detection
    /// </summary>
    public class FileIntegrityManager
    {
        private static FileIntegrityManager _instance;
        private readonly ConcurrentDictionary<string, FileIntegrityInfo> _integrityDatabase;
        private readonly Timer _verificationTimer;
        private readonly IntegrityConfiguration _config;
        private readonly SemaphoreSlim _verificationSemaphore;

        public static FileIntegrityManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new FileIntegrityManager();
                }
                return _instance;
            }
        }

        public event EventHandler<FileCorruptionEventArgs> CorruptionDetected;
        public event EventHandler<IntegrityVerificationEventArgs> VerificationCompleted;
        public event EventHandler<IntegrityRepairEventArgs> RepairCompleted;

        private FileIntegrityManager()
        {
            _integrityDatabase = new ConcurrentDictionary<string, FileIntegrityInfo>();
            _config = new IntegrityConfiguration();
            _verificationSemaphore = new SemaphoreSlim(_config.MaxConcurrentVerifications, _config.MaxConcurrentVerifications);
            
            // Start periodic verification
            _verificationTimer = new Timer(PerformPeriodicVerification, null, 
                _config.VerificationInterval, _config.VerificationInterval);
        }

        /// <summary>
        /// Calculates and stores integrity information for a file
        /// </summary>
        public async Task<IntegrityResult> CalculateIntegrityAsync(string filePath, HashAlgorithmType algorithm = HashAlgorithmType.SHA256)
        {
            var result = new IntegrityResult
            {
                FilePath = filePath,
                Algorithm = algorithm,
                CalculatedAt = DateTime.Now
            };

            try
            {
                if (!File.Exists(filePath))
                {
                    result.Success = false;
                    result.ErrorMessage = "File not found";
                    return result;
                }

                var fileInfo = new FileInfo(filePath);
                result.FileSize = fileInfo.Length;
                result.LastModified = fileInfo.LastWriteTime;

                // Calculate hash
                using (var stream = File.OpenRead(filePath))
                {
                    result.Hash = await CalculateHashAsync(stream, algorithm);
                }

                // Calculate checksum
                result.Checksum = await CalculateChecksumAsync(filePath);

                // Store integrity information
                var integrityInfo = new FileIntegrityInfo
                {
                    FilePath = filePath,
                    Hash = result.Hash,
                    Checksum = result.Checksum,
                    Algorithm = algorithm,
                    FileSize = result.FileSize,
                    LastModified = result.LastModified,
                    CreatedAt = DateTime.Now,
                    LastVerified = DateTime.Now,
                    VerificationCount = 1
                };

                _integrityDatabase[filePath] = integrityInfo;
                result.Success = true;

                System.Diagnostics.Debug.WriteLine($"Calculated integrity for {filePath}: {result.Hash}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Error calculating integrity for {filePath}: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Verifies file integrity against stored information
        /// </summary>
        public async Task<VerificationResult> VerifyIntegrityAsync(string filePath)
        {
            var result = new VerificationResult
            {
                FilePath = filePath,
                VerifiedAt = DateTime.Now
            };

            try
            {
                if (!_integrityDatabase.TryGetValue(filePath, out var storedInfo))
                {
                    result.Status = VerificationStatus.NoBaseline;
                    result.Message = "No baseline integrity information found";
                    return result;
                }

                if (!File.Exists(filePath))
                {
                    result.Status = VerificationStatus.FileNotFound;
                    result.Message = "File not found";
                    return result;
                }

                var fileInfo = new FileInfo(filePath);
                
                // Quick checks first
                if (fileInfo.Length != storedInfo.FileSize)
                {
                    result.Status = VerificationStatus.SizeChanged;
                    result.Message = $"File size changed: {storedInfo.FileSize} -> {fileInfo.Length}";
                    result.ExpectedSize = storedInfo.FileSize;
                    result.ActualSize = fileInfo.Length;
                    
                    OnCorruptionDetected(new FileCorruptionEventArgs
                    {
                        FilePath = filePath,
                        CorruptionType = CorruptionType.SizeChange,
                        DetectedAt = DateTime.Now,
                        Details = result.Message
                    });
                    
                    return result;
                }

                if (fileInfo.LastWriteTime != storedInfo.LastModified)
                {
                    result.Status = VerificationStatus.ModificationTimeChanged;
                    result.Message = $"Modification time changed: {storedInfo.LastModified} -> {fileInfo.LastWriteTime}";
                    result.ExpectedModified = storedInfo.LastModified;
                    result.ActualModified = fileInfo.LastWriteTime;
                }

                // Calculate current hash
                using (var stream = File.OpenRead(filePath))
                {
                    result.ActualHash = await CalculateHashAsync(stream, storedInfo.Algorithm);
                }

                result.ExpectedHash = storedInfo.Hash;

                // Compare hashes
                if (result.ActualHash.Equals(result.ExpectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    result.Status = result.Status == VerificationStatus.ModificationTimeChanged ? 
                        VerificationStatus.ModificationTimeChanged : VerificationStatus.Valid;
                    result.Message = result.Status == VerificationStatus.Valid ? 
                        "File integrity verified" : "File content valid but modification time changed";
                }
                else
                {
                    result.Status = VerificationStatus.HashMismatch;
                    result.Message = "File content has been modified or corrupted";
                    
                    OnCorruptionDetected(new FileCorruptionEventArgs
                    {
                        FilePath = filePath,
                        CorruptionType = CorruptionType.ContentChange,
                        DetectedAt = DateTime.Now,
                        Details = result.Message,
                        ExpectedHash = result.ExpectedHash,
                        ActualHash = result.ActualHash
                    });
                }

                // Update verification statistics
                storedInfo.LastVerified = DateTime.Now;
                storedInfo.VerificationCount++;

                OnVerificationCompleted(new IntegrityVerificationEventArgs
                {
                    FilePath = filePath,
                    Status = result.Status,
                    VerifiedAt = DateTime.Now
                });

                System.Diagnostics.Debug.WriteLine($"Verified integrity for {filePath}: {result.Status}");
            }
            catch (Exception ex)
            {
                result.Status = VerificationStatus.Error;
                result.Message = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Error verifying integrity for {filePath}: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Verifies integrity for multiple files
        /// </summary>
        public async Task<List<VerificationResult>> VerifyMultipleFilesAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
        {
            var results = new List<VerificationResult>();
            var semaphore = new SemaphoreSlim(_config.MaxConcurrentVerifications, _config.MaxConcurrentVerifications);

            var tasks = filePaths.Select(async filePath =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await VerifyIntegrityAsync(filePath);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            results.AddRange(await Task.WhenAll(tasks));
            return results;
        }

        /// <summary>
        /// Attempts to repair a corrupted file
        /// </summary>
        public async Task<RepairResult> RepairFileAsync(string filePath, RepairOptions options = null)
        {
            options = options ?? new RepairOptions();
            var result = new RepairResult
            {
                FilePath = filePath,
                StartedAt = DateTime.Now
            };

            try
            {
                if (!_integrityDatabase.TryGetValue(filePath, out var storedInfo))
                {
                    result.Success = false;
                    result.ErrorMessage = "No baseline integrity information found for repair";
                    return result;
                }

                // Check if backup exists
                var backupPath = GetBackupPath(filePath);
                if (options.UseBackup && File.Exists(backupPath))
                {
                    // Verify backup integrity
                    var backupVerification = await VerifyBackupIntegrityAsync(backupPath, storedInfo);
                    if (backupVerification.Status == VerificationStatus.Valid)
                    {
                        // Restore from backup
                        File.Copy(backupPath, filePath, true);
                        result.Success = true;
                        result.RepairMethod = RepairMethod.BackupRestore;
                        result.Message = "File restored from backup";
                    }
                }

                // Try other repair methods if backup restore failed
                if (!result.Success && options.AttemptRecovery)
                {
                    // This is a placeholder for advanced recovery techniques
                    // In a real implementation, you might use:
                    // - File system journal recovery
                    // - Partial data recovery
                    // - Redundant data reconstruction
                    result.Success = false;
                    result.ErrorMessage = "Advanced recovery methods not implemented";
                }

                result.CompletedAt = DateTime.Now;
                result.Duration = result.CompletedAt - result.StartedAt;

                if (result.Success)
                {
                    // Recalculate integrity after repair
                    await CalculateIntegrityAsync(filePath, storedInfo.Algorithm);
                    
                    OnRepairCompleted(new IntegrityRepairEventArgs
                    {
                        FilePath = filePath,
                        RepairMethod = result.RepairMethod,
                        Success = result.Success,
                        Duration = result.Duration
                    });
                }

                System.Diagnostics.Debug.WriteLine($"Repair attempt for {filePath}: {(result.Success ? "Success" : "Failed")}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Error repairing file {filePath}: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Creates a backup of a file for integrity purposes
        /// </summary>
        public async Task<bool> CreateBackupAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return false;

                var backupPath = GetBackupPath(filePath);
                var backupDir = Path.GetDirectoryName(backupPath);
                
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }

                File.Copy(filePath, backupPath, true);
                
                System.Diagnostics.Debug.WriteLine($"Created backup for {filePath} at {backupPath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating backup for {filePath}: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Gets integrity statistics
        /// </summary>
        public IntegrityStatistics GetIntegrityStatistics()
        {
            var stats = new IntegrityStatistics
            {
                TotalFiles = _integrityDatabase.Count,
                GeneratedAt = DateTime.Now
            };

            var now = DateTime.Now;
            foreach (var info in _integrityDatabase.Values)
            {
                stats.TotalVerifications += info.VerificationCount;
                
                if (now - info.LastVerified < TimeSpan.FromDays(1))
                    stats.RecentlyVerified++;
                
                if (now - info.CreatedAt < TimeSpan.FromDays(7))
                    stats.NewFiles++;
            }

            return stats;
        }

        /// <summary>
        /// Calculates hash for a stream
        /// </summary>
        private async Task<string> CalculateHashAsync(Stream stream, HashAlgorithmType algorithm)
        {
            return await Task.Run(() =>
            {
                using (var hashAlgorithm = CreateHashAlgorithm(algorithm))
                {
                    var hash = hashAlgorithm.ComputeHash(stream);
                    return Convert.ToBase64String(hash);
                }
            });
        }

        /// <summary>
        /// Calculates simple checksum for a file
        /// </summary>
        private async Task<uint> CalculateChecksumAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                uint checksum = 0;
                using (var stream = File.OpenRead(filePath))
                {
                    int b;
                    while ((b = stream.ReadByte()) != -1)
                    {
                        checksum += (uint)b;
                    }
                }
                return checksum;
            });
        }

        /// <summary>
        /// Creates hash algorithm instance
        /// </summary>
        private HashAlgorithm CreateHashAlgorithm(HashAlgorithmType algorithm)
        {
            return algorithm switch
            {
                HashAlgorithmType.MD5 => MD5.Create(),
                HashAlgorithmType.SHA1 => SHA1.Create(),
                HashAlgorithmType.SHA256 => SHA256.Create(),
                HashAlgorithmType.SHA512 => SHA512.Create(),
                _ => SHA256.Create()
            };
        }

        /// <summary>
        /// Gets backup path for a file
        /// </summary>
        private string GetBackupPath(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            var backupDir = Path.Combine(Path.GetTempPath(), "LocalTalk", "Backups");
            return Path.Combine(backupDir, $"{fileName}.backup");
        }

        /// <summary>
        /// Verifies backup integrity
        /// </summary>
        private async Task<VerificationResult> VerifyBackupIntegrityAsync(string backupPath, FileIntegrityInfo originalInfo)
        {
            var result = new VerificationResult
            {
                FilePath = backupPath,
                VerifiedAt = DateTime.Now
            };

            try
            {
                using (var stream = File.OpenRead(backupPath))
                {
                    var hash = await CalculateHashAsync(stream, originalInfo.Algorithm);
                    
                    if (hash.Equals(originalInfo.Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Status = VerificationStatus.Valid;
                        result.Message = "Backup integrity verified";
                    }
                    else
                    {
                        result.Status = VerificationStatus.HashMismatch;
                        result.Message = "Backup is also corrupted";
                    }
                }
            }
            catch (Exception ex)
            {
                result.Status = VerificationStatus.Error;
                result.Message = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Performs periodic verification of tracked files
        /// </summary>
        private async void PerformPeriodicVerification(object state)
        {
            try
            {
                var filesToVerify = _integrityDatabase.Keys
                    .Where(path => DateTime.Now - _integrityDatabase[path].LastVerified > _config.VerificationInterval)
                    .Take(_config.MaxFilesPerVerificationCycle)
                    .ToList();

                if (filesToVerify.Any())
                {
                    await VerifyMultipleFilesAsync(filesToVerify);
                    System.Diagnostics.Debug.WriteLine($"Periodic verification completed for {filesToVerify.Count} files");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Periodic verification error: {ex}");
            }
        }

        /// <summary>
        /// Raises the CorruptionDetected event
        /// </summary>
        private void OnCorruptionDetected(FileCorruptionEventArgs args)
        {
            CorruptionDetected?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the VerificationCompleted event
        /// </summary>
        private void OnVerificationCompleted(IntegrityVerificationEventArgs args)
        {
            VerificationCompleted?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the RepairCompleted event
        /// </summary>
        private void OnRepairCompleted(IntegrityRepairEventArgs args)
        {
            RepairCompleted?.Invoke(this, args);
        }

        /// <summary>
        /// Disposes the integrity manager
        /// </summary>
        public void Dispose()
        {
            _verificationTimer?.Dispose();
            _verificationSemaphore?.Dispose();
        }
    }
}
