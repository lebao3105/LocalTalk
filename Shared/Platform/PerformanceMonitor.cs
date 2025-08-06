using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace Shared.Platform
{
    /// <summary>
    /// Thread-safe performance monitor implementation
    /// </summary>
    public class PerformanceMonitor : IPerformanceMonitor
    {
        private readonly ConcurrentDictionary<string, OperationStats> _operations = new ConcurrentDictionary<string, OperationStats>();
        private readonly ConcurrentDictionary<string, MetricStats> _metrics = new ConcurrentDictionary<string, MetricStats>();
        private readonly ConcurrentDictionary<string, long> _counters = new ConcurrentDictionary<string, long>();
        private readonly ConcurrentDictionary<string, MemoryStats> _memoryStats = new ConcurrentDictionary<string, MemoryStats>();
        private readonly ILogger _logger;
        private DateTime _lastReset = DateTime.Now;

        public PerformanceMonitor()
        {
            _logger = LogManager.GetLogger<PerformanceMonitor>();
        }

        /// <summary>
        /// Starts timing an operation
        /// </summary>
        public IDisposable StartTimer(string operationName)
        {
            if (string.IsNullOrEmpty(operationName))
                throw new ArgumentNullException(nameof(operationName));

            return new OperationTimer(operationName, this);
        }

        /// <summary>
        /// Records a metric value
        /// </summary>
        public void RecordMetric(string metricName, double value, string unit = null)
        {
            if (string.IsNullOrEmpty(metricName))
                throw new ArgumentNullException(nameof(metricName));

            _metrics.AddOrUpdate(metricName,
                new MetricStats
                {
                    Count = 1,
                    Sum = value,
                    Min = value,
                    Max = value,
                    LastValue = value,
                    Unit = unit,
                    LastRecorded = DateTime.Now
                },
                (key, existing) =>
                {
                    existing.Count++;
                    existing.Sum += value;
                    existing.Min = Math.Min(existing.Min, value);
                    existing.Max = Math.Max(existing.Max, value);
                    existing.LastValue = value;
                    existing.LastRecorded = DateTime.Now;
                    if (string.IsNullOrEmpty(existing.Unit))
                        existing.Unit = unit;
                    return existing;
                });
        }

        /// <summary>
        /// Increments a counter
        /// </summary>
        public void IncrementCounter(string counterName, long increment = 1)
        {
            if (string.IsNullOrEmpty(counterName))
                throw new ArgumentNullException(nameof(counterName));

            _counters.AddOrUpdate(counterName, increment, (key, existing) => existing + increment);
        }

        /// <summary>
        /// Records memory usage
        /// </summary>
        public void RecordMemoryUsage(string category = "General")
        {
            if (string.IsNullOrEmpty(category))
                category = "General";

            var currentMemory = GC.GetTotalMemory(false);
            var now = DateTime.Now;

            _memoryStats.AddOrUpdate(category,
                new MemoryStats
                {
                    Count = 1,
                    CurrentBytes = currentMemory,
                    PeakBytes = currentMemory,
                    AverageBytes = currentMemory,
                    LastRecorded = now
                },
                (key, existing) =>
                {
                    existing.Count++;
                    existing.CurrentBytes = currentMemory;
                    existing.PeakBytes = Math.Max(existing.PeakBytes, currentMemory);
                    existing.AverageBytes = (existing.AverageBytes * (existing.Count - 1) + currentMemory) / existing.Count;
                    existing.LastRecorded = now;
                    return existing;
                });
        }

        /// <summary>
        /// Gets performance statistics
        /// </summary>
        public PerformanceStatistics GetStatistics()
        {
            return new PerformanceStatistics
            {
                Operations = new System.Collections.Generic.Dictionary<string, OperationStats>(_operations),
                Metrics = new System.Collections.Generic.Dictionary<string, MetricStats>(_metrics),
                Counters = new System.Collections.Generic.Dictionary<string, long>(_counters),
                MemoryUsage = new System.Collections.Generic.Dictionary<string, MemoryStats>(_memoryStats),
                LastReset = _lastReset
            };
        }

        /// <summary>
        /// Resets all performance counters
        /// </summary>
        public void Reset()
        {
            _operations.Clear();
            _metrics.Clear();
            _counters.Clear();
            _memoryStats.Clear();
            _lastReset = DateTime.Now;
            _logger.Info("Performance monitor reset");
        }

        /// <summary>
        /// Records operation timing (internal method for OperationTimer)
        /// </summary>
        internal void RecordOperationTime(string operationName, double timeMs)
        {
            var now = DateTime.Now;

            _operations.AddOrUpdate(operationName,
                new OperationStats
                {
                    Count = 1,
                    TotalTimeMs = timeMs,
                    MinTimeMs = timeMs,
                    MaxTimeMs = timeMs,
                    LastTimeMs = timeMs,
                    LastExecuted = now
                },
                (key, existing) =>
                {
                    existing.Count++;
                    existing.TotalTimeMs += timeMs;
                    existing.MinTimeMs = Math.Min(existing.MinTimeMs, timeMs);
                    existing.MaxTimeMs = Math.Max(existing.MaxTimeMs, timeMs);
                    existing.LastTimeMs = timeMs;
                    existing.LastExecuted = now;
                    return existing;
                });
        }
    }

    /// <summary>
    /// Static performance monitor for easy access
    /// </summary>
    public static class PerformanceManager
    {
        private static IPerformanceMonitor _current = new PerformanceMonitor();
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets the current performance monitor instance
        /// </summary>
        public static IPerformanceMonitor Current
        {
            get
            {
                lock (_lock)
                {
                    return _current;
                }
            }
        }

        /// <summary>
        /// Sets the performance monitor instance
        /// </summary>
        public static void SetMonitor(IPerformanceMonitor monitor)
        {
            if (monitor == null)
                throw new ArgumentNullException(nameof(monitor));

            lock (_lock)
            {
                _current = monitor;
            }
        }

        /// <summary>
        /// Convenience method to start timing an operation
        /// </summary>
        public static IDisposable Time(string operationName)
        {
            return Current.StartTimer(operationName);
        }

        /// <summary>
        /// Convenience method to record a metric
        /// </summary>
        public static void Metric(string metricName, double value, string unit = null)
        {
            Current.RecordMetric(metricName, value, unit);
        }

        /// <summary>
        /// Convenience method to increment a counter
        /// </summary>
        public static void Counter(string counterName, long increment = 1)
        {
            Current.IncrementCounter(counterName, increment);
        }

        /// <summary>
        /// Convenience method to record memory usage
        /// </summary>
        public static void Memory(string category = "General")
        {
            Current.RecordMemoryUsage(category);
        }
    }
}
