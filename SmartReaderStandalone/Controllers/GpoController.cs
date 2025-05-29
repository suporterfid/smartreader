using Microsoft.AspNetCore.Mvc;
using SmartReaderStandalone.IotDeviceInterface;
using SmartReaderStandalone.Services.SmartReaderStandalone.Services;

namespace SmartReaderStandalone.Controllers
{
    [ApiController]
    [Route("api/v1/device/gpos")]
    public class GpoController : ControllerBase
    {
        private readonly IGpoService _gpoService;
        private readonly ILogger<GpoController> _logger;

        public GpoController(IGpoService gpoService, ILogger<GpoController> logger)
        {
            _gpoService = gpoService;
            _logger = logger;
        }

        /// <summary>
        /// Gets the current GPO configuration
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<ExtendedGpoConfigurationRequest>> GetConfiguration()
        {
            try
            {
                var config = await _gpoService.GetCurrentConfigurationAsync();
                return Ok(config);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get GPO configuration");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Updates the GPO configuration
        /// </summary>
        [HttpPut]
        public async Task<ActionResult> UpdateConfiguration([FromBody] ExtendedGpoConfigurationRequest request)
        {
            try
            {
                // Validate the request
                var validationResult = request.Validate();
                if (!validationResult.IsValid)
                {
                    return BadRequest(new { errors = validationResult.Errors });
                }

                await _gpoService.UpdateConfigurationAsync(request);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update GPO configuration");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Sets a specific GPO state (simplified endpoint)
        /// </summary>
        [HttpPut("{gpo}/state")]
        public async Task<ActionResult> SetGpoState(int gpo, [FromBody] GpoStateRequest request)
        {
            try
            {
                if (gpo < 1 || gpo > 4)
                {
                    return BadRequest(new { error = "GPO must be between 1 and 4" });
                }

                await _gpoService.SetGpoStateAsync(gpo, request.State);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set GPO state");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Pulses a specific GPO (simplified endpoint)
        /// </summary>
        [HttpPost("{gpo}/pulse")]
        public async Task<ActionResult> PulseGpo(int gpo, [FromBody] GpoPulseRequest request)
        {
            try
            {
                if (gpo < 1 || gpo > 4)
                {
                    return BadRequest(new { error = "GPO must be between 1 and 4" });
                }

                await _gpoService.PulseGpoAsync(gpo, request.DurationMs);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to pulse GPO");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Resets GPOs to default configuration
        /// </summary>
        [HttpPost("reset")]
        public async Task<ActionResult> ResetToDefault()
        {
            try
            {
                await _gpoService.ResetToDefaultAsync();
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset GPOs");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    // Request DTOs
    public class GpoStateRequest
    {
        public GpoState State { get; set; }
    }

    public class GpoPulseRequest
    {
        public int DurationMs { get; set; } = 1000;
    }
}
