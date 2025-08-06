using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shared.Platform;

namespace Shared.Protocol
{
    /// <summary>
    /// Real-time progress tracker for UI updates with thread synchronization
    /// </summary>
    public class RealTimeProgressTracker : IDisposable
    {
        private static RealTimeProgressTracker _instance;
        private readonly ConcurrentDictionary<string, ProgressTrackingSession> _trackingSessions;
        private readonly Timer _updateTimer;
        private readonly object _lockObject = new object();
        private bool _disposed;

        public static RealTimeProgressTracker Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new RealTimeProgressTracker();
                }
                return _instance;
            }
        }

        public event EventHandler<ProgressUpdateEventArgs> ProgressUpdated;
        public event EventHandler<TransferStatusChangedEventArgs> StatusChanged;

        private RealTimeProgressTracker()
        {
            _trackingSessions = new ConcurrentDictionary<string, ProgressTrackingSession>();
            
            // Update UI every 100ms for smooth progress bars
            _updateTimer = new Timer(UpdateProgressCallback, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
        }

        /// <summary>
        /// Starts tracking progress for a transfer session
        /// </summary>
        public void StartTracking(string sessionId, string fileName, long totalBytes)
        {
            var session = new ProgressTrackingSession
            {
                SessionId = sessionId,
                FileName = fileName,
                TotalBytes = totalBytes,
                StartTime = DateTime.Now,
                LastUpdateTime = DateTime.Now,
                Status = TransferStatus.Active
            };

            _trackingSessions[sessionId] = session;
            
            OnStatusChanged(new TransferStatusChangedEventArgs
            {
                SessionId = sessionId,
                Status = TransferStatus.Active,
                FileName = fileName
            });
        }

        /// <summary>
        /// Updates progress for a transfer session
        /// </summary>
        public void UpdateProgress(string sessionId, TransferProgressEventArgs progressArgs)
        {
            if (_trackingSessions.TryGetValue(sessionId, out var session))
            {
                lock (session.UpdateLock)
                {
                    session.BytesTransferred = progressArgs.BytesTransferred;
                    session.Progress = progressArgs.Progress;
                    session.TransferSpeed = progressArgs.TransferSpeed;
                    session.EstimatedTimeRemaining = progressArgs.EstimatedTimeRemaining;
                    session.ElapsedTime = progressArgs.ElapsedTime;
                    session.LastUpdateTime = DateTime.Now;
                    session.FailedChunks = progressArgs.FailedChunks;
                    session.CompletedChunks = progressArgs.CompletedChunks;
                    session.TotalChunks = progressArgs.TotalChunks;
                    
                    // Mark as updated for next UI refresh
                    session.HasUpdates = true;
                }
            }
        }

        /// <summary>
        /// Completes tracking for a transfer session
        /// </summary>
        public void CompleteTracking(string sessionId, bool success, string errorMessage = null)
        {
            if (_trackingSessions.TryGetValue(sessionId, out var session))
            {
                lock (session.UpdateLock)
                {
                    session.Status = success ? TransferStatus.Completed : TransferStatus.Failed;
                    session.ErrorMessage = errorMessage;
                    session.EndTime = DateTime.Now;
                    session.HasUpdates = true;
                }

                OnStatusChanged(new TransferStatusChangedEventArgs
                {
                    SessionId = sessionId,
                    Status = session.Status,
                    FileName = session.FileName,
                    ErrorMessage = errorMessage
                });
            }
        }

        /// <summary>
        /// Gets current progress for a specific session
        /// </summary>
        public ProgressSnapshot GetProgress(string sessionId)
        {
            if (_trackingSessions.TryGetValue(sessionId, out var session))
            {
                lock (session.UpdateLock)
                {
                    return new ProgressSnapshot
                    {
                        SessionId = sessionId,
                        FileName = session.FileName,
                        Progress = session.Progress,
                        BytesTransferred = session.BytesTransferred,
                        TotalBytes = session.TotalBytes,
                        TransferSpeed = session.TransferSpeed,
                        EstimatedTimeRemaining = session.EstimatedTimeRemaining,
                        ElapsedTime = session.ElapsedTime,
                        Status = session.Status,
                        ErrorMessage = session.ErrorMessage,
                        CompletedChunks = session.CompletedChunks,
                        TotalChunks = session.TotalChunks,
                        FailedChunks = session.FailedChunks
                    };
                }
            }
            return null;
        }

        /// <summary>
        /// Gets progress for all active sessions
        /// </summary>
        public List<ProgressSnapshot> GetAllProgress()
        {
            var snapshots = new List<ProgressSnapshot>();
            
            foreach (var kvp in _trackingSessions)
            {
                var snapshot = GetProgress(kvp.Key);
                if (snapshot != null)
                {
                    snapshots.Add(snapshot);
                }
            }
            
            return snapshots;
        }

        /// <summary>
        /// Removes completed or failed sessions from tracking
        /// </summary>
        public void CleanupSession(string sessionId)
        {
            _trackingSessions.TryRemove(sessionId, out _);
        }

        /// <summary>
        /// Timer callback for UI updates
        /// </summary>
        private void UpdateProgressCallback(object state)
        {
            if (_disposed) return;

            var updatedSessions = new List<ProgressSnapshot>();

            foreach (var kvp in _trackingSessions)
            {
                var session = kvp.Value;
                
                lock (session.UpdateLock)
                {
                    if (session.HasUpdates)
                    {
                        updatedSessions.Add(new ProgressSnapshot
                        {
                            SessionId = session.SessionId,
                            FileName = session.FileName,
                            Progress = session.Progress,
                            BytesTransferred = session.BytesTransferred,
                            TotalBytes = session.TotalBytes,
                            TransferSpeed = session.TransferSpeed,
                            EstimatedTimeRemaining = session.EstimatedTimeRemaining,
                            ElapsedTime = session.ElapsedTime,
                            Status = session.Status,
                            ErrorMessage = session.ErrorMessage,
                            CompletedChunks = session.CompletedChunks,
                            TotalChunks = session.TotalChunks,
                            FailedChunks = session.FailedChunks
                        });
                        
                        session.HasUpdates = false;
                    }
                }
            }

            // Fire progress updates on UI thread if available
            if (updatedSessions.Count > 0)
            {
                var platform = PlatformFactory.Current;
                if (platform != null)
                {
                    foreach (var snapshot in updatedSessions)
                    {
                        // Dispatch to UI thread
                        platform.RunOnUIThread(() =>
                        {
                            OnProgressUpdated(new ProgressUpdateEventArgs { Snapshot = snapshot });
                        });
                    }
                }
            }
        }

        private void OnProgressUpdated(ProgressUpdateEventArgs args)
        {
            ProgressUpdated?.Invoke(this, args);
        }

        private void OnStatusChanged(TransferStatusChangedEventArgs args)
        {
            StatusChanged?.Invoke(this, args);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _updateTimer?.Dispose();
                _trackingSessions.Clear();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Progress tracking session for internal use
    /// </summary>
    internal class ProgressTrackingSession
    {
        public string SessionId { get; set; }
        public string FileName { get; set; }
        public long TotalBytes { get; set; }
        public long BytesTransferred { get; set; }
        public double Progress { get; set; }
        public double TransferSpeed { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public TransferStatus Status { get; set; }
        public string ErrorMessage { get; set; }
        public int CompletedChunks { get; set; }
        public int TotalChunks { get; set; }
        public int FailedChunks { get; set; }
        public bool HasUpdates { get; set; }
        public readonly object UpdateLock = new object();
    }

    /// <summary>
    /// Progress snapshot for UI consumption
    /// </summary>
    public class ProgressSnapshot
    {
        public string SessionId { get; set; }
        public string FileName { get; set; }
        public double Progress { get; set; }
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public double TransferSpeed { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public TransferStatus Status { get; set; }
        public string ErrorMessage { get; set; }
        public int CompletedChunks { get; set; }
        public int TotalChunks { get; set; }
        public int FailedChunks { get; set; }
        
        // Formatted properties for UI binding
        public string ProgressText => $"{Progress:F1}%";
        public string SpeedText => FormatSpeed(TransferSpeed);
        public string EtaText => FormatTimeSpan(EstimatedTimeRemaining);
        public string ElapsedText => FormatTimeSpan(ElapsedTime);
        public string SizeText => $"{FormatBytes(BytesTransferred)} / {FormatBytes(TotalBytes)}";
        
        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond < 1024)
                return $"{bytesPerSecond:F0} B/s";
            if (bytesPerSecond < 1024 * 1024)
                return $"{bytesPerSecond / 1024:F1} KB/s";
            if (bytesPerSecond < 1024 * 1024 * 1024)
                return $"{bytesPerSecond / (1024 * 1024):F1} MB/s";
            return $"{bytesPerSecond / (1024 * 1024 * 1024):F1} GB/s";
        }
        
        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }
        
        private static string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalSeconds < 60)
                return $"{timeSpan.TotalSeconds:F0}s";
            if (timeSpan.TotalMinutes < 60)
                return $"{timeSpan.TotalMinutes:F0}m {timeSpan.Seconds}s";
            return $"{timeSpan.TotalHours:F0}h {timeSpan.Minutes}m";
        }
    }

    /// <summary>
    /// Progress update event arguments
    /// </summary>
    public class ProgressUpdateEventArgs : EventArgs
    {
        public ProgressSnapshot Snapshot { get; set; }
    }

    /// <summary>
    /// Transfer status changed event arguments
    /// </summary>
    public class TransferStatusChangedEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public TransferStatus Status { get; set; }
        public string FileName { get; set; }
        public string ErrorMessage { get; set; }
    }
}
