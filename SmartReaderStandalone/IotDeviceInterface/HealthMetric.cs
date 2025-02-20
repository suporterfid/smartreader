using System.Collections.ObjectModel;

namespace SmartReaderStandalone.IotDeviceInterface
{
    /// <summary>
    /// Represents different categories of health metrics that can be monitored.
    /// </summary>
    public enum MetricType
    {
        Connection,      // Network connection status
        Performance,     // Performance-related metrics
        Hardware,        // Hardware-related metrics
        Memory,         // Memory usage metrics
        TagReads,       // RFID tag read metrics
        Errors          // Error-related metrics
    }

    /// <summary>
    /// Represents the severity level of a health metric.
    /// </summary>
    public enum MetricSeverity
    {
        Information,    // Normal operation, for informational purposes
        Warning,        // Potential issues that need attention
        Error,         // Serious issues that need immediate attention
        Critical       // Critical issues that may affect system stability
    }

    /// <summary>
    /// Represents a single health metric measurement for the RFID reader.
    /// This class is immutable to ensure thread safety when passing metrics between components.
    /// </summary>
    public class HealthMetric
    {
        /// <summary>
        /// Gets the unique identifier for this metric instance.
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// Gets the timestamp when this metric was recorded.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// Gets the type of metric being recorded.
        /// </summary>
        public MetricType Type { get; }

        /// <summary>
        /// Gets the severity level of this metric.
        /// </summary>
        public MetricSeverity Severity { get; }

        /// <summary>
        /// Gets the name of the metric being measured.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the numerical value of the metric, if applicable.
        /// </summary>
        public double? Value { get; }

        /// <summary>
        /// Gets the unit of measurement for the value, if applicable.
        /// </summary>
        public string Unit { get; }

        /// <summary>
        /// Gets additional context or description for this metric.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets the source component that generated this metric.
        /// </summary>
        public string Source { get; }

        /// <summary>
        /// Gets additional metadata associated with this metric.
        /// </summary>
        public IReadOnlyDictionary<string, string> Metadata { get; }

        /// <summary>
        /// Creates a new instance of the HealthMetric class.
        /// </summary>
        private HealthMetric(
            MetricType type,
            string name,
            MetricSeverity severity,
            double? value,
            string unit,
            string description,
            string source,
            IDictionary<string, string> metadata)
        {
            Id = Guid.NewGuid();
            Timestamp = DateTime.UtcNow;
            Type = type;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Severity = severity;
            Value = value;
            Unit = unit;
            Description = description ?? string.Empty;
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Metadata = new ReadOnlyDictionary<string, string>(
                metadata?.ToDictionary(x => x.Key, x => x.Value) ??
                []);
        }

        /// <summary>
        /// Builder class for creating HealthMetric instances with a fluent interface.
        /// </summary>
        public class Builder
        {
            private MetricType _type;
            private string _name;
            private MetricSeverity _severity = MetricSeverity.Information;
            private double? _value;
            private string _unit;
            private string _description;
            private string _source;
            private readonly Dictionary<string, string> _metadata = [];

            public Builder(MetricType type, string name)
            {
                _type = type;
                _name = name ?? throw new ArgumentNullException(nameof(name));
            }

            public Builder WithSeverity(MetricSeverity severity)
            {
                _severity = severity;
                return this;
            }

            public Builder WithValue(double value, string? unit = null)
            {
                _value = value;
                _unit = unit;
                return this;
            }

            public Builder WithDescription(string description)
            {
                _description = description;
                return this;
            }

            public Builder WithSource(string source)
            {
                _source = source;
                return this;
            }

            public Builder AddMetadata(string key, string value)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    _metadata[key] = value ?? string.Empty;
                }
                return this;
            }

            public HealthMetric Build()
            {
                if (string.IsNullOrEmpty(_source))
                {
                    throw new InvalidOperationException("Source must be specified for health metric");
                }

                return new HealthMetric(
                    _type,
                    _name,
                    _severity,
                    _value,
                    _unit,
                    _description,
                    _source,
                    _metadata);
            }
        }

        /// <summary>
        /// Creates common performance metrics for monitoring reader operations.
        /// </summary>
        public static class Factory
        {
            public static HealthMetric CreateConnectionMetric(bool isConnected, string source)
            {
                return new Builder(MetricType.Connection, "ConnectionStatus")
                    .WithValue(isConnected ? 1 : 0)
                    .WithSeverity(isConnected ? MetricSeverity.Information : MetricSeverity.Error)
                    .WithDescription(isConnected ? "Reader connected" : "Reader disconnected")
                    .WithSource(source)
                    .Build();
            }

            public static HealthMetric CreateTagReadMetric(int tagCount, double readRatePerSecond, string source)
            {
                return new Builder(MetricType.TagReads, "TagReadRate")
                    .WithValue(readRatePerSecond, "tags/sec")
                    .WithSeverity(readRatePerSecond > 0 ? MetricSeverity.Information : MetricSeverity.Warning)
                    .WithDescription($"Current tag read rate with {tagCount} unique tags")
                    .WithSource(source)
                    .AddMetadata("UniqueTagCount", tagCount.ToString())
                    .Build();
            }

            public static HealthMetric CreateMemoryMetric(long bytesInUse, long totalBytes, string source)
            {
                var usagePercentage = (double)bytesInUse / totalBytes * 100;
                var severity = usagePercentage switch
                {
                    > 90 => MetricSeverity.Critical,
                    > 80 => MetricSeverity.Warning,
                    _ => MetricSeverity.Information
                };

                return new Builder(MetricType.Memory, "MemoryUsage")
                    .WithValue(usagePercentage, "%")
                    .WithSeverity(severity)
                    .WithDescription($"Memory usage: {bytesInUse / 1024 / 1024}MB of {totalBytes / 1024 / 1024}MB")
                    .WithSource(source)
                    .AddMetadata("BytesInUse", bytesInUse.ToString())
                    .AddMetadata("TotalBytes", totalBytes.ToString())
                    .Build();
            }
        }

        /// <summary>
        /// Provides methods for analyzing and comparing health metrics.
        /// </summary>
        public static class Analysis
        {
            /// <summary>
            /// Calculates the rate of change between two metric values.
            /// </summary>
            public static double? CalculateRate(HealthMetric older, HealthMetric newer)
            {
                if (older?.Value == null || newer?.Value == null ||
                    older.Name != newer.Name || older.Unit != newer.Unit)
                {
                    return null;
                }

                var timeDiff = newer.Timestamp - older.Timestamp;
                if (timeDiff.TotalSeconds <= 0)
                {
                    return null;
                }

                return (newer.Value - older.Value) / timeDiff.TotalSeconds;
            }

            /// <summary>
            /// Determines if a metric indicates a healthy state.
            /// </summary>
            public static bool IsHealthy(HealthMetric metric)
            {
                return metric.Severity <= MetricSeverity.Warning;
            }
        }
    }
}
