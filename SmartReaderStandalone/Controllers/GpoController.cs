using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartReader.IotDeviceInterface;
using SmartReaderStandalone.IotDeviceInterface;
using SmartReaderStandalone.Services;
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace SmartReaderStandalone.Controllers
{
    /// <summary>
    /// Controller for managing GPO (General Purpose Output) configurations
    /// Supports only 3 GPO ports (1, 2, 3) as per system requirements
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class GpoController : ControllerBase
    {
        private readonly IR700IotReader _reader;
        private readonly ILogger<GpoController> _logger;
        private readonly IGpoService _gpoService;
        private readonly ReaderConfiguration _configuration;

        private const int MAX_GPO_PORTS = 3; // System supports only 3 GPO ports
        private const int MIN_GPO_PORT = 1;  // Minimum GPO port number

        public GpoController(
            IR700IotReader reader,
            ILogger<GpoController> logger,
            IGpoService gpoService,
            ReaderConfiguration configuration)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gpoService = gpoService ?? throw new ArgumentNullException(nameof(gpoService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// Gets the current GPO configuration for all 3 ports
        /// </summary>
        /// <returns>Extended GPO configuration with proper state mapping</returns>
        [HttpGet("configuration")]
        [ProducesResponseType(typeof(ExtendedGpoConfigurationRequest), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ExtendedGpoConfigurationRequest>> GetConfiguration()
        {
            try
            {
                _logger.LogInformation("Retrieving GPO configuration for {MaxPorts} ports", MAX_GPO_PORTS);

                var configuration = await _gpoService.GetCurrentConfigurationAsync();

                // Filter to only include the first 3 GPO ports and ensure proper state mapping
                var filteredConfig = new ExtendedGpoConfigurationRequest
                {
                    GpoConfigurations = configuration.GpoConfigurations
                        .Where(gpo => gpo.Gpo >= MIN_GPO_PORT && gpo.Gpo <= MAX_GPO_PORTS)
                        .Select(gpo => NormalizeGpoConfiguration(gpo))
                        .OrderBy(gpo => gpo.Gpo)
                        .ToList()
                };

                // Ensure we have configurations for all 3 ports
                await EnsureAllPortsConfigured(filteredConfig);

                _logger.LogInformation("Successfully retrieved GPO configuration for {PortCount} ports",
                    filteredConfig.GpoConfigurations.Count);

                return Ok(filteredConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve GPO configuration");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorResponse
                    {
                        Message = "Failed to retrieve GPO configuration",
                        Error = ex.Message,
                        Timestamp = DateTime.UtcNow
                    });
            }
        }

        /// <summary>
        /// Updates the GPO configuration for all 3 ports
        /// </summary>
        /// <param name="request">GPO configuration request</param>
        /// <returns>Success or error response</returns>
        [HttpPost("configuration")]
        [ProducesResponseType(typeof(SuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateConfiguration([FromBody] ExtendedGpoConfigurationRequest request)
        {
            try
            {
                if (request?.GpoConfigurations == null)
                {
                    _logger.LogWarning("Received null or invalid configuration request");
                    return BadRequest(new ValidationErrorResponse
                    {
                        Message = "Invalid configuration request",
                        Errors = new[] { "Configuration request cannot be null or empty" }
                    });
                }

                _logger.LogInformation("Updating GPO configuration for {ConfigCount} ports",
                    request.GpoConfigurations.Count);

                // Validate and filter configurations to only include ports 1-3
                var validConfigurations = ValidateAndFilterConfigurations(request.GpoConfigurations);

                if (!validConfigurations.Any())
                {
                    return BadRequest(new ValidationErrorResponse
                    {
                        Message = "No valid GPO configurations provided",
                        Errors = new[] { $"All GPO configurations must be for ports {MIN_GPO_PORT}-{MAX_GPO_PORTS}" }
                    });
                }

                var filteredRequest = new ExtendedGpoConfigurationRequest
                {
                    GpoConfigurations = validConfigurations
                };

                // Validate the configuration
                var validationResult = filteredRequest.Validate();
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("GPO configuration validation failed: {Errors}",
                        string.Join(", ", validationResult.Errors));
                    return BadRequest(new ValidationErrorResponse
                    {
                        Message = "Invalid configuration",
                        Errors = validationResult.Errors.ToArray()
                    });
                }

                // Update the configuration
                await _gpoService.UpdateConfigurationAsync(filteredRequest);

                _logger.LogInformation("Successfully updated GPO configuration for {UpdatedCount} ports",
                    validConfigurations.Count);

                return Ok(new SuccessResponse
                {
                    Message = "GPO configuration updated successfully",
                    Data = new { UpdatedPorts = validConfigurations.Count, Timestamp = DateTime.UtcNow }
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid GPO configuration provided");
                return BadRequest(new ValidationErrorResponse
                {
                    Message = "Invalid configuration",
                    Errors = new[] { ex.Message }
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Failed to update GPO configuration due to operational error");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorResponse
                    {
                        Message = "Failed to update GPO configuration",
                        Error = ex.Message,
                        Timestamp = DateTime.UtcNow
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while updating GPO configuration");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorResponse
                    {
                        Message = "Failed to update GPO configuration",
                        Error = ex.Message,
                        Timestamp = DateTime.UtcNow
                    });
            }
        }

        /// <summary>
        /// Sets a specific GPO to a static state
        /// </summary>
        /// <param name="gpoNumber">GPO port number (1-3)</param>
        /// <param name="request">State configuration</param>
        /// <returns>Success or error response</returns>
        [HttpPost("{gpoNumber}/state")]
        [ProducesResponseType(typeof(SuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SetGpoState(int gpoNumber, [FromBody] GpoStateRequest request)
        {
            try
            {
                if (gpoNumber < MIN_GPO_PORT || gpoNumber > MAX_GPO_PORTS)
                {
                    _logger.LogWarning("Invalid GPO number provided: {GpoNumber}", gpoNumber);
                    return BadRequest(new ValidationErrorResponse
                    {
                        Message = $"GPO number must be between {MIN_GPO_PORT} and {MAX_GPO_PORTS}",
                        Errors = new[] { $"Provided GPO number: {gpoNumber}" }
                    });
                }

                if (request == null || string.IsNullOrEmpty(request.State))
                {
                    return BadRequest(new ValidationErrorResponse
                    {
                        Message = "State is required",
                        Errors = new[] { "State must be 'High' or 'Low'" }
                    });
                }

                if (!Enum.TryParse<GpoState>(request.State, true, out var state))
                {
                    return BadRequest(new ValidationErrorResponse
                    {
                        Message = "Invalid state",
                        Errors = new[] { "State must be 'High' or 'Low'" }
                    });
                }

                _logger.LogInformation("Setting GPO {GpoNumber} to state {State}", gpoNumber, state);

                await _gpoService.SetGpoStateAsync(gpoNumber, state);

                return Ok(new SuccessResponse
                {
                    Message = $"GPO {gpoNumber} set to {state}",
                    Data = new { GpoNumber = gpoNumber, State = state.ToString(), Timestamp = DateTime.UtcNow }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set GPO {GpoNumber} state", gpoNumber);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorResponse
                    {
                        Message = $"Failed to set GPO {gpoNumber} state",
                        Error = ex.Message,
                        Timestamp = DateTime.UtcNow
                    });
            }
        }

        /// <summary>
        /// Pulses a specific GPO
        /// </summary>
        /// <param name="gpoNumber">GPO port number (1-3)</param>
        /// <param name="request">Pulse configuration</param>
        /// <returns>Success or error response</returns>
        [HttpPost("{gpoNumber}/pulse")]
        [ProducesResponseType(typeof(SuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PulseGpo(int gpoNumber, [FromBody] GpoPulseRequest request)
        {
            try
            {
                if (gpoNumber < MIN_GPO_PORT || gpoNumber > MAX_GPO_PORTS)
                {
                    _logger.LogWarning("Invalid GPO number provided for pulse: {GpoNumber}", gpoNumber);
                    return BadRequest(new ValidationErrorResponse
                    {
                        Message = $"GPO number must be between {MIN_GPO_PORT} and {MAX_GPO_PORTS}",
                        Errors = new[] { $"Provided GPO number: {gpoNumber}" }
                    });
                }

                if (request == null)
                {
                    request = new GpoPulseRequest(); // Use default duration
                }

                var duration = request.DurationMs > 0 ? request.DurationMs : _configuration.Gpo.DefaultPulseDurationMs;

                if (duration < 100 || duration > _configuration.Gpo.MaxPulseDurationMs)
                {
                    return BadRequest(new ValidationErrorResponse
                    {
                        Message = $"Pulse duration must be between 100 and {_configuration.Gpo.MaxPulseDurationMs} milliseconds",
                        Errors = new[] { $"Provided duration: {duration}ms" }
                    });
                }

                _logger.LogInformation("Pulsing GPO {GpoNumber} for {Duration}ms", gpoNumber, duration);

                await _gpoService.PulseGpoAsync(gpoNumber, duration);

                return Ok(new SuccessResponse
                {
                    Message = $"GPO {gpoNumber} pulsed for {duration}ms",
                    Data = new { GpoNumber = gpoNumber, DurationMs = duration, Timestamp = DateTime.UtcNow }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to pulse GPO {GpoNumber}", gpoNumber);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorResponse
                    {
                        Message = $"Failed to pulse GPO {gpoNumber}",
                        Error = ex.Message,
                        Timestamp = DateTime.UtcNow
                    });
            }
        }

        /// <summary>
        /// Sets a specific GPO to network control mode
        /// </summary>
        /// <param name="gpoNumber">GPO port number (1-3)</param>
        /// <returns>Success or error response</returns>
        [HttpPost("{gpoNumber}/network-control")]
        [ProducesResponseType(typeof(SuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SetNetworkControl(int gpoNumber)
        {
            try
            {
                if (gpoNumber < MIN_GPO_PORT || gpoNumber > MAX_GPO_PORTS)
                {
                    _logger.LogWarning("Invalid GPO number provided for network control: {GpoNumber}", gpoNumber);
                    return BadRequest(new ValidationErrorResponse
                    {
                        Message = $"GPO number must be between {MIN_GPO_PORT} and {MAX_GPO_PORTS}",
                        Errors = new[] { $"Provided GPO number: {gpoNumber}" }
                    });
                }

                if (!_configuration.Gpo.AllowNetworkControl)
                {
                    return BadRequest(new ValidationErrorResponse
                    {
                        Message = "Network control mode is disabled",
                        Errors = new[] { "Network control must be enabled in configuration" }
                    });
                }

                _logger.LogInformation("Setting GPO {GpoNumber} to network control mode", gpoNumber);

                await _gpoService.SetNetworkControlAsync(gpoNumber);

                return Ok(new SuccessResponse
                {
                    Message = $"GPO {gpoNumber} set to network control mode",
                    Data = new { GpoNumber = gpoNumber, ControlMode = "Network", Timestamp = DateTime.UtcNow }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set GPO {GpoNumber} to network control", gpoNumber);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorResponse
                    {
                        Message = $"Failed to set GPO {gpoNumber} to network control",
                        Error = ex.Message,
                        Timestamp = DateTime.UtcNow
                    });
            }
        }

        /// <summary>
        /// Resets all GPOs to default configuration
        /// </summary>
        /// <returns>Success or error response</returns>
        [HttpPost("reset")]
        [ProducesResponseType(typeof(SuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ResetToDefaults()
        {
            try
            {
                _logger.LogInformation("Resetting all {MaxPorts} GPOs to default configuration", MAX_GPO_PORTS);

                await _gpoService.ResetToDefaultAsync();

                return Ok(new SuccessResponse
                {
                    Message = $"All {MAX_GPO_PORTS} GPOs reset to default configuration",
                    Data = new
                    {
                        ResetPorts = MAX_GPO_PORTS,
                        DefaultMode = "Static",
                        DefaultState = "Low",
                        Timestamp = DateTime.UtcNow
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset GPOs to defaults");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorResponse
                    {
                        Message = "Failed to reset GPOs to defaults",
                        Error = ex.Message,
                        Timestamp = DateTime.UtcNow
                    });
            }
        }

        /// <summary>
        /// Gets the status of all GPO ports
        /// </summary>
        /// <returns>Status information for all GPO ports</returns>
        [HttpGet("status")]
        [ProducesResponseType(typeof(GpoStatusResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<GpoStatusResponse>> GetStatus()
        {
            try
            {
                _logger.LogInformation("Getting status for all {MaxPorts} GPO ports", MAX_GPO_PORTS);

                var configuration = await _gpoService.GetCurrentConfigurationAsync();
                var status = new GpoStatusResponse
                {
                    MaxSupportedPorts = MAX_GPO_PORTS,
                    ActivePorts = configuration.GpoConfigurations.Count,
                    Timestamp = DateTime.UtcNow,
                    Ports = new Dictionary<int, GpoPortStatus>()
                };

                foreach (var gpo in configuration.GpoConfigurations.Where(g => g.Gpo >= MIN_GPO_PORT && g.Gpo <= MAX_GPO_PORTS))
                {
                    status.Ports[gpo.Gpo] = new GpoPortStatus
                    {
                        Port = gpo.Gpo,
                        ControlMode = gpo.Control.ToString(),
                        State = gpo.State?.ToString() ?? "Not Applicable",
                        PulseDurationMs = gpo.PulseDurationMilliseconds,
                        IsHealthy = true // Could be enhanced with actual health checks
                    };
                }

                // Ensure all 3 ports are represented
                for (int port = MIN_GPO_PORT; port <= MAX_GPO_PORTS; port++)
                {
                    if (!status.Ports.ContainsKey(port))
                    {
                        status.Ports[port] = new GpoPortStatus
                        {
                            Port = port,
                            ControlMode = "Unknown",
                            State = "Unknown",
                            PulseDurationMs = null,
                            IsHealthy = false
                        };
                    }
                }

                _logger.LogInformation("Successfully retrieved status for {PortCount} GPO ports", status.Ports.Count);

                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve GPO status");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorResponse
                    {
                        Message = "Failed to retrieve GPO status",
                        Error = ex.Message,
                        Timestamp = DateTime.UtcNow
                    });
            }
        }

        /// <summary>
        /// Gets available GPO control modes and their descriptions
        /// </summary>
        /// <returns>List of available control modes</returns>
        [HttpGet("control-modes")]
        [ProducesResponseType(typeof(ControlModesResponse), StatusCodes.Status200OK)]
        [AllowAnonymous] // This endpoint doesn't require authentication
        public ActionResult<ControlModesResponse> GetControlModes()
        {
            try
            {
                var controlModes = new ControlModesResponse
                {
                    SupportedModes = new List<ControlModeInfo>
                    {
                        new ControlModeInfo
                        {
                            Mode = "Static",
                            Description = "GPO state is set manually and remains fixed",
                            RequiresState = true,
                            RequiresPulseDuration = false
                        },
                        new ControlModeInfo
                        {
                            Mode = "Reader",
                            Description = "GPO state is controlled by reader operations (e.g., tag reading)",
                            RequiresState = false,
                            RequiresPulseDuration = false
                        },
                        new ControlModeInfo
                        {
                            Mode = "Pulsed",
                            Description = "GPO generates a pulse for a specified duration",
                            RequiresState = true,
                            RequiresPulseDuration = true
                        }
                    },
                    MaxSupportedPorts = MAX_GPO_PORTS,
                    SupportedPorts = Enumerable.Range(MIN_GPO_PORT, MAX_GPO_PORTS).ToArray(),
                    PulseDurationLimits = new PulseDurationLimits
                    {
                        MinDurationMs = 100,
                        MaxDurationMs = _configuration.Gpo.MaxPulseDurationMs,
                        DefaultDurationMs = _configuration.Gpo.DefaultPulseDurationMs
                    }
                };

                // Add Network mode if enabled
                if (_configuration.Gpo.AllowNetworkControl)
                {
                    controlModes.SupportedModes.Add(new ControlModeInfo
                    {
                        Mode = "Network",
                        Description = "GPO state is controlled via network commands",
                        RequiresState = false,
                        RequiresPulseDuration = false
                    });
                }

                return Ok(controlModes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve control modes");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorResponse
                    {
                        Message = "Failed to retrieve control modes",
                        Error = ex.Message,
                        Timestamp = DateTime.UtcNow
                    });
            }
        }

        #region Private Helper Methods

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

            // Ensure state is properly set - default to Low if null for modes that require state
            switch (gpo.Control)
            {
                case GpoControlMode.Static:
                case GpoControlMode.Pulsed:
                    normalized.State = gpo.State ?? GpoState.Low;
                    break;
                case GpoControlMode.Reader:
                case GpoControlMode.Network:
                    normalized.State = null; // These modes don't use static state
                    break;
                default:
                    normalized.State = GpoState.Low; // Safe default
                    break;
            }

            return normalized;
        }

        /// <summary>
        /// Validates and filters GPO configurations to only include valid ports (1-3)
        /// </summary>
        private List<ExtendedGpoConfiguration> ValidateAndFilterConfigurations(List<ExtendedGpoConfiguration> configurations)
        {
            var validConfigurations = new List<ExtendedGpoConfiguration>();

            foreach (var config in configurations)
            {
                if (config.Gpo >= MIN_GPO_PORT && config.Gpo <= MAX_GPO_PORTS)
                {
                    var normalizedConfig = NormalizeGpoConfiguration(config);
                    validConfigurations.Add(normalizedConfig);
                    _logger.LogDebug("Added valid GPO {GpoNumber} configuration: Control={Control}, State={State}",
                        config.Gpo, config.Control, config.State);
                }
                else
                {
                    _logger.LogWarning("Ignoring invalid GPO port number: {GpoNumber}. Only ports {MinPort}-{MaxPort} are supported.",
                        config.Gpo, MIN_GPO_PORT, MAX_GPO_PORTS);
                }
            }

            return validConfigurations;
        }

        /// <summary>
        /// Ensures all 3 GPO ports have configurations, creating defaults for missing ones
        /// </summary>
        private async Task EnsureAllPortsConfigured(ExtendedGpoConfigurationRequest configuration)
        {
            var existingPorts = configuration.GpoConfigurations.Select(g => g.Gpo).ToHashSet();

            for (int port = MIN_GPO_PORT; port <= MAX_GPO_PORTS; port++)
            {
                if (!existingPorts.Contains(port))
                {
                    _logger.LogDebug("Adding default configuration for missing GPO port {Port}", port);

                    var defaultConfig = new ExtendedGpoConfiguration
                    {
                        Gpo = port,
                        Control = GpoControlMode.Static,
                        State = GpoState.Low
                    };

                    configuration.GpoConfigurations.Add(defaultConfig);
                }
            }

            // Sort by GPO number for consistent ordering
            configuration.GpoConfigurations = configuration.GpoConfigurations
                .OrderBy(g => g.Gpo)
                .ToList();
        }

        #endregion
    }

    #region Request/Response Models

    /// <summary>
    /// Request model for setting GPO state
    /// </summary>
    public class GpoStateRequest
    {
        [Required]
        [RegularExpression("^(High|Low)$", ErrorMessage = "State must be 'High' or 'Low'")]
        public string State { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request model for pulsing GPO
    /// </summary>
    public class GpoPulseRequest
    {
        [Range(100, 60000, ErrorMessage = "Duration must be between 100 and 60000 milliseconds")]
        public int DurationMs { get; set; } = 1000;
    }

    /// <summary>
    /// Success response model
    /// </summary>
    public class SuccessResponse
    {
        public string Message { get; set; } = string.Empty;
        public object? Data { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Error response model
    /// </summary>
    public class ErrorResponse
    {
        public string Message { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Validation error response model
    /// </summary>
    public class ValidationErrorResponse
    {
        public string Message { get; set; } = string.Empty;
        public string[] Errors { get; set; } = Array.Empty<string>();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// GPO status response model
    /// </summary>
    public class GpoStatusResponse
    {
        public int MaxSupportedPorts { get; set; }
        public int ActivePorts { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<int, GpoPortStatus> Ports { get; set; } = new();
    }

    /// <summary>
    /// Individual GPO port status
    /// </summary>
    public class GpoPortStatus
    {
        public int Port { get; set; }
        public string ControlMode { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public int? PulseDurationMs { get; set; }
        public bool IsHealthy { get; set; }
    }

    /// <summary>
    /// Control modes response model
    /// </summary>
    public class ControlModesResponse
    {
        public List<ControlModeInfo> SupportedModes { get; set; } = new();
        public int MaxSupportedPorts { get; set; }
        public int[] SupportedPorts { get; set; } = Array.Empty<int>();
        public PulseDurationLimits PulseDurationLimits { get; set; } = new();
    }

    /// <summary>
    /// Control mode information
    /// </summary>
    public class ControlModeInfo
    {
        public string Mode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool RequiresState { get; set; }
        public bool RequiresPulseDuration { get; set; }
    }

    /// <summary>
    /// Pulse duration limits
    /// </summary>
    public class PulseDurationLimits
    {
        public int MinDurationMs { get; set; }
        public int MaxDurationMs { get; set; }
        public int DefaultDurationMs { get; set; }
    }

    #endregion
}