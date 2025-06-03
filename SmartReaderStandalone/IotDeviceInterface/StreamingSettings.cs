#region copyright
//****************************************************************************************************
// Copyright ©2025 Impinj, Inc.All rights reserved.              
//                                    
// You may use and modify this code under the terms of the Impinj Software Tools License & Disclaimer. 
// Visit https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer   
// for full license details, or contact Impinj, Inc.at support@impinj.com for a copy of the license.   
//
//****************************************************************************************************
#endregion
namespace SmartReaderStandalone.IotDeviceInterface
{
    public class StreamingSettings
    {
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan MessageTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets or sets the interval between heartbeat messages in seconds.
        /// This helps monitor connection health.
        /// </summary>
        public int HeartbeatIntervalSeconds { get; set; }

        /// <summary>
        /// Gets or sets the timeout for individual messages in seconds.
        /// If no message is received within this time, the connection may be considered stale.
        /// </summary>
        public int MessageTimeoutSeconds { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of concurrent streams allowed.
        /// This helps prevent resource exhaustion.
        /// </summary>
        public int MaxConcurrentStreams { get; set; }

        /// <summary>
        /// Gets or sets the size of the buffer used for receiving stream data.
        /// Larger buffers can improve performance but use more memory.
        /// </summary>
        public int BufferSize { get; set; }

        /// <summary>
        /// Gets or sets whether to automatically reconnect on connection loss.
        /// </summary>
        public bool AutoReconnect { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of automatic reconnection attempts.
        /// Only used when AutoReconnect is true.
        /// </summary>
        public int MaxReconnectAttempts { get; set; }

        /// <summary>
        /// Gets or sets whether to compress stream data.
        /// Can reduce bandwidth usage but increases CPU usage.
        /// </summary>
        public bool UseCompression { get; set; }

        /// <summary>
        /// Creates a new instance of StreamingSettings with production-ready default values.
        /// These defaults are chosen based on common RFID reader usage patterns.
        /// </summary>
        public static StreamingSettings CreateDefault()
        {
            return new StreamingSettings
            {
                // 30-second heartbeat interval provides good balance between 
                // connection monitoring and network overhead
                HeartbeatIntervalSeconds = 30,

                // 10-second message timeout allows for network latency while
                // still detecting connection issues promptly
                MessageTimeoutSeconds = 10,

                // Single stream is sufficient for most use cases and 
                // prevents resource contention
                MaxConcurrentStreams = 1,

                // 8KB buffer size is suitable for typical RFID data volumes
                // while maintaining reasonable memory usage
                BufferSize = 8192,

                // Auto-reconnect enabled by default for better reliability
                AutoReconnect = true,

                // Limit reconnection attempts to prevent infinite retry loops
                MaxReconnectAttempts = 3,

                // Compression disabled by default as RFID data is typically
                // not highly compressible
                UseCompression = false
            };
        }

        /// <summary>
        /// Validates the streaming settings configuration.
        /// </summary>
        /// <returns>A collection of validation error messages.</returns>
        internal IEnumerable<string> Validate()
        {
            // Validate heartbeat interval
            if (HeartbeatIntervalSeconds <= 0)
            {
                yield return "Heartbeat interval must be greater than 0 seconds";
            }
            else if (HeartbeatIntervalSeconds < 5)
            {
                yield return "Heartbeat interval less than 5 seconds may cause excessive network traffic";
            }

            // Validate message timeout
            if (MessageTimeoutSeconds <= 0)
            {
                yield return "Message timeout must be greater than 0 seconds";
            }


            // Validate concurrent streams
            if (MaxConcurrentStreams <= 0)
            {
                yield return "Maximum concurrent streams must be greater than 0";
            }
            else if (MaxConcurrentStreams > 5)
            {
                yield return "High number of concurrent streams may impact performance";
            }

            // Validate buffer size
            if (BufferSize <= 0)
            {
                yield return "Buffer size must be greater than 0";
            }
            else if (BufferSize < 1024)
            {
                yield return "Buffer size less than 1KB may impact performance";
            }
            else if (BufferSize > 1024 * 1024)
            {
                yield return "Buffer size greater than 1MB may cause memory issues";
            }

            // Validate reconnection settings
            if (AutoReconnect && MaxReconnectAttempts <= 0)
            {
                yield return "Maximum reconnection attempts must be greater than 0 when auto-reconnect is enabled";
            }
        }
    }

}
