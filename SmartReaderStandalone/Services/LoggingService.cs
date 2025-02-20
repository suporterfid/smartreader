using Serilog.Core;
using Serilog.Events;

namespace SmartReaderStandalone.Services
{
    public class LoggingService
    {
        private readonly LoggingLevelSwitch _levelSwitch;
        private readonly ILogger<LoggingService> _logger;

        public LoggingService(LoggingLevelSwitch levelSwitch, ILogger<LoggingService> logger)
        {
            _levelSwitch = levelSwitch;
            _logger = logger;
        }

        public bool SetLogLevel(string level)
        {
            try
            {
                var newLevel = level.ToUpper() switch
                {
                    "VERBOSE" => LogEventLevel.Verbose,
                    "DEBUG" => LogEventLevel.Debug,
                    "INFORMATION" => LogEventLevel.Information,
                    "WARNING" => LogEventLevel.Warning,
                    "ERROR" => LogEventLevel.Error,
                    "FATAL" => LogEventLevel.Fatal,
                    _ => throw new ArgumentException($"Invalid log level: {level}")
                };

                _levelSwitch.MinimumLevel = newLevel;
                _logger.LogInformation("Log level changed to: {Level}", newLevel);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to change log level to: {Level}", level);
                return false;
            }
        }

        public LogEventLevel GetCurrentLogLevel()
        {
            return _levelSwitch.MinimumLevel;
        }
    }

    // Create a DTO for the log level request
    public class LogLevelRequest
    {
        public required string Level { get; set; }
    }

    // Create a DTO for the log level response
    public class LogLevelResponse
    {
        public required string CurrentLevel { get; set; }
        public required string Message { get; set; }
        public bool Success { get; set; }
    }
}
