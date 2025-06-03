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
using Microsoft.AspNetCore.Mvc;
using SmartReaderStandalone.Authentication;
using SmartReaderStandalone.Services;

namespace SmartReaderStandalone.Controllers
{
    /// <summary>
    /// Controller for querying and updating the application's logging level.
    /// </summary>
    [ApiController]
    [Route("api/logging")]
    [AuthorizeBasicAuth]
    public class LoggingController : ControllerBase
    {
        private readonly LoggingService _loggingService;

        public LoggingController(LoggingService loggingService)
        {
            _loggingService = loggingService;
        }

        /// <summary>
        /// Gets the current logging level.
        /// </summary>
        /// <returns>Current log level and status.</returns>
        /// <response code="200">Returns the current log level.</response>
        [HttpGet("level")]
        [ProducesResponseType(typeof(LogLevelResponse), 200)]
        public IActionResult GetLogLevel()
        {
            return Ok(new LogLevelResponse
            {
                Success = true,
                CurrentLevel = _loggingService.GetCurrentLogLevel().ToString(),
                Message = "Current log level retrieved successfully"
            });
        }

        /// <summary>
        /// Changes the logging level.
        /// </summary>
        /// <param name="request">Logging level change request.</param>
        /// <returns>Status and the new log level.</returns>
        /// <response code="200">Log level successfully changed.</response>
        /// <response code="400">Invalid or unsupported log level.</response>
        [HttpPost("level")]
        [ProducesResponseType(typeof(LogLevelResponse), 200)]
        [ProducesResponseType(typeof(LogLevelResponse), 400)]
        public IActionResult SetLogLevel([FromBody] LogLevelRequest request)
        {
            if (string.IsNullOrEmpty(request.Level))
            {
                return BadRequest(new LogLevelResponse
                {
                    Success = false,
                    Message = "Log level cannot be empty",
                    CurrentLevel = _loggingService.GetCurrentLogLevel().ToString()
                });
            }

            var success = _loggingService.SetLogLevel(request.Level);
            return success
                ? Ok(new LogLevelResponse
                {
                    Success = true,
                    Message = $"Log level changed to {request.Level}",
                    CurrentLevel = _loggingService.GetCurrentLogLevel().ToString()
                })
                : BadRequest(new LogLevelResponse
                {
                    Success = false,
                    Message = $"Failed to change log level to {request.Level}",
                    CurrentLevel = _loggingService.GetCurrentLogLevel().ToString()
                });
        }
    }
}
