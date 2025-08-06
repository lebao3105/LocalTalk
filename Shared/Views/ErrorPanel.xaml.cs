using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shared.Workflows;
using Shared.Platform;

#if WINDOWS_PHONE
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
#else
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI;
#endif

namespace Shared.Views
{
    /// <summary>
    /// Represents an error panel user control that displays and manages application errors.
    /// This control provides a centralized interface for viewing, retrying, and dismissing errors
    /// that occur during file transfer operations and other application activities.
    /// </summary>
    public sealed partial class ErrorPanel : UserControl
    {
        /// <summary>
        /// The progress tracker instance used to monitor and manage errors.
        /// </summary>
        private ProgressTracker _progressTracker;

        /// <summary>
        /// Collection of error item controls currently displayed in the panel.
        /// </summary>
        private List<ErrorItemControl> _errorControls;

        /// <summary>
        /// Event raised when the user requests to close the error panel.
        /// </summary>
        public event EventHandler CloseRequested;

        /// <summary>
        /// Initializes a new instance of the ErrorPanel class.
        /// Sets up event subscriptions and initializes the error tracking system.
        /// </summary>
        public ErrorPanel()
        {
            this.InitializeComponent();
            _progressTracker = ProgressTracker.Instance;
            _errorControls = new List<ErrorItemControl>();

            // Subscribe to progress tracker events
            _progressTracker.ErrorOccurred += OnErrorOccurred;
            _progressTracker.PropertyChanged += OnProgressTrackerPropertyChanged;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>
        /// Handles the Loaded event of the ErrorPanel.
        /// Refreshes the error list to display current errors when the panel becomes visible.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The routed event arguments.</param>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshErrorList();
        }

        /// <summary>
        /// Handles the Unloaded event of the ErrorPanel.
        /// Performs cleanup by unsubscribing from events and clearing error controls to prevent memory leaks.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The routed event arguments.</param>
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Unsubscribe from events to prevent memory leaks
            if (_progressTracker != null)
            {
                _progressTracker.ErrorOccurred -= OnErrorOccurred;
                _progressTracker.PropertyChanged -= OnProgressTrackerPropertyChanged;
            }

            // Clean up error controls
            foreach (var errorControl in _errorControls)
            {
                errorControl.RetryRequested -= OnErrorRetryRequested;
                errorControl.DismissRequested -= OnErrorDismissRequested;
            }
            _errorControls.Clear();
        }

        /// <summary>
        /// Handles property change events from the progress tracker.
        /// Updates the error count display when the HasErrors property changes.
        /// </summary>
        /// <param name="sender">The progress tracker that raised the event.</param>
        /// <param name="e">Property changed event arguments containing the changed property name.</param>
        private void OnProgressTrackerPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProgressTracker.HasErrors))
            {
                PlatformFactory.Current.RunOnUIThread(() =>
                {
                    UpdateErrorCount();
                });
            }
        }

        /// <summary>
        /// Handles error occurrence events from the progress tracker.
        /// Adds new errors to the display list and updates the error count on the UI thread.
        /// </summary>
        /// <param name="sender">The progress tracker that raised the event.</param>
        /// <param name="e">Error occurred event arguments containing the error details.</param>
        private void OnErrorOccurred(object sender, ErrorOccurredEventArgs e)
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                AddErrorToList(e.Error);
                UpdateErrorCount();
            });
        }

        /// <summary>
        /// Refreshes the error list display by clearing existing controls and rebuilding from current error history.
        /// Shows a "no errors" message when no unresolved errors exist.
        /// </summary>
        private void RefreshErrorList()
        {
            // Clear existing error controls
            foreach (var errorControl in _errorControls)
            {
                errorControl.RetryRequested -= OnErrorRetryRequested;
                errorControl.DismissRequested -= OnErrorDismissRequested;
            }
            _errorControls.Clear();
            ErrorListContainer.Children.Clear();

            // Add current errors
            if (_progressTracker.ErrorHistory.Any())
            {
                ErrorListContainer.Children.Remove(NoErrorsMessage);

                foreach (var error in _progressTracker.ErrorHistory.Where(e => !e.IsResolved))
                {
                    AddErrorToList(error);
                }
            }
            else
            {
                if (!ErrorListContainer.Children.Contains(NoErrorsMessage))
                {
                    ErrorListContainer.Children.Add(NoErrorsMessage);
                }
            }

            UpdateErrorCount();
        }

        /// <summary>
        /// Adds a new error to the display list by creating an ErrorItemControl and subscribing to its events.
        /// Removes the "no errors" message if it's currently displayed.
        /// </summary>
        /// <param name="error">The error report to add to the display list.</param>
        private void AddErrorToList(ErrorReport error)
        {
            if (ErrorListContainer.Children.Contains(NoErrorsMessage))
            {
                ErrorListContainer.Children.Remove(NoErrorsMessage);
            }

            var errorControl = new ErrorItemControl(error);
            errorControl.RetryRequested += OnErrorRetryRequested;
            errorControl.DismissRequested += OnErrorDismissRequested;

            _errorControls.Add(errorControl);
            ErrorListContainer.Children.Add(errorControl);
        }

        private void UpdateErrorCount()
        {
            var unresolvedErrors = _progressTracker.ErrorHistory.Count(e => !e.IsResolved);
            ErrorCount.Text = unresolvedErrors.ToString();

            // Update header color based on error severity
            var hasErrors = _progressTracker.ErrorHistory.Any(e => !e.IsResolved && e.Severity >= ErrorSeverity.Error);
            var hasWarnings = _progressTracker.ErrorHistory.Any(e => !e.IsResolved && e.Severity == ErrorSeverity.Warning);

#if WINDOWS_UWP
            if (hasErrors)
            {
                HeaderText.Foreground = new SolidColorBrush(Colors.Red);
            }
            else if (hasWarnings)
            {
                HeaderText.Foreground = new SolidColorBrush(Colors.Orange);
            }
            else
            {
                HeaderText.Foreground = new SolidColorBrush(Colors.White);
            }
#elif WINDOWS_PHONE
            if (hasErrors)
            {
                HeaderText.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Red);
            }
            else if (hasWarnings)
            {
                HeaderText.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Orange);
            }
            else
            {
                HeaderText.Foreground = new SolidColorBrush(System.Windows.Media.Colors.White);
            }
#endif
        }

        private async void OnErrorRetryRequested(object sender, ErrorRetryEventArgs e)
        {
            try
            {
                var success = await ErrorRecoveryManager.AttemptRecoveryAsync(e.Error);
                if (success)
                {
                    _progressTracker.ResolveError(e.Error.Id);
                    RefreshErrorList();
                }
            }
            catch (Exception ex)
            {
                _progressTracker.ReportError("ErrorPanel", $"Retry failed: {ex.Message}", ex, ErrorSeverity.Warning);
            }
        }

        private void OnErrorDismissRequested(object sender, ErrorDismissEventArgs e)
        {
            _progressTracker.ResolveError(e.Error.Id);
            RefreshErrorList();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnClearAllClick(object sender, RoutedEventArgs e)
        {
            _progressTracker.ClearErrorHistory();
            RefreshErrorList();
        }

        private async void OnRetryAllClick(object sender, RoutedEventArgs e)
        {
            var unresolvedErrors = _progressTracker.ErrorHistory.Where(e => !e.IsResolved).ToList();

            foreach (var error in unresolvedErrors)
            {
                try
                {
                    var success = await ErrorRecoveryManager.AttemptRecoveryAsync(error);
                    if (success)
                    {
                        _progressTracker.ResolveError(error.Id);
                    }
                }
                catch (Exception ex)
                {
                    _progressTracker.ReportError("ErrorPanel", $"Bulk retry failed for {error.Source}: {ex.Message}", ex, ErrorSeverity.Warning);
                }
            }

            RefreshErrorList();
        }
    }

    public class ErrorItemControl : Border
    {
        private ErrorReport _error;

        public event EventHandler<ErrorRetryEventArgs> RetryRequested;
        public event EventHandler<ErrorDismissEventArgs> DismissRequested;

        public ErrorItemControl(ErrorReport error)
        {
            _error = error;
            BuildUI();
        }

        private void BuildUI()
        {
            Margin = new Thickness(0, 4, 0, 4);
            Padding = new Thickness(8);
            CornerRadius = new CornerRadius(2);

            // Set background based on severity
#if WINDOWS_UWP
            Background = _error.Severity switch
            {
                ErrorSeverity.Critical => new SolidColorBrush(Color.FromArgb(40, 255, 0, 0)),
                ErrorSeverity.Error => new SolidColorBrush(Color.FromArgb(30, 255, 100, 100)),
                ErrorSeverity.Warning => new SolidColorBrush(Color.FromArgb(30, 255, 200, 0)),
                _ => new SolidColorBrush(Color.FromArgb(20, 100, 100, 100))
            };
#elif WINDOWS_PHONE
            Background = _error.Severity switch
            {
                ErrorSeverity.Critical => new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 0, 0)),
                ErrorSeverity.Error => new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 255, 100, 100)),
                ErrorSeverity.Warning => new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 255, 200, 0)),
                _ => new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 100, 100, 100))
            };
#endif

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Error info
            var infoPanel = new StackPanel();

            var titleText = new TextBlock
            {
                Text = $"[{_error.SeverityText}] {_error.Source}",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12
            };
            infoPanel.Children.Add(titleText);

            var messageText = new TextBlock
            {
                Text = _error.Message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0)
            };
            infoPanel.Children.Add(messageText);

            var timeText = new TextBlock
            {
                Text = _error.FormattedTimestamp,
                FontSize = 10,
                Opacity = 0.7,
                Margin = new Thickness(0, 2, 0, 0)
            };
            infoPanel.Children.Add(timeText);

            Grid.SetColumn(infoPanel, 0);
            grid.Children.Add(infoPanel);

            // Retry button
            var retryButton = new Button
            {
                Content = "Retry",
                FontSize = 10,
                MinWidth = 50,
                Height = 24,
                Margin = new Thickness(4, 0, 0, 0)
            };
            retryButton.Click += OnRetryClick;
            Grid.SetColumn(retryButton, 1);
            grid.Children.Add(retryButton);

            // Dismiss button
            var dismissButton = new Button
            {
                Content = "✕",
                FontSize = 10,
                Width = 24,
                Height = 24,
                Margin = new Thickness(4, 0, 0, 0)
            };
            dismissButton.Click += OnDismissClick;
            Grid.SetColumn(dismissButton, 2);
            grid.Children.Add(dismissButton);

            Child = grid;
        }

        private void OnRetryClick(object sender, RoutedEventArgs e)
        {
            RetryRequested?.Invoke(this, new ErrorRetryEventArgs(_error));
        }

        private void OnDismissClick(object sender, RoutedEventArgs e)
        {
            DismissRequested?.Invoke(this, new ErrorDismissEventArgs(_error));
        }
    }

    public class ErrorRetryEventArgs : EventArgs
    {
        public ErrorReport Error { get; }

        public ErrorRetryEventArgs(ErrorReport error)
        {
            Error = error;
        }
    }

    public class ErrorDismissEventArgs : EventArgs
    {
        public ErrorReport Error { get; }

        public ErrorDismissEventArgs(ErrorReport error)
        {
            Error = error;
        }
    }
}
