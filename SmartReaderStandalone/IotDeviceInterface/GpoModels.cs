using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartReaderStandalone.IotDeviceInterface
{
    /// <summary>
    /// Represents the control mode for a GPO port
    /// </summary>
    public enum GpoControlMode
    {
        Static,    // Manual control with high/low state
        Pulsed,    // Pulse mode with configurable duration
        Network,   // Network-controlled mode
        Reader     // Reader-controlled mode (default/legacy)
    }

    /// <summary>
    /// Represents the state of a GPO port
    /// </summary>
    public enum GpoState
    {
        Low,
        High
    }

    /// <summary>
    /// Extended GPO configuration supporting advanced modes
    /// </summary>
    public class ExtendedGpoConfiguration
    {
        /// <summary>
        /// GPO port number (1-4)
        /// </summary>
        public int Gpo { get; set; }

        /// <summary>
        /// Control mode for the GPO
        /// </summary>
        public GpoControlMode Control { get; set; }

        /// <summary>
        /// State of the GPO (used in static and pulsed modes)
        /// </summary>
        public GpoState? State { get; set; }

        /// <summary>
        /// Pulse duration in milliseconds (used in pulsed mode)
        /// </summary>
        public int? PulseDurationMilliseconds { get; set; }

        /// <summary>
        /// Validates the configuration based on the control mode
        /// </summary>
        public ValidationResult Validate()
        {
            var errors = new List<string>();

            if (Gpo < 1 || Gpo > 4)
            {
                errors.Add($"GPO port must be between 1 and 4, got {Gpo}");
            }

            switch (Control)
            {
                case GpoControlMode.Static:
                    if (!State.HasValue)
                    {
                        errors.Add("State is required for static control mode");
                    }
                    break;

                case GpoControlMode.Pulsed:
                    if (!State.HasValue)
                    {
                        errors.Add("State is required for pulsed control mode");
                    }
                    if (!PulseDurationMilliseconds.HasValue || PulseDurationMilliseconds <= 0)
                    {
                        errors.Add("PulseDurationMilliseconds must be greater than 0 for pulsed mode");
                    }
                    if (PulseDurationMilliseconds > 60000) // 60 seconds max
                    {
                        errors.Add("PulseDurationMilliseconds cannot exceed 60000ms (60 seconds)");
                    }
                    break;

                case GpoControlMode.Network:
                    // Network mode doesn't require state or duration
                    break;

                case GpoControlMode.Reader:
                    // Reader mode is the default and doesn't require additional parameters
                    break;
            }

            return new ValidationResult(errors);
        }
    }

    /// <summary>
    /// Wrapper for multiple GPO configurations
    /// </summary>
    public class ExtendedGpoConfigurationRequest
    {
        public List<ExtendedGpoConfiguration> GpoConfigurations { get; set; } = new();

        /// <summary>
        /// Validates all GPO configurations
        /// </summary>
        public ValidationResult Validate()
        {
            var errors = new List<string>();

            if (GpoConfigurations == null || !GpoConfigurations.Any())
            {
                errors.Add("At least one GPO configuration is required");
                return new ValidationResult(errors);
            }

            // Check for duplicate GPO ports
            var duplicatePorts = GpoConfigurations
                .GroupBy(g => g.Gpo)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            foreach (var port in duplicatePorts)
            {
                errors.Add($"Duplicate configuration for GPO port {port}");
            }

            // Validate each configuration
            foreach (var config in GpoConfigurations)
            {
                var validationResult = config.Validate();
                errors.AddRange(validationResult.Errors);
            }

            return new ValidationResult(errors);
        }
    }
}
