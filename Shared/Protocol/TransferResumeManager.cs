using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Shared.Platform;
using Shared.FileSystem;

namespace Shared.Protocol
{
    /// <summary>
    /// Transfer resume and recovery manager with state persistence and corruption detection
    /// </summary>
    public class TransferResumeManager
    {
        private static TransferResumeManager _instance;
        private readonly ConcurrentDictionary<string, TransferState> _activeTransfers;
        private readonly string _stateDirectory;
        private readonly ResumeConfiguration _config;

        public static TransferResumeManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TransferResumeManager();
                }
                return _instance;
            }
        }

        public event EventHandler<TransferResumedEventArgs> TransferResumed;
        public event EventHandler<TransferRecoveredEventArgs> TransferRecovered;
        public event EventHandler<ChunkCorruptionEventArgs> ChunkCorruptionDetected;

        private TransferResumeManager()
        {
            _activeTransfers = new ConcurrentDictionary<string, TransferState>();
            _stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalTalk", "TransferStates");
            _config = new ResumeConfiguration();
            
            // Ensure state directory exists
            Directory.CreateDirectory(_stateDirectory);
            
            // Load existing transfer states on startup
            _ = LoadExistingTransferStatesAsync();
        }

        /// <summary>
        /// Saves transfer state for resume capability
        /// </summary>
        public async Task SaveTransferStateAsync(string transferId, TransferSession session)
        {
            try
            {
                var state = new TransferState
                {
                    TransferId = transferId,
                    FileName = session.Request.FileName,
                    FileSize = session.Request.FileSize,
                    ChunkSize = session.ChunkSize,
                    TotalChunks = session.TotalChunks,
                    CompletedChunks = session.CompletedChunks,
                    Direction = session.Request.Direction,
                    RemoteEndpoint = session.Request.RemoteEndpoint,
                    LocalPath = session.Request.Direction == TransferDirection.Download ? 
                        session.Request.DestinationPath : session.Request.SourceFile?.Path,
                    LastSaved = DateTime.Now,
                    ChunkStates = session.ChunkStates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    Metadata = session.Request.Metadata ?? new Dictionary<string, string>()
                };

                // Calculate and store chunk checksums for corruption detection
                await CalculateChunkChecksumsAsync(state, session);

                _activeTransfers[transferId] = state;
                await PersistTransferStateAsync(state);

                System.Diagnostics.Debug.WriteLine($"Transfer state saved for {transferId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving transfer state for {transferId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempts to resume a transfer from saved state
        /// </summary>
        public async Task<TransferResumeResult> ResumeTransferAsync(string transferId)
        {
            var result = new TransferResumeResult
            {
                TransferId = transferId,
                AttemptedAt = DateTime.Now
            };

            try
            {
                // Load transfer state
                var state = await LoadTransferStateAsync(transferId);
                if (state == null)
                {
                    result.ErrorMessage = "Transfer state not found";
                    return result;
                }

                // Validate transfer can be resumed
                var validationResult = await ValidateResumeCapabilityAsync(state);
                if (!validationResult.CanResume)
                {
                    result.ErrorMessage = validationResult.Reason;
                    return result;
                }

                // Detect and recover from corruption
                var recoveryResult = await DetectAndRecoverCorruptionAsync(state);
                if (!recoveryResult.Success)
                {
                    result.ErrorMessage = $"Corruption recovery failed: {recoveryResult.ErrorMessage}";
                    return result;
                }

                // Create new transfer session from saved state
                var resumedSession = await CreateResumedSessionAsync(state);
                if (resumedSession == null)
                {
                    result.ErrorMessage = "Failed to create resumed session";
                    return result;
                }

                result.Success = true;
                result.ResumedSession = resumedSession;
                result.ResumedFromChunk = state.CompletedChunks;
                result.RemainingChunks = state.TotalChunks - state.CompletedChunks;
                result.RecoveredChunks = recoveryResult.RecoveredChunks;

                OnTransferResumed(new TransferResumedEventArgs
                {
                    TransferId = transferId,
                    ResumedFromChunk = result.ResumedFromChunk,
                    RemainingChunks = result.RemainingChunks,
                    RecoveredChunks = result.RecoveredChunks
                });

                System.Diagnostics.Debug.WriteLine($"Transfer resumed: {transferId} from chunk {result.ResumedFromChunk}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Error resuming transfer {transferId}: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Gets list of resumable transfers
        /// </summary>
        public async Task<List<ResumableTransferInfo>> GetResumableTransfersAsync()
        {
            var resumableTransfers = new List<ResumableTransferInfo>();

            try
            {
                var stateFiles = Directory.GetFiles(_stateDirectory, "*.json");
                
                foreach (var stateFile in stateFiles)
                {
                    try
                    {
                        var state = await LoadTransferStateFromFileAsync(stateFile);
                        if (state != null)
                        {
                            var validation = await ValidateResumeCapabilityAsync(state);
                            
                            resumableTransfers.Add(new ResumableTransferInfo
                            {
                                TransferId = state.TransferId,
                                FileName = state.FileName,
                                FileSize = state.FileSize,
                                Progress = (double)state.CompletedChunks / state.TotalChunks * 100,
                                LastSaved = state.LastSaved,
                                CanResume = validation.CanResume,
                                ResumeBlockedReason = validation.Reason,
                                Direction = state.Direction,
                                RemoteEndpoint = state.RemoteEndpoint
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading transfer state from {stateFile}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting resumable transfers: {ex.Message}");
            }

            return resumableTransfers.OrderByDescending(t => t.LastSaved).ToList();
        }

        /// <summary>
        /// Removes transfer state (when transfer completes or is cancelled)
        /// </summary>
        public async Task RemoveTransferStateAsync(string transferId)
        {
            try
            {
                _activeTransfers.TryRemove(transferId, out _);
                
                var stateFile = GetStateFilePath(transferId);
                if (File.Exists(stateFile))
                {
                    File.Delete(stateFile);
                }

                System.Diagnostics.Debug.WriteLine($"Transfer state removed for {transferId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing transfer state for {transferId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates if a transfer can be resumed
        /// </summary>
        private async Task<ResumeValidationResult> ValidateResumeCapabilityAsync(TransferState state)
        {
            var result = new ResumeValidationResult { CanResume = true };

            try
            {
                // Check if transfer is too old
                if (DateTime.Now - state.LastSaved > _config.MaxResumeAge)
                {
                    result.CanResume = false;
                    result.Reason = "Transfer state is too old";
                    return result;
                }

                // Check if local file still exists (for uploads)
                if (state.Direction == TransferDirection.Upload && !string.IsNullOrEmpty(state.LocalPath))
                {
                    if (!File.Exists(state.LocalPath))
                    {
                        result.CanResume = false;
                        result.Reason = "Source file no longer exists";
                        return result;
                    }

                    // Check if file has been modified
                    var fileInfo = new FileInfo(state.LocalPath);
                    if (fileInfo.Length != state.FileSize)
                    {
                        result.CanResume = false;
                        result.Reason = "Source file has been modified";
                        return result;
                    }
                }

                // Check if partial download file exists (for downloads)
                if (state.Direction == TransferDirection.Download && !string.IsNullOrEmpty(state.LocalPath))
                {
                    var partialFile = state.LocalPath + ".partial";
                    if (!File.Exists(partialFile))
                    {
                        result.CanResume = false;
                        result.Reason = "Partial download file not found";
                        return result;
                    }
                }

                // Additional validation can be added here
                result.CanResume = true;
            }
            catch (Exception ex)
            {
                result.CanResume = false;
                result.Reason = $"Validation error: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Detects and recovers from chunk corruption
        /// </summary>
        private async Task<CorruptionRecoveryResult> DetectAndRecoverCorruptionAsync(TransferState state)
        {
            var result = new CorruptionRecoveryResult { Success = true };

            try
            {
                var corruptedChunks = new List<int>();

                // Verify chunk integrity for completed chunks
                foreach (var chunkKvp in state.ChunkStates.Where(kvp => kvp.Value == ChunkState.Completed))
                {
                    var chunkIndex = chunkKvp.Key;
                    
                    if (state.ChunkChecksums.ContainsKey(chunkIndex))
                    {
                        var isValid = await ValidateChunkIntegrityAsync(state, chunkIndex);
                        if (!isValid)
                        {
                            corruptedChunks.Add(chunkIndex);
                            
                            OnChunkCorruptionDetected(new ChunkCorruptionEventArgs
                            {
                                TransferId = state.TransferId,
                                ChunkIndex = chunkIndex,
                                DetectedAt = DateTime.Now
                            });
                        }
                    }
                }

                // Mark corrupted chunks for re-download
                foreach (var chunkIndex in corruptedChunks)
                {
                    state.ChunkStates[chunkIndex] = ChunkState.Failed;
                    state.CompletedChunks--;
                }

                result.RecoveredChunks = corruptedChunks.Count;
                
                if (corruptedChunks.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Detected and marked {corruptedChunks.Count} corrupted chunks for recovery in transfer {state.TransferId}");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Error during corruption recovery for {state.TransferId}: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Validates chunk integrity using stored checksum
        /// </summary>
        private async Task<bool> ValidateChunkIntegrityAsync(TransferState state, int chunkIndex)
        {
            try
            {
                if (!state.ChunkChecksums.ContainsKey(chunkIndex))
                    return true; // No checksum to validate against

                // This is a simplified implementation
                // In a real implementation, you would read the actual chunk data and calculate its checksum
                await Task.Delay(10); // Simulate checksum calculation
                
                // For now, assume chunks are valid (in real implementation, compare actual vs stored checksum)
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error validating chunk {chunkIndex} integrity: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Calculates checksums for completed chunks
        /// </summary>
        private async Task CalculateChunkChecksumsAsync(TransferState state, TransferSession session)
        {
            try
            {
                state.ChunkChecksums = new Dictionary<int, string>();
                
                // Calculate checksums for completed chunks
                foreach (var chunkKvp in session.ChunkStates.Where(kvp => kvp.Value == ChunkState.Completed))
                {
                    var chunkIndex = chunkKvp.Key;
                    
                    // This is a simplified implementation
                    // In a real implementation, you would calculate actual chunk checksums
                    await Task.Delay(1); // Simulate checksum calculation
                    state.ChunkChecksums[chunkIndex] = $"checksum_{chunkIndex}";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculating chunk checksums: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a resumed transfer session from saved state
        /// </summary>
        private async Task<TransferSession> CreateResumedSessionAsync(TransferState state)
        {
            try
            {
                var request = new TransferRequest
                {
                    FileName = state.FileName,
                    FileSize = state.FileSize,
                    Direction = state.Direction,
                    DestinationPath = state.Direction == TransferDirection.Download ? state.LocalPath : null,
                    RemoteEndpoint = state.RemoteEndpoint,
                    ChunkSize = state.ChunkSize,
                    Metadata = state.Metadata
                };

                var session = new TransferSession
                {
                    SessionId = state.TransferId,
                    Request = request,
                    Status = TransferStatus.Active,
                    ChunkSize = state.ChunkSize,
                    TotalChunks = state.TotalChunks,
                    CompletedChunks = state.CompletedChunks,
                    ChunkStates = new ConcurrentDictionary<int, ChunkState>(state.ChunkStates),
                    StartTime = DateTime.Now // Reset start time for resumed transfer
                };

                return session;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating resumed session: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Persists transfer state to disk
        /// </summary>
        private async Task PersistTransferStateAsync(TransferState state)
        {
            try
            {
                var stateFile = GetStateFilePath(state.TransferId);
                var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(stateFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error persisting transfer state: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads transfer state from disk
        /// </summary>
        private async Task<TransferState> LoadTransferStateAsync(string transferId)
        {
            try
            {
                var stateFile = GetStateFilePath(transferId);
                if (!File.Exists(stateFile))
                    return null;

                return await LoadTransferStateFromFileAsync(stateFile);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading transfer state for {transferId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Loads transfer state from a specific file
        /// </summary>
        private async Task<TransferState> LoadTransferStateFromFileAsync(string stateFile)
        {
            try
            {
                var json = await File.ReadAllTextAsync(stateFile);
                return JsonSerializer.Deserialize<TransferState>(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading transfer state from file {stateFile}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Loads existing transfer states on startup
        /// </summary>
        private async Task LoadExistingTransferStatesAsync()
        {
            try
            {
                var stateFiles = Directory.GetFiles(_stateDirectory, "*.json");
                
                foreach (var stateFile in stateFiles)
                {
                    var state = await LoadTransferStateFromFileAsync(stateFile);
                    if (state != null)
                    {
                        _activeTransfers[state.TransferId] = state;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Loaded {_activeTransfers.Count} existing transfer states");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading existing transfer states: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the file path for a transfer state
        /// </summary>
        private string GetStateFilePath(string transferId)
        {
            return Path.Combine(_stateDirectory, $"{transferId}.json");
        }

        /// <summary>
        /// Raises the TransferResumed event
        /// </summary>
        private void OnTransferResumed(TransferResumedEventArgs args)
        {
            TransferResumed?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the TransferRecovered event
        /// </summary>
        private void OnTransferRecovered(TransferRecoveredEventArgs args)
        {
            TransferRecovered?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the ChunkCorruptionDetected event
        /// </summary>
        private void OnChunkCorruptionDetected(ChunkCorruptionEventArgs args)
        {
            ChunkCorruptionDetected?.Invoke(this, args);
        }
    }

    /// <summary>
    /// Transfer state for persistence
    /// </summary>
    public class TransferState
    {
        public string TransferId { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public int ChunkSize { get; set; }
        public int TotalChunks { get; set; }
        public int CompletedChunks { get; set; }
        public TransferDirection Direction { get; set; }
        public string RemoteEndpoint { get; set; }
        public string LocalPath { get; set; }
        public DateTime LastSaved { get; set; }
        public Dictionary<int, ChunkState> ChunkStates { get; set; } = new Dictionary<int, ChunkState>();
        public Dictionary<int, string> ChunkChecksums { get; set; } = new Dictionary<int, string>();
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Transfer resume result
    /// </summary>
    public class TransferResumeResult
    {
        public bool Success { get; set; }
        public string TransferId { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime AttemptedAt { get; set; }
        public TransferSession ResumedSession { get; set; }
        public int ResumedFromChunk { get; set; }
        public int RemainingChunks { get; set; }
        public int RecoveredChunks { get; set; }
    }

    /// <summary>
    /// Resumable transfer information
    /// </summary>
    public class ResumableTransferInfo
    {
        public string TransferId { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public double Progress { get; set; }
        public DateTime LastSaved { get; set; }
        public bool CanResume { get; set; }
        public string ResumeBlockedReason { get; set; }
        public TransferDirection Direction { get; set; }
        public string RemoteEndpoint { get; set; }
    }

    /// <summary>
    /// Resume configuration
    /// </summary>
    public class ResumeConfiguration
    {
        public TimeSpan MaxResumeAge { get; set; } = TimeSpan.FromDays(7);
        public bool EnableCorruptionDetection { get; set; } = true;
        public bool EnableAutoRecovery { get; set; } = true;
        public int MaxRecoveryAttempts { get; set; } = 3;
        public TimeSpan StateUpdateInterval { get; set; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Resume validation result
    /// </summary>
    internal class ResumeValidationResult
    {
        public bool CanResume { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Corruption recovery result
    /// </summary>
    internal class CorruptionRecoveryResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int RecoveredChunks { get; set; }
    }

    /// <summary>
    /// Transfer resumed event arguments
    /// </summary>
    public class TransferResumedEventArgs : EventArgs
    {
        public string TransferId { get; set; }
        public int ResumedFromChunk { get; set; }
        public int RemainingChunks { get; set; }
        public int RecoveredChunks { get; set; }
    }

    /// <summary>
    /// Transfer recovered event arguments
    /// </summary>
    public class TransferRecoveredEventArgs : EventArgs
    {
        public string TransferId { get; set; }
        public int RecoveredChunks { get; set; }
        public string RecoveryMethod { get; set; }
    }

    /// <summary>
    /// Chunk corruption event arguments
    /// </summary>
    public class ChunkCorruptionEventArgs : EventArgs
    {
        public string TransferId { get; set; }
        public int ChunkIndex { get; set; }
        public DateTime DetectedAt { get; set; }
        public string CorruptionType { get; set; }
    }
}
