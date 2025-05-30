using SmartReaderStandalone.IotDeviceInterface;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartReaderStandalone.IotDeviceInterface
{
    /// <summary>
    /// Enhanced GPO configuration request supporting only 3 GPO ports
    /// </summary>
    public class ExtendedGpoConfigurationRequest
    {
        /// <summary>
        /// List of GPO configurations (maximum 3 ports supported)
        /// </summary>
        public List<ExtendedGpoConfiguration> GpoConfigurations { get; set; } = new();

        /// <summary>
        /// Validates the GPO configuration request
        /// </summary>
        /// <returns>Validation result with any errors</returns>
        public ValidationResult Validate()
        {
            var errors = new List<string>();

            if (GpoConfigurations == null)
            {
                errors.Add("GPO configurations list cannot be null");
                return new ValidationResult(errors);
            }

            // Check for maximum 3 GPO ports
            if (GpoConfigurations.Count > 3)
            {
                errors.Add("Maximum 3 GPO ports supported");
            }

            // Validate each GPO configuration
            foreach (var config in GpoConfigurations)
            {
                var configErrors = ValidateGpoConfiguration(config);
                errors.AddRange(configErrors);
            }

            // Check for duplicate GPO numbers
            var duplicateGpos = GpoConfigurations
                .GroupBy(g => g.Gpo)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            foreach (var duplicate in duplicateGpos)
            {
                errors.Add($"Duplicate GPO port configuration found: {duplicate}");
            }

            return new ValidationResult(errors);
        }

        private List<string> ValidateGpoConfiguration(ExtendedGpoConfiguration config)
        {
            var errors = new List<string>();

            // Validate GPO port number (1-3 only)
            if (config.Gpo < 1 || config.Gpo > 3)
            {
                errors.Add($"GPO port {config.Gpo} is invalid. Only ports 1-3 are supported");
            }

            // Validate control mode specific requirements
            switch (config.Control)
            {
                case GpoControlMode.Static:
                    if (!config.State.HasValue)
                    {
                        errors.Add($"GPO {config.Gpo}: State is required for Static control mode");
                    }
                    break;

                case GpoControlMode.Pulsed:
                    if (!config.State.HasValue)
                    {
                        errors.Add($"GPO {config.Gpo}: State is required for Pulsed control mode");
                    }
                    if (!config.PulseDurationMilliseconds.HasValue || config.PulseDurationMilliseconds <= 0)
                    {
                        errors.Add($"GPO {config.Gpo}: Valid pulse duration is required for Pulsed control mode");
                    }
                    if (config.PulseDurationMilliseconds > 60000)
                    {
                        errors.Add($"GPO {config.Gpo}: Pulse duration cannot exceed 60000ms");
                    }
                    break;

                case GpoControlMode.Reader:
                    // Reader control mode doesn't require additional validation
                    break;

                case GpoControlMode.Network:
                    // Network control mode doesn't require additional validation
                    break;

                default:
                    errors.Add($"GPO {config.Gpo}: Invalid control mode");
                    break;
            }

            return errors;
        }
    }

    /// <summary>
    /// Enhanced GPO configuration for individual ports
    /// </summary>
    public class ExtendedGpoConfiguration
    {
        /// <summary>
        /// GPO port number (1-3)
        /// </summary>
        [Range(1, 3, ErrorMessage = "GPO port must be between 1 and 3")]
        public int Gpo { get; set; }

        /// <summary>
        /// GPO control mode
        /// </summary>
        [Required]
        public GpoControlMode Control { get; set; }

        /// <summary>
        /// GPO state (required for Static and Pulsed modes)
        /// </summary>
        public GpoState? State { get; set; }

        /// <summary>
        /// Pulse duration in milliseconds (required for Pulsed mode)
        /// </summary>
        [Range(100, 60000, ErrorMessage = "Pulse duration must be between 100 and 60000 milliseconds")]
        public int? PulseDurationMilliseconds { get; set; }

        /// <summary>
        /// Creates a default GPO configuration
        /// </summary>
        /// <param name="gpoNumber">GPO port number (1-3)</param>
        /// <returns>Default configuration with Static control and Low state</returns>
        public static ExtendedGpoConfiguration CreateDefault(int gpoNumber)
        {
            if (gpoNumber < 1 || gpoNumber > 3)
            {
                throw new ArgumentException("GPO number must be between 1 and 3", nameof(gpoNumber));
            }

            return new ExtendedGpoConfiguration
            {
                Gpo = gpoNumber,
                Control = GpoControlMode.Static,
                State = GpoState.Low
            };
        }

        /// <summary>
        /// Creates a static GPO configuration
        /// </summary>
        /// <param name="gpoNumber">GPO port number (1-3)</param>
        /// <param name="state">GPO state</param>
        /// <returns>Static GPO configuration</returns>
        public static ExtendedGpoConfiguration CreateStatic(int gpoNumber, GpoState state)
        {
            if (gpoNumber < 1 || gpoNumber > 3)
            {
                throw new ArgumentException("GPO number must be between 1 and 3", nameof(gpoNumber));
            }

            return new ExtendedGpoConfiguration
            {
                Gpo = gpoNumber,
                Control = GpoControlMode.Static,
                State = state
            };
        }

        /// <summary>
        /// Creates a pulsed GPO configuration
        /// </summary>
        /// <param name="gpoNumber">GPO port number (1-3)</param>
        /// <param name="durationMs">Pulse duration in milliseconds</param>
        /// <returns>Pulsed GPO configuration</returns>
        public static ExtendedGpoConfiguration CreatePulsed(int gpoNumber, int durationMs)
        {
            if (gpoNumber < 1 || gpoNumber > 3)
            {
                throw new ArgumentException("GPO number must be between 1 and 3", nameof(gpoNumber));
            }

            if (durationMs < 100 || durationMs > 60000)
            {
                throw new ArgumentException("Pulse duration must be between 100 and 60000 milliseconds", nameof(durationMs));
            }

            return new ExtendedGpoConfiguration
            {
                Gpo = gpoNumber,
                Control = GpoControlMode.Pulsed,
                State = GpoState.High,
                PulseDurationMilliseconds = durationMs
            };
        }

        /// <summary>
        /// Creates a reader-controlled GPO configuration
        /// </summary>
        /// <param name="gpoNumber">GPO port number (1-3)</param>
        /// <returns>Reader-controlled GPO configuration</returns>
        public static ExtendedGpoConfiguration CreateReaderControlled(int gpoNumber)
        {
            if (gpoNumber < 1 || gpoNumber > 3)
            {
                throw new ArgumentException("GPO number must be between 1 and 3", nameof(gpoNumber));
            }

            return new ExtendedGpoConfiguration
            {
                Gpo = gpoNumber,
                Control = GpoControlMode.Reader
            };
        }

        /// <summary>
        /// Creates a network-controlled GPO configuration
        /// </summary>
        /// <param name="gpoNumber">GPO port number (1-3)</param>
        /// <returns>Network-controlled GPO configuration</returns>
        public static ExtendedGpoConfiguration CreateNetworkControlled(int gpoNumber)
        {
            if (gpoNumber < 1 || gpoNumber > 3)
            {
                throw new ArgumentException("GPO number must be between 1 and 3", nameof(gpoNumber));
            }

            return new ExtendedGpoConfiguration
            {
                Gpo = gpoNumber,
                Control = GpoControlMode.Network
            };
        }
    }

    /// <summary>
    /// GPO control modes
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GpoControlMode
    {
        /// <summary>
        /// Static control - GPO state is set manually and remains fixed
        /// </summary>
        Static = 0,

        /// <summary>
        /// Reader control - GPO state is controlled by reader operations (e.g., tag reading)
        /// </summary>
        Reader = 1,

        /// <summary>
        /// Pulsed control - GPO generates a pulse for a specified duration
        /// </summary>
        Pulsed = 2,

        /// <summary>
        /// Network control - GPO state is controlled via network commands
        /// </summary>
        Network = 3
    }

    /// <summary>
    /// GPO states
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GpoState
    {
        /// <summary>
        /// Low state (0V)
        /// </summary>
        Low = 0,

        /// <summary>
        /// High state (typically 3.3V or 5V)
        /// </summary>
        High = 1
    }

    /// <summary>
    /// Validation result for GPO configurations
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// List of validation errors
        /// </summary>
        public List<string> Errors { get; }

        /// <summary>
        /// True if validation passed (no errors)
        /// </summary>
        public bool IsValid => !Errors.Any();

        /// <summary>
        /// Creates a new validation result
        /// </summary>
        /// <param name="errors">List of validation errors</param>
        public ValidationResult(List<string> errors)
        {
            Errors = errors ?? new List<string>();
        }

        /// <summary>
        /// Creates a successful validation result
        /// </summary>
        /// <returns>Validation result with no errors</returns>
        public static ValidationResult Success()
        {
            return new ValidationResult(new List<string>());
        }

        /// <summary>
        /// Creates a failed validation result with a single error
        /// </summary>
        /// <param name="error">Error message</param>
        /// <returns>Validation result with the specified error</returns>
        public static ValidationResult Failure(string error)
        {
            return new ValidationResult(new List<string> { error });
        }
    }
}

/// <summary>
/// Extension methods for GPO configuration utilities
/// </summary>
public static class GpoConfigurationExtensions
{
    /// <summary>
    /// Creates a default configuration for all 3 GPO ports
    /// </summary>
    /// <returns>Configuration with all 3 ports set to Static/Low</returns>
    public static ExtendedGpoConfigurationRequest CreateDefaultThreePortConfiguration()
    {
        return new ExtendedGpoConfigurationRequest
        {
            GpoConfigurations = new List<ExtendedGpoConfiguration>
            {
                ExtendedGpoConfiguration.CreateDefault(1),
                ExtendedGpoConfiguration.CreateDefault(2),
                ExtendedGpoConfiguration.CreateDefault(3)
            }
        };
    }

    /// <summary>
    /// Filters GPO configurations to only include valid ports (1-3)
    /// </summary>
    /// <param name="configurations">Original configurations</param>
    /// <returns>Filtered configurations with only ports 1-3</returns>
    public static List<ExtendedGpoConfiguration> FilterToValidPorts(this List<ExtendedGpoConfiguration> configurations)
    {
        return configurations.Where(config => config.Gpo >= 1 && config.Gpo <= 3).ToList();
    }

    /// <summary>
    /// Ensures state is set for configurations that require it
    /// </summary>
    /// <param name="config">GPO configuration</param>
    /// <returns>Configuration with state ensured</returns>
    public static ExtendedGpoConfiguration EnsureStateSet(this ExtendedGpoConfiguration config)
    {
        if ((config.Control == GpoControlMode.Static || config.Control == GpoControlMode.Pulsed) && !config.State.HasValue)
        {
            config.State = GpoState.Low; // Default to Low
        }
        return config;
    }
}