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
    /// Controller for uploading CA and client certificates for MQTT communication.
    /// </summary>
    [ApiController]
    [Route("upload/mqtt")]
    [AuthorizeBasicAuth]
    public class UploadController : ControllerBase
    {
        private readonly ISmartReaderConfigurationService _configurationService;
        private readonly ILogger<UploadController> _logger;

        public UploadController(ISmartReaderConfigurationService configurationService, ILogger<UploadController> logger)
        {
            _configurationService = configurationService;
            _logger = logger;
        }

        /// <summary>
        /// Uploads a CA certificate for MQTT.
        /// </summary>
        /// <remarks>
        /// The file must be sent as multipart/form-data.
        /// </remarks>
        /// <param name="file">CA certificate file (any format: crt, key, pem).</param>
        /// <returns>Upload status message.</returns>
        /// <response code="200">File uploaded successfully.</response>
        /// <response code="400">No file sent or error saving file.</response>
        [HttpPost("ca")]
        [ProducesResponseType(typeof(UploadResponse), 200)]
        [ProducesResponseType(typeof(UploadResponse), 400)]
        public async Task<IActionResult> UploadCaCertificate([FromForm] IFormFile file)
        {
            if (file == null)
            {
                return BadRequest(new UploadResponse { Message = "At least one file is required." });
            }

            var targetDir = @"/customer/config/ca/";
            if (!Directory.Exists(targetDir))
            {
                try
                {
                    Directory.CreateDirectory(targetDir);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating CA directory.");
                    return StatusCode(500, new UploadResponse { Message = "Error creating CA directory." });
                }
            }

            var filePath = Path.Combine(targetDir, file.FileName);
            try
            {
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var configDto = await _configurationService.GetConfigDtoFromDb();
                if (configDto != null)
                {
                    configDto.mqttSslCaCertificate = filePath;
                    _configurationService.SaveConfigDtoToDb(configDto);
                }

                return Ok(new UploadResponse { Message = "CA file uploaded successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving CA file.");
                return StatusCode(500, new UploadResponse { Message = "Error saving CA file." });
            }
        }

        /// <summary>
        /// Uploads a client certificate (PFX) for MQTT.
        /// </summary>
        /// <remarks>
        /// The file must be sent as multipart/form-data.  
        /// The certificate password must be included in the form field "password".
        /// </remarks>
        /// <param name="file">Client certificate file (PFX).</param>
        /// <param name="password">Certificate password.</param>
        /// <returns>Upload status message.</returns>
        /// <response code="200">Certificate and password uploaded successfully.</response>
        /// <response code="400">No file sent or error saving file.</response>
        [HttpPost("certificate")]
        [ProducesResponseType(typeof(UploadResponse), 200)]
        [ProducesResponseType(typeof(UploadResponse), 400)]
        public async Task<IActionResult> UploadClientCertificate([FromForm] IFormFile file, [FromForm] string password)
        {
            if (file == null)
            {
                return BadRequest(new UploadResponse { Message = "At least one file is required." });
            }

            var targetDir = @"/customer/config/certificate/";
            if (!Directory.Exists(targetDir))
            {
                try
                {
                    Directory.CreateDirectory(targetDir);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating certificate directory.");
                    return StatusCode(500, new UploadResponse { Message = "Error creating certificate directory." });
                }
            }

            var filePath = Path.Combine(targetDir, file.FileName);
            try
            {
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var configDto = await _configurationService.GetConfigDtoFromDb();
                if (configDto != null)
                {
                    configDto.mqttSslClientCertificate = filePath;
                    configDto.mqttSslClientCertificatePassword = password ?? string.Empty;
                    _configurationService.SaveConfigDtoToDb(configDto);
                }

                _logger.LogInformation("Certificate and password saved successfully.");
                return Ok(new UploadResponse { Message = "Certificate and password uploaded successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving certificate file.");
                return StatusCode(500, new UploadResponse { Message = "Error saving certificate file." });
            }
        }
    }

    /// <summary>
    /// Simple upload response message.
    /// </summary>
    public class UploadResponse
    {
        /// <example>CA file uploaded successfully.</example>
        public string Message { get; set; }
    }
}
