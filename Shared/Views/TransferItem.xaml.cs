using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Shared.Protocol;
using Shared.FileSystem;
using Shared.Platform;

#if WINDOWS_PHONE
using System.Windows.Controls;
#else
using Windows.UI.Xaml.Controls;
#endif

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Shared.Views
{
    public sealed partial class TransferItem : UserControl, INotifyPropertyChanged
    {
        private TransferSession _transferSession;
        private SecureFileInfo _fileInfo;
        private DateTime _transferStartTime;
        private long _lastTransferredBytes;
        private DateTime _lastSpeedUpdate;

        public event PropertyChangedEventHandler PropertyChanged;

        public TransferItem()
        {
            this.InitializeComponent();
            this.DataContext = this;
            _lastSpeedUpdate = DateTime.Now;
        }

        public SecureFileInfo FileInfo
        {
            get => _fileInfo;
            set
            {
                _fileInfo = value;
                UpdateFileDisplay();
                OnPropertyChanged();
            }
        }

        public TransferSession TransferSession
        {
            get => _transferSession;
            set
            {
                if (_transferSession != null)
                {
                    // Unsubscribe from old session
                    UnsubscribeFromSession(_transferSession);
                }

                _transferSession = value;

                if (_transferSession != null)
                {
                    // Subscribe to new session
                    SubscribeToSession(_transferSession);
                    UpdateTransferDisplay();
                }

                OnPropertyChanged();
            }
        }

        private void UpdateFileDisplay()
        {
            if (_fileInfo?.File == null) return;

            PlatformFactory.Current.RunOnUIThread(() =>
            {
                FileName.Text = _fileInfo.File.Name;
                FileSize.Text = FormatFileSize(_fileInfo.File.Size);

                // Set appropriate icon based on file type
                var extension = Path.GetExtension(_fileInfo.File.Name).ToLowerInvariant();
                FileIcon.Glyph = GetFileIcon(extension);
            });
        }

        private void UpdateTransferDisplay()
        {
            if (_transferSession == null) return;

            PlatformFactory.Current.RunOnUIThread(() =>
            {
                StatusText.Text = GetStatusText(_transferSession.Status);

                switch (_transferSession.Status)
                {
                    case TransferStatus.Initializing:
                    case TransferStatus.Active:
                        TransferProgress.Visibility = Visibility.Visible;
                        TransferDetails.Visibility = Visibility.Visible;
                        CancelButton.Visibility = Visibility.Visible;
                        _transferStartTime = _transferSession.StartTime;
                        break;

                    case TransferStatus.Completed:
                        TransferProgress.Value = 100;
                        TransferProgress.Visibility = Visibility.Visible;
                        TransferDetails.Visibility = Visibility.Collapsed;
                        CancelButton.Visibility = Visibility.Collapsed;
                        StatusText.Text = "Completed";
                        break;

                    case TransferStatus.Failed:
                    case TransferStatus.Cancelled:
                        TransferProgress.Visibility = Visibility.Collapsed;
                        TransferDetails.Visibility = Visibility.Collapsed;
                        CancelButton.Visibility = Visibility.Collapsed;
                        break;
                }
            });
        }

        private void SubscribeToSession(TransferSession session)
        {
            // Subscribe to progress events
            ChunkedTransferProtocol.Instance.ProgressUpdated += OnProgressUpdated;
            ChunkedTransferProtocol.Instance.TransferCompleted += OnTransferCompleted;
        }

        private void UnsubscribeFromSession(TransferSession session)
        {
            // Unsubscribe from progress events
            if (ChunkedTransferProtocol.Instance != null)
            {
                ChunkedTransferProtocol.Instance.ProgressUpdated -= OnProgressUpdated;
                ChunkedTransferProtocol.Instance.TransferCompleted -= OnTransferCompleted;
            }
        }

        private void OnProgressUpdated(object sender, TransferProgressEventArgs e)
        {
            if (e.SessionId != _transferSession?.SessionId) return;

            PlatformFactory.Current.RunOnUIThread(() =>
            {
                var progressPercentage = (double)e.TransferredBytes / e.TotalBytes * 100;
                TransferProgress.Value = progressPercentage;

                // Update transfer details
                TransferredAmount.Text = $"{FormatFileSize(e.TransferredBytes)} / {FormatFileSize(e.TotalBytes)}";

                // Calculate and display transfer speed
                UpdateTransferSpeed(e.TransferredBytes);

                // Calculate and display time remaining
                UpdateTimeRemaining(e.TransferredBytes, e.TotalBytes);
            });
        }

        private void OnTransferCompleted(object sender, TransferCompletedEventArgs e)
        {
            if (e.SessionId != _transferSession?.SessionId) return;

            PlatformFactory.Current.RunOnUIThread(() =>
            {
                if (e.Success)
                {
                    StatusText.Text = "Completed";
                    TransferProgress.Value = 100;
                }
                else
                {
                    StatusText.Text = "Failed";
                    TransferProgress.Visibility = Visibility.Collapsed;
                }

                TransferDetails.Visibility = Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Collapsed;
            });
        }

        private void UpdateTransferSpeed(long transferredBytes)
        {
            var now = DateTime.Now;
            var timeDiff = (now - _lastSpeedUpdate).TotalSeconds;

            if (timeDiff >= 1.0) // Update speed every second
            {
                var bytesDiff = transferredBytes - _lastTransferredBytes;
                var speed = bytesDiff / timeDiff;

                TransferSpeed.Text = $"{FormatFileSize((long)speed)}/s";

                _lastTransferredBytes = transferredBytes;
                _lastSpeedUpdate = now;
            }
        }

        private void UpdateTimeRemaining(long transferredBytes, long totalBytes)
        {
            if (transferredBytes == 0) return;

            var elapsed = DateTime.Now - _transferStartTime;
            var rate = transferredBytes / elapsed.TotalSeconds;
            var remaining = (totalBytes - transferredBytes) / rate;

            if (remaining > 0 && remaining < double.MaxValue)
            {
                var timeSpan = TimeSpan.FromSeconds(remaining);
                if (timeSpan.TotalHours >= 1)
                {
                    TimeRemaining.Text = $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
                }
                else
                {
                    TimeRemaining.Text = $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
                }
            }
            else
            {
                TimeRemaining.Text = "--:--";
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            if (_transferSession != null)
            {
                ChunkedTransferProtocol.Instance.CancelTransferAsync(_transferSession.SessionId);
            }
        }

        private string GetStatusText(TransferStatus status)
        {
            return status switch
            {
                TransferStatus.Pending => "Pending",
                TransferStatus.Initializing => "Initializing",
                TransferStatus.Active => "Transferring",
                TransferStatus.Completed => "Completed",
                TransferStatus.Failed => "Failed",
                TransferStatus.Cancelled => "Cancelled",
                _ => "Unknown"
            };
        }

        private string GetFileIcon(string extension)
        {
            return extension switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" => "\uE91B", // Image
                ".mp4" or ".avi" or ".mov" or ".wmv" => "\uE8B2", // Video
                ".mp3" or ".wav" or ".wma" or ".flac" => "\uE8D6", // Audio
                ".pdf" => "\uE8A5", // PDF
                ".doc" or ".docx" => "\uE8A5", // Document
                ".zip" or ".rar" or ".7z" => "\uE8B7", // Archive
                _ => "\uE8A5" // Generic file
            };
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
