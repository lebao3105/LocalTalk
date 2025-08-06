using System;
using System.Collections.Generic;
using System.Linq;
using Shared.Workflows;
using Shared.Platform;

#if WINDOWS_PHONE
using System.Windows;
using System.Windows.Controls;
#else
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
#endif

namespace Shared.Views
{
    public sealed partial class ProgressIndicator : UserControl
    {
        private ProgressTracker _progressTracker;
        private bool _showDetails;
        private List<OperationProgressControl> _operationControls;

        public ProgressIndicator()
        {
            this.InitializeComponent();
            _progressTracker = ProgressTracker.Instance;
            _operationControls = new List<OperationProgressControl>();

            // Subscribe to progress tracker events
            _progressTracker.ProgressUpdated += OnProgressUpdated;
            _progressTracker.PropertyChanged += OnProgressTrackerPropertyChanged;
            _progressTracker.ActiveOperations.CollectionChanged += OnActiveOperationsChanged;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateDisplay();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Unsubscribe from events to prevent memory leaks
            if (_progressTracker != null)
            {
                _progressTracker.ProgressUpdated -= OnProgressUpdated;
                _progressTracker.PropertyChanged -= OnProgressTrackerPropertyChanged;
                _progressTracker.ActiveOperations.CollectionChanged -= OnActiveOperationsChanged;
            }

            // Clean up operation controls
            _operationControls.Clear();
        }

        private void OnProgressUpdated(object sender, ProgressUpdatedEventArgs e)
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                MainProgressBar.Value = e.Progress;
                ProgressText.Text = $"{e.Progress:F1}%";
                OperationText.Text = e.Operation ?? "Ready";
            });
        }

        private void OnProgressTrackerPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProgressTracker.CurrentOperation))
            {
                PlatformFactory.Current.RunOnUIThread(() =>
                {
                    OperationText.Text = _progressTracker.CurrentOperation ?? "Ready";
                });
            }
        }

        private void OnActiveOperationsChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                UpdateOperationsList();
                UpdateDetailsVisibility();
            });
        }

        private void UpdateDisplay()
        {
            MainProgressBar.Value = _progressTracker.OverallProgress;
            ProgressText.Text = $"{_progressTracker.OverallProgress:F1}%";
            OperationText.Text = _progressTracker.CurrentOperation ?? "Ready";
            
            UpdateOperationsList();
            UpdateDetailsVisibility();
        }

        private void UpdateOperationsList()
        {
            // Clear existing operation controls
            _operationControls.Clear();
            OperationsContainer.Children.Clear();

            // Add current operations
            foreach (var operation in _progressTracker.ActiveOperations)
            {
                var operationControl = new OperationProgressControl(operation);
                _operationControls.Add(operationControl);
                OperationsContainer.Children.Add(operationControl);
            }
        }

        private void UpdateDetailsVisibility()
        {
            var hasOperations = _progressTracker.ActiveOperations.Any();
            
            ToggleDetailsButton.Visibility = hasOperations ? Visibility.Visible : Visibility.Collapsed;
            
            if (_showDetails && hasOperations)
            {
                OperationsScrollViewer.Visibility = Visibility.Visible;
                ToggleDetailsButton.Content = "▲";
            }
            else
            {
                OperationsScrollViewer.Visibility = Visibility.Collapsed;
                ToggleDetailsButton.Content = "▼";
            }
        }

        private void OnToggleDetailsClick(object sender, RoutedEventArgs e)
        {
            _showDetails = !_showDetails;
            UpdateDetailsVisibility();
        }
    }

    public class OperationProgressControl : Border
    {
        private ProgressOperation _operation;
        private TextBlock _nameText;
        private TextBlock _statusText;
        private ProgressBar _progressBar;

        public OperationProgressControl(ProgressOperation operation)
        {
            _operation = operation;
            BuildUI();
            
            // Subscribe to operation changes
            _operation.PropertyChanged += OnOperationPropertyChanged;
        }

        private void BuildUI()
        {
            Margin = new Thickness(0, 2, 0, 2);
            Padding = new Thickness(8, 4);
            CornerRadius = new CornerRadius(2);
            Background = new SolidColorBrush(
#if WINDOWS_UWP
                Windows.UI.Color.FromArgb(20, 100, 100, 100)
#elif WINDOWS_PHONE
                System.Windows.Media.Color.FromArgb(20, 100, 100, 100)
#endif
            );

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Operation name and progress percentage
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _nameText = new TextBlock
            {
                Text = _operation.Description,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(_nameText, 0);
            headerGrid.Children.Add(_nameText);

            var progressText = new TextBlock
            {
                Text = $"{_operation.Progress:F1}%",
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(progressText, 1);
            headerGrid.Children.Add(progressText);

            Grid.SetRow(headerGrid, 0);
            grid.Children.Add(headerGrid);

            // Progress bar
            _progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = _operation.Progress,
                Height = 3,
                Margin = new Thickness(0, 2, 0, 2)
            };
            Grid.SetRow(_progressBar, 1);
            grid.Children.Add(_progressBar);

            // Status message
            _statusText = new TextBlock
            {
                Text = _operation.StatusMessage ?? "",
                FontSize = 9,
                Opacity = 0.8,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(_statusText, 2);
            grid.Children.Add(_statusText);

            Child = grid;
        }

        private void OnOperationPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            PlatformFactory.Current.RunOnUIThread(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(ProgressOperation.Progress):
                        _progressBar.Value = _operation.Progress;
                        // Update progress text in header
                        var headerGrid = (Grid)((Grid)Child).Children[0];
                        var progressText = (TextBlock)headerGrid.Children[1];
                        progressText.Text = $"{_operation.Progress:F1}%";
                        break;
                        
                    case nameof(ProgressOperation.StatusMessage):
                        _statusText.Text = _operation.StatusMessage ?? "";
                        break;
                }
            });
        }
    }
}
