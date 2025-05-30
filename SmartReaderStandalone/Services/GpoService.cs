using SmartReader.IotDeviceInterface;
using Microsoft.Extensions.Logging;
using SmartReaderStandalone.IotDeviceInterface;

namespace SmartReaderStandalone.Services
{
    public interface IGpoService
    {
        Task<ExtendedGpoConfigurationRequest> GetCurrentConfigurationAsync();
        Task UpdateConfigurationAsync(ExtendedGpoConfigurationRequest configuration);
        Task SetGpoStateAsync(int gpo, GpoState state);
        Task PulseGpoAsync(int gpo, int durationMs);
        Task SetNetworkControlAsync(int gpo);
        Task ResetToDefaultAsync();
    }

    /// <summary>
    /// Enhanced GPO service supporting only 3 GPO ports with improved state management
    /// </summary>
    public class GpoService : IGpoService
    {
        private readonly IR700IotReader _reader;
        private readonly ILogger<GpoService> _logger;
        private readonly ReaderConfiguration _configuration;
        private readonly SemaphoreSlim _gpoLock = new(1, 1);

        private const int MAX_GPO_PORTS = 3;
        private const int MIN_GPO_PORT = 1;

        public GpoService(
            IR700IotReader reader,
            ILogger<GpoService> logger,
            ReaderConfiguration configuration)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// Gets the current GPO configuration from the reader with proper state handling
        /// </summary>
        public async Task<ExtendedGpoConfigurationRequest> GetCurrentConfigurationAsync()
        {
            await _gpoLock.WaitAsync();
            try
            {
                _logger.LogInformation("Getting current GPO configuration for {MaxPorts} ports", MAX_GPO_PORTS);

                var readerConfig = await _reader.GetGpoExtendedAsync();

                // Filter and normalize configurations to only include ports 1-3
                var filteredConfigurations = new List<ExtendedGpoConfiguration>();

                // Ensure we have configurations for all 3 ports
                for (int port = MIN_GPO_PORT; port <= MAX_GPO_PORTS; port++)
                {
                    var existingConfig = readerConfig.GpoConfigurations
                        .FirstOrDefault(g => g.Gpo == port);

                    if (existingConfig != null)
                    {
                        // Normalize the existing configuration
                        var normalizedConfig = NormalizeGpoConfiguration(existingConfig);
                        filteredConfigurations.Add(normalizedConfig);
                    }
                    else
                    {
                        // Create a default configuration for missing ports
                        _logger.LogDebug("Creating default configuration for missing GPO port {Port}", port);
                        filteredConfigurations.Add(ExtendedGpoConfiguration.CreateDefault(port));
                    }
                }

                var result = new ExtendedGpoConfigurationRequest
                {
                    GpoConfigurations = filteredConfigurations.OrderBy(g => g.Gpo).ToList()
                };

                _logger.LogInformation("Successfully retrieved GPO configuration for {ConfigCount} ports",
                    result.GpoConfigurations.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get current GPO configuration");
                throw new InvalidOperationException("Failed to retrieve GPO configuration from reader", ex);
            }
            finally
            {
                _gpoLock.Release();
            }
        }

        /// <summary>
        /// Updates the complete GPO configuration with enhanced validation
        /// </summary>
        public async Task UpdateConfigurationAsync(ExtendedGpoConfigurationRequest configuration)
        {
            await _gpoLock.WaitAsync();
            try
            {
                if (configuration?.GpoConfigurations == null)
                {
                    throw new ArgumentNullException(nameof(configuration), "Configuration cannot be null");
                }

                _logger.LogInformation("Updating GPO configuration for {ConfigCount} ports",
                    configuration.GpoConfigurations.Count);

                // Validate the configuration
                var validationResult = configuration.Validate();
                if (!validationResult.IsValid)
                {
                    var errorMessage = string.Join("; ", validationResult.Errors);
                    _logger.LogError("GPO configuration validation failed: {Errors}", errorMessage);
                    throw new ArgumentException($"Invalid GPO configuration: {errorMessage}");
                }

                // Filter to only valid ports and ensure proper state
                var validConfigurations = configuration.GpoConfigurations
                    .Where(g => g.Gpo >= MIN_GPO_PORT && g.Gpo <= MAX_GPO_PORTS)
                    .Select(g => g.EnsureStateSet())
                    .ToList();

                if (!validConfigurations.Any())
                {
                    throw new ArgumentException("No valid GPO configurations provided");
                }

                // Validate against reader configuration limits
                ValidateAgainstReaderLimits(validConfigurations);

                // Create the filtered request
                var filteredRequest = new ExtendedGpoConfigurationRequest
                {
                    GpoConfigurations = validConfigurations
                };

                // Update the reader configuration
                await _reader.UpdateReaderGpoExtendedAsync(filteredRequest);

                _logger.LogInformation("Successfully updated GPO configuration for {UpdatedCount} ports",
                    validConfigurations.Count);
            }
            catch (Exception ex) when (!(ex is ArgumentException || ex is ArgumentNullException))
            {
                _logger.LogError(ex, "Failed to update GPO configuration");
                throw new InvalidOperationException("Failed to update GPO configuration on reader", ex);
            }
            finally
            {
                _gpoLock.Release();
            }
        }

        /// <summary>
        /// Sets a specific GPO to a static state with validation
        /// </summary>
        public async Task SetGpoStateAsync(int gpo, GpoState state)
        {
            ValidateGpoNumber(gpo);

            _logger.LogInformation("Setting GPO {Gpo} to state {State}", gpo, state);

            var config = new ExtendedGpoConfigurationRequest
            {
                GpoConfigurations = new List<ExtendedGpoConfiguration>
                {
                    ExtendedGpoConfiguration.CreateStatic(gpo, state)
                }
            };

            await UpdateConfigurationAsync(config);
        }

        /// <summary>
        /// Pulses a specific GPO with validation
        /// </summary>
        public async Task PulseGpoAsync(int gpo, int durationMs)
        {
            ValidateGpoNumber(gpo);

            if (durationMs <= 0)
            {
                durationMs = _configuration.Gpo.DefaultPulseDurationMs;
            }

            if (durationMs > _configuration.Gpo.MaxPulseDurationMs)
            {
                throw new ArgumentException(
                    $"Pulse duration {durationMs}ms exceeds maximum allowed {_configuration.Gpo.MaxPulseDurationMs}ms");
            }

            _logger.LogInformation("Pulsing GPO {Gpo} for {Duration}ms", gpo, durationMs);

            var config = new ExtendedGpoConfigurationRequest
            {
                GpoConfigurations = new List<ExtendedGpoConfiguration>
                {
                    ExtendedGpoConfiguration.CreatePulsed(gpo, durationMs)
                }
            };

            await UpdateConfigurationAsync(config);
        }

        /// <summary>
        /// Sets a specific GPO to network control mode
        /// </summary>
        public async Task SetNetworkControlAsync(int gpo)
        {
            ValidateGpoNumber(gpo);

            if (!_configuration.Gpo.AllowNetworkControl)
            {
                throw new InvalidOperationException("Network control mode is disabled in configuration");
            }

            _logger.LogInformation("Setting GPO {Gpo} to network control mode", gpo);

            var config = new ExtendedGpoConfigurationRequest
            {
                GpoConfigurations = new List<ExtendedGpoConfiguration>
                {
                    ExtendedGpoConfiguration.CreateNetworkControlled(gpo)
                }
            };

            await UpdateConfigurationAsync(config);
        }

        /// <summary>
        /// Resets all GPOs to default configuration (Static/Low)
        /// </summary>
        public async Task ResetToDefaultAsync()
        {
            _logger.LogInformation("Resetting all {MaxPorts} GPOs to default configuration", MAX_GPO_PORTS);

            var defaultConfig = GpoConfigurationExtensions.CreateDefaultThreePortConfiguration();
            await UpdateConfigurationAsync(defaultConfig);

            _logger.LogInformation("Successfully reset all GPOs to default configuration");
        }

        /// <summary>
        /// Sets all GPOs to reader control mode
        /// </summary>
        public async Task SetAllToReaderControlAsync()
        {
            _logger.LogInformation("Setting all {MaxPorts} GPOs to reader control mode", MAX_GPO_PORTS);

            var config = new ExtendedGpoConfigurationRequest
            {
                GpoConfigurations = new List<ExtendedGpoConfiguration>
                {
                    ExtendedGpoConfiguration.CreateReaderControlled(1),
                    ExtendedGpoConfiguration.CreateReaderControlled(2),
                    ExtendedGpoConfiguration.CreateReaderControlled(3)
                }
            };

            await UpdateConfigurationAsync(config);
        }

        /// <summary>
        /// Gets the status of all GPO ports
        /// </summary>
        public async Task<Dictionary<int, string>> GetGpoStatusAsync()
        {
            var configuration = await GetCurrentConfigurationAsync();
            var status = new Dictionary<int, string>();

            foreach (var gpo in configuration.GpoConfigurations)
            {
                var statusText = $"Control: {gpo.Control}";
                if (gpo.State.HasValue)
                {
                    statusText += $", State: {gpo.State}";
                }
                if (gpo.PulseDurationMilliseconds.HasValue)
                {
                    statusText += $", Pulse: {gpo.PulseDurationMilliseconds}ms";
                }

                status[gpo.Gpo] = statusText;
            }

            return status;
        }

        #region Private Helper Methods

        /// <summary>
        /// Validates GPO port number
        /// </summary>
        private static void ValidateGpoNumber(int gpo)
        {
            if (gpo < MIN_GPO_PORT || gpo > MAX_GPO_PORTS)
            {
                throw new ArgumentException(
                    $"GPO number {gpo} is invalid. Only GPO ports {MIN_GPO_PORT}-{MAX_GPO_PORTS} are supported.");
            }
        }

        /// <summary>
        /// Normalizes GPO configuration to ensure consistent state representation
        /// </summary>
        private ExtendedGpoConfiguration NormalizeGpoConfiguration(ExtendedGpoConfiguration gpo)
        {
            var normalized = new ExtendedGpoConfiguration
            {
                Gpo = gpo.Gpo,
                Control = gpo.Control,
                PulseDurationMilliseconds = gpo.PulseDurationMilliseconds
            };

            // Ensure state is properly set based on control mode
            switch (gpo.Control)
            {
                case GpoControlMode.Static:
                case GpoControlMode.Pulsed:
                    // These modes require a state
                    normalized.State = gpo.State ?? GpoState.Low; // Default to Low if not specified
                    break;

                case GpoControlMode.Reader:
                case GpoControlMode.Network:
                    // These modes don't use static state
                    normalized.State = null;
                    break;

                default:
                    // Unknown mode, default to Low state
                    normalized.State = GpoState.Low;
                    break;
            }

            return normalized;
        }

        /// <summary>
        /// Validates configurations against reader limits
        /// </summary>
        private void ValidateAgainstReaderLimits(List<ExtendedGpoConfiguration> configurations)
        {
            foreach (var config in configurations)
            {
                // Check network control permission
                if (config.Control == GpoControlMode.Network && !_configuration.Gpo.AllowNetworkControl)
                {
                    throw new InvalidOperationException(
                        $"Network control mode is disabled for GPO {config.Gpo}");
                }

                // Check pulse duration limits
                if (config.Control == GpoControlMode.Pulsed && config.PulseDurationMilliseconds.HasValue)
                {
                    if (config.PulseDurationMilliseconds > _configuration.Gpo.MaxPulseDurationMs)
                    {
                        throw new ArgumentException(
                            $"GPO {config.Gpo}: Pulse duration {config.PulseDurationMilliseconds}ms exceeds " +
                            $"maximum allowed {_configuration.Gpo.MaxPulseDurationMs}ms");
                    }
                }
            }
        }

        #endregion

        /// <summary>
        /// Disposes the service and releases resources
        /// </summary>
        public void Dispose()
        {
            _gpoLock?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}