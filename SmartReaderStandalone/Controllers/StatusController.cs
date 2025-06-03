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
using SmartReader.Infrastructure.ViewModel;
using SmartReader.IotDeviceInterface;
using SmartReaderStandalone.Authentication;
using SmartReaderStandalone.IotDeviceInterface;
using SmartReaderStandalone.Utils;
using SmartReaderStandalone.ViewModel;
using SmartReaderStandalone.ViewModel.Status;

namespace SmartReaderStandalone.Controllers
{
    /// <summary>
    /// Controller for querying device status, capabilities, serial, and image.
    /// </summary>
    [ApiController]
    [Route("api")]
    [AuthorizeBasicAuth]
    public class StatusController : ControllerBase
    {
        private readonly RuntimeDb _db;
        private readonly ILogger<StatusController> _logger;
        private readonly IR700IotReader _iotReader;
        private readonly ReaderConfiguration _configuration;

        public StatusController(
            RuntimeDb db,
            ILogger<StatusController> logger,
            IR700IotReader iotReader,
            ReaderConfiguration configuration)
        {
            _db = db;
            _logger = logger;
            _iotReader = iotReader;
            _configuration = configuration;
        }

        /// <summary>
        /// Gets the reader capabilities (antenna, region, etc.).
        /// </summary>
        /// <remarks>Returns model, antennas, and region capabilities.</remarks>
        /// <returns>List of SmartReaderCapabilities</returns>
        /// <response code="200">Success with capabilities info.</response>
        [HttpGet("getcapabilities")]
        [ProducesResponseType(typeof(List<SmartReaderCapabilities>), 200)]
        public async Task<IActionResult> GetCapabilities()
        {
            var capabilities = new List<SmartReaderCapabilities>();
            try
            {
                _logger.LogInformation("Retrieving reader capabilities...");

                // 1. System info
                var systemInfo = await _iotReader.GetSystemInfoAsync();
                var systemRegion = await _iotReader.GetSystemRegionInfoAsync();
                var systemPower = await _iotReader.GetSystemPowerAsync();

                bool isPoePlus = systemPower.PowerSource.Equals(Impinj.Atlas.PowerSource.Poeplus);

                // 2. Capabilities object
                var capability = new SmartReaderCapabilities
                {
                    RxTable = SmartReaderJobs.Utils.Utils.GetDefaultRxTable(),
                    TxTable = SmartReaderJobs.Utils.Utils.GetDefaultTxTable(systemInfo.ProductModel, isPoePlus, systemRegion.OperatingRegion),
                    RfModeTable = SmartReaderJobs.Utils.Utils.GetDefaultRfModeTable(),
                    MaxAntennas = 4,
                    SearchModeTable = SmartReaderJobs.Utils.Utils.GetDefaultSearchModeTable(),
                    LicenseValid = 1,
                    ValidAntennas = "1,2,3,4",
                    ModelName = "R700"
                };

                // 3. Load from stored config if available
                try
                {
                    var configModel = await _db.ReaderConfigs.FindAsync("READER_CONFIG");
                    if (configModel != null && !string.IsNullOrEmpty(configModel.Value))
                    {
                        var storedSettingsDto = JsonConvert.DeserializeObject<StandaloneConfigDTO>(configModel.Value);
                        if (storedSettingsDto != null)
                        {
                            storedSettingsDto = StandaloneConfigDTO.CleanupUrlEncoding(storedSettingsDto);
                            var currentAntennas = storedSettingsDto.antennaPorts?.Split(",") ?? Array.Empty<string>();
                            capability.MaxAntennas = currentAntennas.Length;
                            capability.ValidAntennas = storedSettingsDto.antennaPorts;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading stored configuration");
                }

                capabilities.Add(capability);
                return Ok(capabilities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving capabilities");
                return StatusCode(500, new { message = ex.Message });
            }
        }

        /// <summary>
        /// Gets RFID reader status information.
        /// </summary>
        /// <remarks>Returns status key-value pairs from RShell command.</remarks>
        /// <returns>RFID status (dictionary)</returns>
        /// <response code="200">Success with RFID status.</response>
        [HttpGet("getrfidstatus")]
        [ProducesResponseType(typeof(List<Dictionary<string, string>>), 200)]
        public IActionResult GetRfidStatus()
        {
            var rfidStatus = new List<Dictionary<string, string>>();
            var statusEvent = new Dictionary<string, string>();
            try
            {
                var rshell = new RShellUtil(_configuration.Network.Hostname, _configuration.Security.Username, _configuration.Security.Password);
                var resultRfidStat = rshell.SendCommand("show rfid stat");
                rshell.Disconnect();

                var lines = resultRfidStat.Split("\n");
                foreach (var line in lines)
                {
                    if (line.StartsWith("status", StringComparison.OrdinalIgnoreCase)) continue;
                    var values = line.Split('=');
                    if (values.Length == 2)
                    {
                        statusEvent[values[0].Trim()] = values[1].Replace("'", string.Empty).Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting RFID status");
            }
            rfidStatus.Add(statusEvent);
            return Ok(rfidStatus);
        }

        /// <summary>
        /// Gets the current reader image status.
        /// </summary>
        /// <remarks>Returns information from RShell about the image summary.</remarks>
        /// <returns>Dictionary of image status.</returns>
        /// <response code="200">Success with image status.</response>
        [HttpGet("image")]
        [ProducesResponseType(typeof(Dictionary<object, object>), 200)]
        public IActionResult GetImage()
        {
            var imageStatus = new Dictionary<object, object>();
            try
            {
                var rshell = new RShellUtil(_configuration.Network.Hostname, _configuration.Security.Username, _configuration.Security.Password);
                var resultImageStatus = rshell.SendCommand("show image summary");
                var lines = resultImageStatus.Split('\n');
                foreach (var line in lines)
                {
                    if (line.ToUpper().Contains("STATUS")) continue;
                    var lineData = line.Split('=');
                    if (lineData.Length > 1)
                    {
                        imageStatus[lineData[0].Trim()] = lineData[1].Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading image status");
            }
            return Ok(imageStatus);
        }

        /// <summary>
        /// Gets the running status of the reader (from database).
        /// </summary>
        /// <remarks>Returns stored running status records.</remarks>
        /// <returns>List of running status.</returns>
        /// <response code="200">Success with running status.</response>
        [HttpGet("getstatus")]
        [ProducesResponseType(typeof(List<SmartreaderRunningStatusDto>), 200)]
        public IActionResult GetStatus()
        {
            var status = _db.ReaderStatus.FindAsync("READER_STATUS");
            if (status != null && status.Result != null)
            {
                var json = JsonConvert.DeserializeObject<List<SmartreaderRunningStatusDto>>(status.Result.Value);
                return Ok(json);
            }
            return NotFound();
        }

        /// <summary>
        /// Gets the reader serial number(s).
        /// </summary>
        /// <remarks>Returns serial number(s) from the database.</remarks>
        /// <returns>List of serial numbers.</returns>
        /// <response code="200">Success with serial number(s).</response>
        [HttpGet("getserial")]
        [ProducesResponseType(typeof(List<SmartreaderSerialNumberDto>), 200)]
        public IActionResult GetSerial()
        {
            var serial = _db.ReaderStatus.FindAsync("READER_SERIAL");
            if (serial != null && serial.Result != null && serial.Result.Value != null)
            {
                var json = JsonConvert.DeserializeObject<List<SmartreaderSerialNumberDto>>(serial.Result.Value);
                return Ok(json);
            }
            return NotFound();
        }

        /// <summary>
        /// Gets the device (reader) ID.
        /// </summary>
        /// <remarks>Returns readerName from current configuration file.</remarks>
        /// <returns>Reader name object.</returns>
        /// <response code="200">Success with reader name.</response>
        [HttpGet("deviceid")]
        [ProducesResponseType(typeof(Dictionary<string, string>), 200)]
        public IActionResult GetDeviceId()
        {
            var configDto = ConfigFileHelper.ReadFile();
            if (configDto != null && !string.IsNullOrEmpty(configDto.readerName))
            {
                var json = new Dictionary<string, string>
                {
                    { "readerName", configDto.readerName }
                };
                return Ok(json);
            }
            return NotFound();
        }
    }


}
