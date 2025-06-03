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
using Newtonsoft.Json;
using SmartReader.Infrastructure.Database;
using SmartReaderStandalone.Authentication;

namespace SmartReaderStandalone.Controllers
{
    /// <summary>
    /// Controller for verifying license keys for the reader.
    /// </summary>
    [ApiController]
    [Route("api")]
    [AuthorizeBasicAuth]
    public class LicenseController : ControllerBase
    {
        private readonly RuntimeDb _db;
        private readonly ILogger<LicenseController> _logger;

        public LicenseController(RuntimeDb db, ILogger<LicenseController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Verifies if the provided license key is valid for the current reader.
        /// </summary>
        /// <param name="key">The license key to validate.</param>
        /// <returns>License validation status.</returns>
        /// <response code="200">Validation result: "pass" or "fail".</response>
        [HttpGet("verify_key/{key}")]
        [ProducesResponseType(typeof(List<ReaderLicenseDto>), 200)]
        public IActionResult VerifyKey([FromRoute] string key)
        {
            var licenses = new List<ReaderLicenseDto>
            {
                new ReaderLicenseDto { IsValid = "fail" }
            };

            try
            {
                var serial = _db.ReaderStatus.FindAsync("READER_SERIAL");
                if (serial != null && serial.Result != null && !string.IsNullOrEmpty(serial.Result.Value))
                {
                    var json = JsonConvert.DeserializeObject<List<SmartreaderSerialNumberDto>>(serial.Result.Value);
                    if (json != null && json.FirstOrDefault() != null)
                    {
                        var expectedLicense = SmartReaderJobs.Utils.Utils.CreateMD5Hash("sM@RTrEADER2022-" + json.FirstOrDefault().SerialNumber);
                        if (string.Equals(key, expectedLicense, StringComparison.OrdinalIgnoreCase))
                        {
                            licenses[0].IsValid = "pass";
                        }
                    }
                }
                return Ok(licenses);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying license key.");
                return Ok(licenses);
            }
        }
    }

    /// <summary>
    /// DTO for license verification result.
    /// </summary>
    public class ReaderLicenseDto
    {
        /// <example>pass</example>
        public string IsValid { get; set; }
    }

    /// <summary>
    /// DTO for serial number.
    /// </summary>
    public class SmartreaderSerialNumberDto
    {
        public string SerialNumber { get; set; }
    }
}
