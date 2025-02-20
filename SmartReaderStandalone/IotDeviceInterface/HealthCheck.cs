using System.Collections.Concurrent;

namespace SmartReaderStandalone.IotDeviceInterface
{
    /// <summary>
    /// Manages health monitoring for the RFID reader's streaming connection.
    /// Provides thread-safe tracking of connection health metrics and diagnostics.
    /// </summary>
    public class HealthCheck
    {
        // Atomic counter for received messages
        private long _messageCount;

        // Thread-safe timestamp storage using ticks
        private long _lastHeartbeatTicks;
        private long _lastMessageReceivedTicks;
        private long _streamStartTimeTicks;

        // Status tracking
        private volatile int _currentStatus;

        // Concurrent collection for recent errors
        private readonly ConcurrentQueue<HealthCheckError> _recentErrors;

        // Configuration settings
        private readonly TimeSpan _heartbeatTimeout;
        private readonly TimeSpan _messageTimeout;
        private readonly int _maxErrorCount;

        public HealthCheck(
            TimeSpan? heartbeatTimeout = null,
            TimeSpan? messageTimeout = null,
            int maxErrorCount = 100)
        {
            _heartbeatTimeout = heartbeatTimeout ?? TimeSpan.FromSeconds(30);
            _messageTimeout = messageTimeout ?? TimeSpan.FromSeconds(10);
            _maxErrorCount = maxErrorCount;

            _recentErrors = new ConcurrentQueue<HealthCheckError>();

            var now = DateTime.UtcNow;
            _ = Interlocked.Exchange(ref _lastHeartbeatTicks, now.Ticks);
            _ = Interlocked.Exchange(ref _lastMessageReceivedTicks, now.Ticks);
            _ = Interlocked.Exchange(ref _streamStartTimeTicks, now.Ticks);

            _currentStatus = (int)HealthStatus.Starting;
            _messageCount = 0;
        }

        /// <summary>
        /// Gets or sets the last heartbeat time in a thread-safe manner.
        /// </summary>
        public DateTime LastHeartbeat
        {
            get => new(Interlocked.Read(ref _lastHeartbeatTicks));
            private set => Interlocked.Exchange(ref _lastHeartbeatTicks, value.Ticks);
        }

        /// <summary>
        /// Gets or sets the last message received time in a thread-safe manner.
        /// </summary>
        public DateTime LastMessageReceived
        {
            get => new(Interlocked.Read(ref _lastMessageReceivedTicks));
            private set => Interlocked.Exchange(ref _lastMessageReceivedTicks, value.Ticks);
        }

        /// <summary>
        /// Gets or sets the stream start time in a thread-safe manner.
        /// </summary>
        public DateTime StreamStartTime
        {
            get => new(Interlocked.Read(ref _streamStartTimeTicks));
            private set => Interlocked.Exchange(ref _streamStartTimeTicks, value.Ticks);
        }

        /// <summary>
        /// Gets the current health status in a thread-safe manner.
        /// </summary>
        public HealthStatus CurrentStatus
        {
            get => (HealthStatus)Thread.VolatileRead(ref _currentStatus);
            private set => Interlocked.Exchange(ref _currentStatus, (int)value);
        }

        /// <summary>
        /// Records a successful heartbeat from the stream.
        /// </summary>
        public void RecordHeartbeat()
        {
            LastHeartbeat = DateTime.UtcNow;
            UpdateStatus();
        }

        /// <summary>
        /// Records the receipt of a message from the stream.
        /// </summary>
        public void RecordMessageReceived()
        {
            LastMessageReceived = DateTime.UtcNow;
            _ = Interlocked.Increment(ref _messageCount);
            UpdateStatus();
        }

        /// <summary>
        /// Records an error that occurred during streaming.
        /// </summary>
        public void RecordError(Exception error, ErrorSeverity severity)
        {
            var healthError = new HealthCheckError(error, severity, DateTime.UtcNow);

            _recentErrors.Enqueue(healthError);

            // Maintain error queue size
            while (_recentErrors.Count > _maxErrorCount)
            {
                _ = _recentErrors.TryDequeue(out _);
            }

            UpdateStatus();
        }

        /// <summary>
        /// Marks the start of a new streaming session.
        /// </summary>
        public void MarkStreamStart()
        {
            var now = DateTime.UtcNow;
            StreamStartTime = now;
            LastHeartbeat = now;
            LastMessageReceived = now;

            _ = Interlocked.Exchange(ref _messageCount, 0);
            CurrentStatus = HealthStatus.Starting;

            while (_recentErrors.TryDequeue(out _)) { }
        }

        /// <summary>
        /// Gets the total number of messages received.
        /// </summary>
        public long MessageCount => Interlocked.Read(ref _messageCount);

        /// <summary>
        /// Gets detailed health metrics for the stream.
        /// </summary>
        public HealthMetrics GetHealthMetrics()
        {
            var now = DateTime.UtcNow;
            var lastHeartbeat = LastHeartbeat;
            var lastMessage = LastMessageReceived;
            var startTime = StreamStartTime;

            return new HealthMetrics
            {
                Status = CurrentStatus,
                Uptime = now - startTime,
                TimeSinceLastHeartbeat = now - lastHeartbeat,
                TimeSinceLastMessage = now - lastMessage,
                TotalMessageCount = MessageCount,
                RecentErrors = _recentErrors.ToArray(),
                MessagesPerMinute = CalculateMessageRate()
            };
        }

        private void UpdateStatus()
        {
            var now = DateTime.UtcNow;
            var timeSinceHeartbeat = now - LastHeartbeat;
            var timeSinceMessage = now - LastMessageReceived;
            var recentSevereErrors = _recentErrors
                .Count(e => e.Timestamp > now.AddMinutes(-1) &&
                           e.Severity == ErrorSeverity.Critical);

            var newStatus = DetermineHealthStatus(
                timeSinceHeartbeat,
                timeSinceMessage,
                recentSevereErrors);

            CurrentStatus = newStatus;
        }

        private HealthStatus DetermineHealthStatus(
            TimeSpan timeSinceHeartbeat,
            TimeSpan timeSinceMessage,
            int recentSevereErrors)
        {
            if (recentSevereErrors >= 3)
            {
                return HealthStatus.Failed;
            }

            if (timeSinceHeartbeat > _heartbeatTimeout)
            {
                return HealthStatus.Unhealthy;
            }

            if (timeSinceMessage > _messageTimeout)
            {
                return HealthStatus.Degraded;
            }

            return HealthStatus.Healthy;
        }

        private double CalculateMessageRate()
        {
            var uptime = DateTime.UtcNow - StreamStartTime;
            if (uptime.TotalMinutes < 1)
            {
                return MessageCount;
            }
            return MessageCount / uptime.TotalMinutes;
        }
    }

    /// <summary>
    /// Represents the current health status of the stream.
    /// </summary>
    public enum HealthStatus
    {
        Starting,
        Healthy,
        Degraded,
        Unhealthy,
        Failed
    }

    /// <summary>
    /// Represents the severity of a health check error.
    /// </summary>
    public enum ErrorSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// Represents a health check error with its associated metadata.
    /// </summary>
    public class HealthCheckError
    {
        public Exception Error { get; }
        public ErrorSeverity Severity { get; }
        public DateTime Timestamp { get; }

        public HealthCheckError(Exception error, ErrorSeverity severity, DateTime timestamp)
        {
            Error = error;
            Severity = severity;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    /// Contains detailed metrics about the stream's health.
    /// The properties use PascalCase following C# conventions.
    /// </summary>
    public class HealthMetrics
    {
        /// <summary>
        /// The current health status of the stream
        /// </summary>
        public HealthStatus Status { get; set; }

        /// <summary>
        /// The total time the stream has been running
        /// </summary>
        public TimeSpan Uptime { get; set; }

        /// <summary>
        /// Time elapsed since the last heartbeat was received
        /// </summary>
        public TimeSpan TimeSinceLastHeartbeat { get; set; }

        /// <summary>
        /// Time elapsed since the last message was received
        /// </summary>
        public TimeSpan TimeSinceLastMessage { get; set; }

        /// <summary>
        /// Total number of messages received since stream start
        /// </summary>
        public long TotalMessageCount { get; set; }

        /// <summary>
        /// Average number of messages received per minute
        /// </summary>
        public double MessagesPerMinute { get; set; }

        /// <summary>
        /// Collection of recent errors that have occurred
        /// </summary>
        public required HealthCheckError[] RecentErrors { get; set; }
    }
}
