namespace SmartReaderStandalone.Services
{
    using SmartReader.IotDeviceInterface;
    using Microsoft.Extensions.Logging;
    using global::SmartReaderStandalone.IotDeviceInterface;

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

        public class GpoService : IGpoService
        {
            private readonly IR700IotReader _reader;
            private readonly ILogger<GpoService> _logger;
            private readonly ReaderConfiguration _configuration;

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
            /// Gets the current GPO configuration from the reader
            /// </summary>
            public async Task<ExtendedGpoConfigurationRequest> GetCurrentConfigurationAsync()
            {
                try
                {
                    _logger.LogInformation("Getting current GPO configuration");
                    return await _reader.GetGpoExtendedAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get GPO configuration");
                    throw;
                }
            }

            /// <summary>
            /// Updates the complete GPO configuration
            /// </summary>
            public async Task UpdateConfigurationAsync(ExtendedGpoConfigurationRequest configuration)
            {
                try
                {
                    _logger.LogInformation("Updating GPO configuration: {@Configuration}", configuration);

                    // Validate against configured limits
                    foreach (var gpoConfig in configuration.GpoConfigurations)
                    {
                        if (gpoConfig.Control == GpoControlMode.Network &&
                            !_configuration.Gpo.AllowNetworkControl)
                        {
                            throw new InvalidOperationException(
                                $"Network control mode is disabled for GPO {gpoConfig.Gpo}");
                        }

                        if (gpoConfig.PulseDurationMilliseconds.HasValue &&
                            gpoConfig.PulseDurationMilliseconds > _configuration.Gpo.MaxPulseDurationMs)
                        {
                            throw new InvalidOperationException(
                                $"Pulse duration {gpoConfig.PulseDurationMilliseconds}ms exceeds " +
                                $"maximum allowed {_configuration.Gpo.MaxPulseDurationMs}ms");
                        }
                    }

                    await _reader.UpdateReaderGpoExtendedAsync(configuration);
                    _logger.LogInformation("GPO configuration updated successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update GPO configuration");
                    throw;
                }
            }

            /// <summary>
            /// Sets a specific GPO to a static state
            /// </summary>
            public async Task SetGpoStateAsync(int gpo, GpoState state)
            {
                _logger.LogInformation("Setting GPO {Gpo} to {State}", gpo, state);

                var config = new ExtendedGpoConfigurationRequest
                {
                    GpoConfigurations = new List<ExtendedGpoConfiguration>
                {
                    new ExtendedGpoConfiguration
                    {
                        Gpo = gpo,
                        Control = GpoControlMode.Static,
                        State = state
                    }
                }
                };

                await UpdateConfigurationAsync(config);
            }

            /// <summary>
            /// Pulses a specific GPO
            /// </summary>
            public async Task PulseGpoAsync(int gpo, int durationMs)
            {
                _logger.LogInformation("Pulsing GPO {Gpo} for {Duration}ms", gpo, durationMs);

                if (durationMs <= 0)
                {
                    durationMs = _configuration.Gpo.DefaultPulseDurationMs;
                }

                var config = new ExtendedGpoConfigurationRequest
                {
                    GpoConfigurations = new List<ExtendedGpoConfiguration>
                {
                    new ExtendedGpoConfiguration
                    {
                        Gpo = gpo,
                        Control = GpoControlMode.Pulsed,
                        State = GpoState.High,
                        PulseDurationMilliseconds = durationMs
                    }
                }
                };

                await UpdateConfigurationAsync(config);
            }

            /// <summary>
            /// Sets a specific GPO to network control mode
            /// </summary>
            public async Task SetNetworkControlAsync(int gpo)
            {
                _logger.LogInformation("Setting GPO {Gpo} to network control", gpo);

                var config = new ExtendedGpoConfigurationRequest
                {
                    GpoConfigurations = new List<ExtendedGpoConfiguration>
                {
                    new ExtendedGpoConfiguration
                    {
                        Gpo = gpo,
                        Control = GpoControlMode.Network
                    }
                }
                };

                await UpdateConfigurationAsync(config);
            }

            /// <summary>
            /// Resets all GPOs to default configuration
            /// </summary>
            public async Task ResetToDefaultAsync()
            {
                _logger.LogInformation("Resetting GPOs to default configuration");

                var config = new ExtendedGpoConfigurationRequest
                {
                    GpoConfigurations = _configuration.Gpo.DefaultConfigurations
                };

                await UpdateConfigurationAsync(config);
            }
        }
    }
}
