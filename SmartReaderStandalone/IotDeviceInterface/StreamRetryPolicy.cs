namespace SmartReaderStandalone.IotDeviceInterface
{
    public class StreamRetryPolicy
    {
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the maximum number of retry attempts.
        /// </summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>
        /// Gets or sets the initial delay between retries in milliseconds.
        /// </summary>
        public int InitialDelayMs { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the maximum delay between retries in milliseconds.
        /// </summary>
        public int MaxDelayMs { get; set; } = 30000;

        /// <summary>
        /// Gets or sets whether to use exponential backoff for retry delays.
        /// </summary>
        public bool UseExponentialBackoff { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to add jitter to retry delays.
        /// This helps prevent thundering herd problems in distributed systems.
        /// </summary>
        public bool AddJitter { get; set; } = true;

        /// <summary>
        /// Gets or sets which HTTP status codes should trigger a retry.
        /// </summary>
        public List<int> RetryableStatusCodes { get; set; } = new();

        /// <summary>
        /// Initializes a new instance of StreamRetryPolicy with default values.
        /// </summary>
        public StreamRetryPolicy()
        {
            // Garantir que RetryableStatusCodes está sempre inicializada
            if (RetryableStatusCodes == null || RetryableStatusCodes.Count == 0)
            {
                RetryableStatusCodes = new List<int>
                {
                    408, // Request Timeout
                    429, // Too Many Requests
                    500, // Internal Server Error
                    502, // Bad Gateway
                    503, // Service Unavailable
                    504  // Gateway Timeout
                };
            }
        }

        /// <summary>
        /// Creates a new instance of RetryPolicy with production-ready default values.
        /// These defaults are chosen to provide reliable operation while preventing
        /// excessive resource usage.
        /// </summary>
        public static StreamRetryPolicy CreateDefault()
        {
            return new StreamRetryPolicy
            {
                // 3 attempts provides good balance between reliability and timely failure
                MaxAttempts = 3,

                // Start with a 1-second delay
                InitialDelayMs = 1000,

                // Cap maximum delay at 30 seconds to prevent excessive waiting
                MaxDelayMs = 30000,

                // Use exponential backoff to prevent overwhelming the system
                UseExponentialBackoff = true,

                // Add jitter to prevent synchronized retry attempts
                AddJitter = true,

                // Common retryable HTTP status codes
                RetryableStatusCodes = new List<int>
                {
                    408, // Request Timeout
                    429, // Too Many Requests
                    500, // Internal Server Error
                    502, // Bad Gateway
                    503, // Service Unavailable
                    504  // Gateway Timeout
                }
            };
        }

        /// <summary>
        /// Validates the retry policy configuration.
        /// </summary>
        /// <returns>A collection of validation error messages.</returns>
        internal IEnumerable<string> Validate()
        {
            // Validate retry attempts
            if (MaxAttempts <= 0)
            {
                yield return "Maximum retry attempts must be greater than 0";
            }
            else if (MaxAttempts > 10)
            {
                yield return "High number of retry attempts may cause excessive delays";
            }

            // Validate delay settings
            if (InitialDelayMs <= 0)
            {
                yield return "Initial retry delay must be greater than 0 milliseconds";
            }

            if (MaxDelayMs <= InitialDelayMs)
            {
                yield return "Maximum retry delay must be greater than initial delay";
            }

            if (MaxDelayMs > 300000) // 5 minutes
            {
                yield return "Maximum retry delay exceeds 5 minutes, which may cause timeout issues";
            }

            // Validate status codes if provided
            if (RetryableStatusCodes?.Count > 0)
            {
                foreach (var statusCode in RetryableStatusCodes)
                {
                    if (statusCode < 100 || statusCode > 599)
                    {
                        yield return $"Invalid HTTP status code for retry: {statusCode}";
                    }
                }
            }
            else
            {
                yield return "RetryableStatusCodes list cannot be null or empty";
            }
        }

        /// <summary>
        /// Calculates the delay for a specific retry attempt.
        /// </summary>
        /// <param name="attemptNumber">The current attempt number (1-based).</param>
        /// <returns>The delay duration in milliseconds.</returns>
        public int CalculateDelayForAttempt(int attemptNumber)
        {
            if (attemptNumber <= 0)
                throw new ArgumentException("Attempt number must be greater than 0");

            int delay = InitialDelayMs;

            if (UseExponentialBackoff)
            {
                delay *= (int)Math.Pow(2, attemptNumber - 1);
            }

            if (AddJitter)
            {
                var random = new Random();
                delay = (int)(delay * (0.8 + (random.NextDouble() * 0.4))); // ±20% jitter
            }

            return Math.Min(delay, MaxDelayMs);
        }

        /// <summary>
        /// Determines if a given HTTP status code should trigger a retry.
        /// </summary>
        /// <param name="statusCode">The HTTP status code to check.</param>
        /// <returns>True if the status code should trigger a retry; otherwise, false.</returns>
        public bool ShouldRetry(int statusCode)
        {
            return RetryableStatusCodes?.Contains(statusCode) ?? false;
        }

        /// <summary>
        /// Creates a copy of this retry policy with modified settings.
        /// </summary>
        /// <returns>A new StreamRetryPolicy instance with copied settings.</returns>
        public StreamRetryPolicy Clone()
        {
            return new StreamRetryPolicy
            {
                MaxAttempts = this.MaxAttempts,
                InitialDelayMs = this.InitialDelayMs,
                MaxDelayMs = this.MaxDelayMs,
                UseExponentialBackoff = this.UseExponentialBackoff,
                AddJitter = this.AddJitter,
                RetryableStatusCodes = this.RetryableStatusCodes?.ToList() ?? new List<int>(),
                InitialDelay = this.InitialDelay,
                MaxDelay = this.MaxDelay
            };
        }

        /// <summary>
        /// Resets the retry policy to default values.
        /// </summary>
        public void ResetToDefaults()
        {
            MaxAttempts = 3;
            InitialDelayMs = 1000;
            MaxDelayMs = 30000;
            UseExponentialBackoff = true;
            AddJitter = true;
            InitialDelay = TimeSpan.FromSeconds(1);
            MaxDelay = TimeSpan.FromSeconds(30);

            RetryableStatusCodes = new List<int>
            {
                408, // Request Timeout
                429, // Too Many Requests
                500, // Internal Server Error
                502, // Bad Gateway
                503, // Service Unavailable
                504  // Gateway Timeout
            };
        }
    }
}