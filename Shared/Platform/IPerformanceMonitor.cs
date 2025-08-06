using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Shared.Platform
{
    /// <summary>
    /// Interface for performance monitoring
    /// </summary>
    public interface IPerformanceMonitor
    {
        /// <summary>
        /// Starts timing an operation
        /// </summary>
        /// <param name="operationName">Name of the operation</param>
        /// <returns>Disposable timer that stops when disposed</returns>
        IDisposable StartTimer(string operationName);

        /// <summary>
        /// Records a metric value
        /// </summary>
        /// <param name="metricName">Name of the metric</param>
        /// <param name="value">Metric value</param>
        /// <param name="unit">Unit of measurement</param>
        void RecordMetric(string metricName, double value, string unit = null);

        /// <summary>
        /// Increments a counter
        /// </summary>
        /// <param name="counterName">Name of the counter</param>
        /// <param name="increment">Amount to increment (default 1)</param>
        void IncrementCounter(string counterName, long increment = 1);

        /// <summary>
        /// Records memory usage
        /// </summary>
        /// <param name="category">Memory category</param>
        void RecordMemoryUsage(string category = "General");

        /// <summary>
        /// Gets performance statistics
        /// </summary>
        /// <returns>Performance statistics</returns>
        PerformanceStatistics GetStatistics();

        /// <summary>
        /// Resets all performance counters
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// Performance statistics
    /// </summary>
    public class PerformanceStatistics
    {
        /// <summary>
        /// Operation timing statistics
        /// </summary>
        public Dictionary<string, OperationStats> Operations { get; set; } = new Dictionary<string, OperationStats>();

        /// <summary>
        /// Metric values
        /// </summary>
        public Dictionary<string, MetricStats> Metrics { get; set; } = new Dictionary<string, MetricStats>();

        /// <summary>
        /// Counter values
        /// </summary>
        public Dictionary<string, long> Counters { get; set; } = new Dictionary<string, long>();

        /// <summary>
        /// Memory usage statistics
        /// </summary>
        public Dictionary<string, MemoryStats> MemoryUsage { get; set; } = new Dictionary<string, MemoryStats>();

        /// <summary>
        /// When statistics were last reset
        /// </summary>
        public DateTime LastReset { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Operation timing statistics
    /// </summary>
    public class OperationStats
    {
        /// <summary>
        /// Total number of operations
        /// </summary>
        public long Count { get; set; }

        /// <summary>
        /// Total time spent in milliseconds
        /// </summary>
        public double TotalTimeMs { get; set; }

        /// <summary>
        /// Average time per operation in milliseconds
        /// </summary>
        public double AverageTimeMs => Count > 0 ? TotalTimeMs / Count : 0;

        /// <summary>
        /// Minimum time in milliseconds
        /// </summary>
        public double MinTimeMs { get; set; } = double.MaxValue;

        /// <summary>
        /// Maximum time in milliseconds
        /// </summary>
        public double MaxTimeMs { get; set; }

        /// <summary>
        /// Last operation time in milliseconds
        /// </summary>
        public double LastTimeMs { get; set; }

        /// <summary>
        /// When the operation was last executed
        /// </summary>
        public DateTime LastExecuted { get; set; }
    }

    /// <summary>
    /// Metric statistics
    /// </summary>
    public class MetricStats
    {
        /// <summary>
        /// Number of metric recordings
        /// </summary>
        public long Count { get; set; }

        /// <summary>
        /// Sum of all values
        /// </summary>
        public double Sum { get; set; }

        /// <summary>
        /// Average value
        /// </summary>
        public double Average => Count > 0 ? Sum / Count : 0;

        /// <summary>
        /// Minimum value
        /// </summary>
        public double Min { get; set; } = double.MaxValue;

        /// <summary>
        /// Maximum value
        /// </summary>
        public double Max { get; set; } = double.MinValue;

        /// <summary>
        /// Last recorded value
        /// </summary>
        public double LastValue { get; set; }

        /// <summary>
        /// Unit of measurement
        /// </summary>
        public string Unit { get; set; }

        /// <summary>
        /// When the metric was last recorded
        /// </summary>
        public DateTime LastRecorded { get; set; }
    }

    /// <summary>
    /// Memory usage statistics
    /// </summary>
    public class MemoryStats
    {
        /// <summary>
        /// Number of memory recordings
        /// </summary>
        public long Count { get; set; }

        /// <summary>
        /// Current memory usage in bytes
        /// </summary>
        public long CurrentBytes { get; set; }

        /// <summary>
        /// Peak memory usage in bytes
        /// </summary>
        public long PeakBytes { get; set; }

        /// <summary>
        /// Average memory usage in bytes
        /// </summary>
        public long AverageBytes { get; set; }

        /// <summary>
        /// When memory was last recorded
        /// </summary>
        public DateTime LastRecorded { get; set; }
    }

    /// <summary>
    /// Disposable timer for measuring operation duration
    /// </summary>
    public class OperationTimer : IDisposable
    {
        private readonly string _operationName;
        private readonly PerformanceMonitor _monitor;
        private readonly Stopwatch _stopwatch;
        private bool _disposed = false;

        internal OperationTimer(string operationName, PerformanceMonitor monitor)
        {
            _operationName = operationName ?? throw new ArgumentNullException(nameof(operationName));
            _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _stopwatch.Stop();
                _monitor.RecordOperationTime(_operationName, _stopwatch.Elapsed.TotalMilliseconds);
                _disposed = true;
            }
        }
    }
}
