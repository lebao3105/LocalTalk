using Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Shared.Http
{
    /// <summary>
    /// Manages HTTP sessions for file transfers
    /// </summary>
    public class SessionManager
    {
        private static SessionManager _instance;
        private readonly ConcurrentDictionary<string, UploadSession> _uploadSessions;
        private readonly ConcurrentDictionary<string, DownloadSession> _downloadSessions;
        private readonly Timer _cleanupTimer;

        public static SessionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new SessionManager();
                }
                return _instance;
            }
        }

        private SessionManager()
        {
            _uploadSessions = new ConcurrentDictionary<string, UploadSession>();
            _downloadSessions = new ConcurrentDictionary<string, DownloadSession>();
            
            // Start cleanup timer to remove expired sessions
            _cleanupTimer = new Timer(CleanupExpiredSessions, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// Creates a new upload session
        /// </summary>
        public UploadSession CreateUploadSession(string sessionId, UploadRequest uploadRequest, Dictionary<string, string> fileTokens)
        {
            var session = new UploadSession
            {
                SessionId = sessionId,
                UploadRequest = uploadRequest,
                FileTokens = fileTokens,
                CreatedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.AddHours(1), // Sessions expire after 1 hour
                Status = SessionStatus.Active
            };

            _uploadSessions[sessionId] = session;
            
            System.Diagnostics.Debug.WriteLine($"Created upload session {sessionId} with {fileTokens.Count} files");
            return session;
        }

        /// <summary>
        /// Gets an upload session by ID
        /// </summary>
        public UploadSession GetUploadSession(string sessionId)
        {
            _uploadSessions.TryGetValue(sessionId, out var session);
            return session;
        }

        /// <summary>
        /// Creates a new download session
        /// </summary>
        public DownloadSession CreateDownloadSession(string sessionId, Dictionary<string, UploadObject> files)
        {
            var session = new DownloadSession
            {
                SessionId = sessionId,
                Files = files,
                CreatedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.AddHours(1),
                Status = SessionStatus.Active
            };

            _downloadSessions[sessionId] = session;
            
            System.Diagnostics.Debug.WriteLine($"Created download session {sessionId} with {files.Count} files");
            return session;
        }

        /// <summary>
        /// Gets a download session by ID
        /// </summary>
        public DownloadSession GetDownloadSession(string sessionId)
        {
            _downloadSessions.TryGetValue(sessionId, out var session);
            return session;
        }

        /// <summary>
        /// Cancels a session
        /// </summary>
        public void CancelSession(string sessionId)
        {
            if (_uploadSessions.TryGetValue(sessionId, out var uploadSession))
            {
                uploadSession.Status = SessionStatus.Cancelled;
                System.Diagnostics.Debug.WriteLine($"Cancelled upload session {sessionId}");
            }

            if (_downloadSessions.TryGetValue(sessionId, out var downloadSession))
            {
                downloadSession.Status = SessionStatus.Cancelled;
                System.Diagnostics.Debug.WriteLine($"Cancelled download session {sessionId}");
            }
        }

        /// <summary>
        /// Removes expired sessions
        /// </summary>
        private void CleanupExpiredSessions(object state)
        {
            var now = DateTime.Now;
            var expiredUploadSessions = new List<string>();
            var expiredDownloadSessions = new List<string>();

            // Find expired upload sessions
            foreach (var kvp in _uploadSessions)
            {
                if (kvp.Value.ExpiresAt < now || kvp.Value.Status == SessionStatus.Completed || kvp.Value.Status == SessionStatus.Cancelled)
                {
                    expiredUploadSessions.Add(kvp.Key);
                }
            }

            // Find expired download sessions
            foreach (var kvp in _downloadSessions)
            {
                if (kvp.Value.ExpiresAt < now || kvp.Value.Status == SessionStatus.Completed || kvp.Value.Status == SessionStatus.Cancelled)
                {
                    expiredDownloadSessions.Add(kvp.Key);
                }
            }

            // Remove expired sessions
            foreach (var sessionId in expiredUploadSessions)
            {
                _uploadSessions.TryRemove(sessionId, out _);
            }

            foreach (var sessionId in expiredDownloadSessions)
            {
                _downloadSessions.TryRemove(sessionId, out _);
            }

            if (expiredUploadSessions.Count > 0 || expiredDownloadSessions.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Cleaned up {expiredUploadSessions.Count} upload sessions and {expiredDownloadSessions.Count} download sessions");
            }
        }

        /// <summary>
        /// Gets session statistics
        /// </summary>
        public SessionStatistics GetStatistics()
        {
            return new SessionStatistics
            {
                ActiveUploadSessions = _uploadSessions.Count,
                ActiveDownloadSessions = _downloadSessions.Count,
                TotalSessions = _uploadSessions.Count + _downloadSessions.Count
            };
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }
    }

    /// <summary>
    /// Upload session information
    /// </summary>
    public class UploadSession
    {
        public string SessionId { get; set; }
        public UploadRequest UploadRequest { get; set; }
        public Dictionary<string, string> FileTokens { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public SessionStatus Status { get; set; }
        public HashSet<string> ReceivedFiles { get; set; } = new HashSet<string>();
        public string RemoteAddress { get; set; }

        /// <summary>
        /// Marks a file as received
        /// </summary>
        public void MarkFileReceived(string fileId)
        {
            ReceivedFiles.Add(fileId);
            
            // Check if all files have been received
            if (ReceivedFiles.Count == FileTokens.Count)
            {
                Status = SessionStatus.Completed;
                System.Diagnostics.Debug.WriteLine($"Upload session {SessionId} completed - all files received");
            }
        }

        /// <summary>
        /// Gets the progress of the upload session
        /// </summary>
        public double GetProgress()
        {
            if (FileTokens.Count == 0) return 1.0;
            return (double)ReceivedFiles.Count / FileTokens.Count;
        }
    }

    /// <summary>
    /// Download session information
    /// </summary>
    public class DownloadSession
    {
        public string SessionId { get; set; }
        public Dictionary<string, UploadObject> Files { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public SessionStatus Status { get; set; }
        public HashSet<string> DownloadedFiles { get; set; } = new HashSet<string>();
        public string RemoteAddress { get; set; }

        /// <summary>
        /// Marks a file as downloaded
        /// </summary>
        public void MarkFileDownloaded(string fileId)
        {
            DownloadedFiles.Add(fileId);
            
            // Check if all files have been downloaded
            if (DownloadedFiles.Count == Files.Count)
            {
                Status = SessionStatus.Completed;
                System.Diagnostics.Debug.WriteLine($"Download session {SessionId} completed - all files downloaded");
            }
        }

        /// <summary>
        /// Gets the progress of the download session
        /// </summary>
        public double GetProgress()
        {
            if (Files.Count == 0) return 1.0;
            return (double)DownloadedFiles.Count / Files.Count;
        }
    }

    /// <summary>
    /// Session status enumeration
    /// </summary>
    public enum SessionStatus
    {
        Active,
        Completed,
        Cancelled,
        Expired,
        Error
    }

    /// <summary>
    /// Session statistics
    /// </summary>
    public class SessionStatistics
    {
        public int ActiveUploadSessions { get; set; }
        public int ActiveDownloadSessions { get; set; }
        public int TotalSessions { get; set; }
    }
}
