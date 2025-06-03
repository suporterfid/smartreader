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
using SmartReaderStandalone.Authentication;
using SmartReaderStandalone.Entities;
using SmartReaderStandalone.Services;
using SmartReaderStandalone.Utils;

namespace SmartReaderStandalone.Controllers
{
    /// <summary>
    /// Controller for SmartReader device configuration and runtime commands.
    /// </summary>
    [ApiController]
    [Route("api/settings")]
    [AuthorizeBasicAuth]
    public class SettingsController : ControllerBase
    {
        private readonly RuntimeDb _db;
        private readonly ILogger<SettingsController> _logger;
        private readonly ISmartReaderConfigurationService _configService;

        public SettingsController(RuntimeDb db, ILogger<SettingsController> logger, ISmartReaderConfigurationService configService)
        {
            _db = db;
            _logger = logger;
            _configService = configService;
        }

        /// <summary>
        /// Gets the current SmartReader configuration.
        /// </summary>
        /// <returns>List with current configuration DTO.</returns>
        /// <response code="200">Current settings returned.</response>
        /// <response code="404">No configuration found.</response>
        [HttpGet]
        [ProducesResponseType(typeof(List<StandaloneConfigDTO>), 200)]
        [ProducesResponseType(404)]
        public IActionResult GetSettings()
        {
            try
            {
                var dtos = new List<StandaloneConfigDTO>();
                var configDto = ConfigFileHelper.ReadFile();

                var configModel = _db.ReaderConfigs.FindAsync("READER_CONFIG").Result;
                if (configModel != null && !string.IsNullOrEmpty(configModel.Value))
                {
                    var storedSettingsDto = JsonConvert.DeserializeObject<StandaloneConfigDTO>(configModel.Value);
                    if (storedSettingsDto != null)
                    {
                        storedSettingsDto = StandaloneConfigDTO.CleanupUrlEncoding(storedSettingsDto);
                        dtos.Add(storedSettingsDto);
                        return Ok(dtos);
                    }
                }

                if (configDto != null && !string.IsNullOrEmpty(configDto.readerName))
                {
                    dtos.Add(configDto);
                    return Ok(dtos);
                }

                return NotFound();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve settings.");
                return NotFound();
            }
        }

        /// <summary>
        /// Updates the SmartReader configuration.
        /// </summary>
        /// <param name="config">Configuration object to save.</param>
        /// <returns>Success status.</returns>
        /// <response code="200">Settings saved successfully.</response>
        /// <response code="400">Invalid settings.</response>
        [HttpPost]
        [ProducesResponseType(typeof(SimpleResponse), 200)]
        [ProducesResponseType(typeof(SimpleResponse), 400)]
        public IActionResult SaveSettings([FromBody] StandaloneConfigDTO config)
        {
            try
            {
                if (!string.IsNullOrEmpty(config.licenseKey))
                {
                    var configLicenseModel = _db.ReaderConfigs.FindAsync("READER_LICENSE").Result;
                    if (configLicenseModel == null)
                    {
                        configLicenseModel = new ReaderConfigs
                        {
                            Id = "READER_LICENSE",
                            Value = config.licenseKey
                        };
                        _db.ReaderConfigs.Add(configLicenseModel);
                    }
                    else
                    {
                        configLicenseModel.Value = config.licenseKey;
                        _db.ReaderConfigs.Update(configLicenseModel);
                    }

                    _db.SaveChanges();
                }

                // GPO/tag presence consistency (as no seu código original)
                if (config != null)
                {
                    if ("1".Equals(config.advancedGpoEnabled))
                    {
                        if ("6".Equals(config.advancedGpoMode1) || "6".Equals(config.advancedGpoMode2) || "6".Equals(config.advancedGpoMode3))
                        {
                            config.softwareFilterEnabled = "1";
                            if ("0".Equals(config.softwareFilterField))
                            {
                                config.softwareFilterField = "1";
                            }
                        }
                    }

                    if ("1".Equals(config.tagPresenceTimeoutEnabled))
                    {
                        config.softwareFilterField = config.tagPresenceTimeoutInSec;
                    }
                }

                var configModel = _db.ReaderConfigs.FindAsync("READER_CONFIG").Result;
                if (configModel == null)
                {
                    configModel = new ReaderConfigs
                    {
                        Id = "READER_CONFIG",
                        Value = JsonConvert.SerializeObject(config)
                    };
                    _db.ReaderConfigs.Add(configModel);
                }
                else
                {
                    configModel.Value = JsonConvert.SerializeObject(config);
                    _db.ReaderConfigs.Update(configModel);
                }

                _db.SaveChanges();

                return Ok(new SimpleResponse { Message = "Settings saved successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings.");
                return BadRequest(new SimpleResponse { Message = "Error saving settings." });
            }
        }

        /// <summary>
        /// Restores default settings and (optionally) resets authentication to default.
        /// </summary>
        /// <returns>Restored configuration DTO.</returns>
        /// <response code="200">Default settings restored.</response>
        /// <response code="404">No configuration restored.</response>
        [HttpGet("restore-default-settings")]
        [ProducesResponseType(typeof(List<StandaloneConfigDTO>), 200)]
        [ProducesResponseType(404)]
        public IActionResult RestoreDefaultSettings()
        {
            try
            {
                var configDto = ConfigFileHelper.GetSmartreaderDefaultConfigDTO();
                if (configDto != null)
                {
                    // Optionally reset authentication credentials
                    try
                    {
                        string json;
                        using (var reader = System.IO.File.OpenText("customsettings.json"))
                        {
                            json = reader.ReadToEnd();
                        }
                        var data = System.Text.Json.JsonSerializer.Deserialize<CustomAuth>(json);
                        data.BasicAuth.UserName = "admin";
                        data.BasicAuth.Password = "admin";
                        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                        var updatedJson = System.Text.Json.JsonSerializer.Serialize(data, options);
                        using (var writer = System.IO.File.CreateText("customsettings.json"))
                        {
                            writer.Write(updatedJson);
                        }
                    }
                    catch (Exception) { }

                    _configService.SaveConfigDtoToDb(configDto);
                    return Ok(new List<StandaloneConfigDTO> { configDto });
                }
                return NotFound();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore default settings.");
                return NotFound();
            }
        }

        /// <summary>
        /// Starts the reader with the preset configuration.
        /// </summary>
        /// <returns>Status message.</returns>
        /// <response code="200">Command queued.</response>
        [HttpGet("start-preset")]
        [ProducesResponseType(typeof(SimpleResponse), 200)]
        public IActionResult StartPreset()
        {
            return AddCommand("START_PRESET", "START");
        }

        /// <summary>
        /// Starts the inventory process.
        /// </summary>
        /// <returns>Status message.</returns>
        /// <response code="200">Command queued.</response>
        [HttpGet("start-inventory")]
        [ProducesResponseType(typeof(SimpleResponse), 200)]
        public IActionResult StartInventory()
        {
            return AddCommand("START_INVENTORY", "START");
        }

        /// <summary>
        /// Stops the reader preset operation.
        /// </summary>
        /// <returns>Status message.</returns>
        /// <response code="200">Command queued.</response>
        [HttpGet("stop-preset")]
        [ProducesResponseType(typeof(SimpleResponse), 200)]
        public IActionResult StopPreset()
        {
            return AddCommand("STOP_PRESET", "STOP");
        }

        /// <summary>
        /// Stops the inventory process.
        /// </summary>
        /// <returns>Status message.</returns>
        /// <response code="200">Command queued.</response>
        [HttpGet("stop-inventory")]
        [ProducesResponseType(typeof(SimpleResponse), 200)]
        public IActionResult StopInventory()
        {
            return AddCommand("STOP_INVENTORY", "STOP");
        }

        /// <summary>
        /// Triggers a firmware upgrade command if a valid URL is configured.
        /// </summary>
        /// <returns>Status message.</returns>
        /// <response code="200">Command queued.</response>
        [HttpGet("upgrade-firmware")]
        [ProducesResponseType(typeof(SimpleResponse), 200)]
        public IActionResult UpgradeFirmware()
        {
            try
            {
                var configDto = ConfigFileHelper.ReadFile();
                if (configDto != null && !string.IsNullOrEmpty(configDto.systemImageUpgradeUrl))
                {
                    var command = new ReaderCommands
                    {
                        Id = "UPGRADE_SYSTEM_IMAGE",
                        Value = configDto.systemImageUpgradeUrl,
                        Timestamp = DateTime.Now
                    };
                    _db.ReaderCommands.Add(command);
                    _db.SaveChanges();
                    return Ok(new SimpleResponse { Message = "Upgrade firmware command queued." });
                }
                return Ok(new SimpleResponse { Message = "Upgrade firmware URL not configured." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to queue upgrade firmware command.");
                return Ok(new SimpleResponse { Message = "Error queueing upgrade command." });
            }
        }

        /// <summary>
        /// Helper method to add a command to the ReaderCommands table.
        /// </summary>
        private IActionResult AddCommand(string commandId, string value)
        {
            try
            {
                var command = new ReaderCommands
                {
                    Id = commandId,
                    Value = value,
                    Timestamp = DateTime.Now
                };
                _db.ReaderCommands.Add(command);
                _db.SaveChanges();
                return Ok(new SimpleResponse { Message = $"Command {commandId} queued." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to add command: {commandId}");
                return Ok(new SimpleResponse { Message = $"Error queueing {commandId}." });
            }
        }
    }

    /// <summary>
    /// Auth configuration for restore-default-settings helper.
    /// </summary>
    public class CustomAuth
    {
        public BasicAuthConfig BasicAuth { get; set; }
    }
    public class BasicAuthConfig
    {
        public string UserName { get; set; }
        public string Password { get; set; }
    }
}
