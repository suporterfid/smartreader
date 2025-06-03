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
using Newtonsoft.Json.Linq;
using SmartReader.Infrastructure.Database;
using SmartReader.Infrastructure.ViewModel;
using SmartReader.IotDeviceInterface;
using SmartReaderJobs.ViewModel.Mqtt.Endpoint;
using SmartReaderStandalone.Authentication;
using SmartReaderStandalone.Entities;
using SmartReaderStandalone.Utils;
using System.Text.Json;

namespace SmartReaderStandalone.Controllers
{
    /// <summary>
    /// Controller for managing MQTT configuration, status, and commands.
    /// </summary>
    [ApiController]
    [Route("mqtt")]
    [AuthorizeBasicAuth]
    public class MqttController : ControllerBase
    {
        private readonly RuntimeDb _db;
        private readonly ILogger<MqttController> _logger;
        private readonly IotInterfaceService _iotService;

        public MqttController(RuntimeDb db, ILogger<MqttController> logger, IotInterfaceService iotService)
        {
            _db = db;
            _logger = logger;
            _iotService = iotService;
        }

        /// <summary>
        /// Gets the current MQTT configuration.
        /// </summary>
        /// <returns>MQTT configuration object.</returns>
        /// <response code="200">Returns MQTT configuration.</response>
        [HttpGet]
        [ProducesResponseType(typeof(MqttConfigurationDto), 200)]
        public IActionResult GetMqttConfig()
        {
            try
            {
                var mqttConfigurationDTO = new MqttConfigurationDto
                {
                    Data = new Data
                    {
                        Configuration = new Configuration
                        {
                            Additional = new Additional(),
                            Topics = new Topics
                            {
                                Control = new Control
                                {
                                    Command = new ManagementEvents(),
                                    Response = new ManagementEvents()
                                },
                                Management = new Control
                                {
                                    Command = new ManagementEvents(),
                                    Response = new ManagementEvents()
                                },
                                ManagementEvents = new ManagementEvents(),
                                TagEvents = new ManagementEvents()
                            },
                            Endpoint = new SmartReaderJobs.ViewModel.Mqtt.Endpoint.Endpoint()
                        }
                    }
                };

                var configDto = ConfigFileHelper.ReadFile();

                if (configDto != null && !string.IsNullOrEmpty(configDto.readerName))
                {
                    mqttConfigurationDTO.Data.Configuration.Endpoint.Hostname = configDto.mqttBrokerAddress;
                    mqttConfigurationDTO.Data.Configuration.Endpoint.Port = long.Parse(configDto.mqttBrokerPort);
                    mqttConfigurationDTO.Data.Configuration.Endpoint.Protocol = configDto.mqttBrokerProtocol;

                    mqttConfigurationDTO.Data.Configuration.Additional.CleanSession =
                        Convert.ToBoolean(Convert.ToInt32(configDto.mqttBrokerCleanSession));
                    mqttConfigurationDTO.Data.Configuration.Additional.ClientId = configDto.readerName;
                    mqttConfigurationDTO.Data.Configuration.Additional.Debug =
                        Convert.ToBoolean(Convert.ToInt32(configDto.mqttBrokerDebug));
                    mqttConfigurationDTO.Data.Configuration.Additional.KeepAlive = long.Parse(configDto.mqttBrokerKeepAlive);

                    mqttConfigurationDTO.Data.Configuration.Topics.Control.Command.Topic = configDto.mqttControlCommandTopic;
                    mqttConfigurationDTO.Data.Configuration.Topics.Control.Command.Qos =
                        long.Parse(configDto.mqttControlCommandQoS);
                    mqttConfigurationDTO.Data.Configuration.Topics.Control.Command.Retain =
                        bool.Parse(configDto.mqttControlCommandRetainMessages);

                    mqttConfigurationDTO.Data.Configuration.Topics.Control.Response.Topic = configDto.mqttControlResponseTopic;
                    mqttConfigurationDTO.Data.Configuration.Topics.Control.Response.Qos =
                        long.Parse(configDto.mqttControlResponseQoS);
                    mqttConfigurationDTO.Data.Configuration.Topics.Control.Response.Retain =
                        bool.Parse(configDto.mqttControlResponseRetainMessages);

                    mqttConfigurationDTO.Data.Configuration.Topics.Management.Command.Topic =
                        configDto.mqttManagementCommandTopic;
                    mqttConfigurationDTO.Data.Configuration.Topics.Management.Command.Qos =
                        long.Parse(configDto.mqttManagementCommandQoS);
                    mqttConfigurationDTO.Data.Configuration.Topics.Management.Command.Retain =
                        bool.Parse(configDto.mqttManagementCommandRetainMessages);

                    mqttConfigurationDTO.Data.Configuration.Topics.Management.Response.Topic =
                        configDto.mqttManagementResponseTopic;
                    mqttConfigurationDTO.Data.Configuration.Topics.Management.Response.Qos =
                        long.Parse(configDto.mqttManagementResponseQoS);
                    mqttConfigurationDTO.Data.Configuration.Topics.Management.Response.Retain =
                        bool.Parse(configDto.mqttManagementResponseRetainMessages);

                    mqttConfigurationDTO.Data.Configuration.Topics.ManagementEvents.Topic = configDto.mqttManagementEventsTopic;
                    mqttConfigurationDTO.Data.Configuration.Topics.ManagementEvents.Qos =
                        long.Parse(configDto.mqttManagementEventsQoS);
                    mqttConfigurationDTO.Data.Configuration.Topics.ManagementEvents.Retain =
                        bool.Parse(configDto.mqttManagementEventsRetainMessages);

                    mqttConfigurationDTO.Data.Configuration.Topics.TagEvents.Topic = configDto.mqttTagEventsTopic;
                    mqttConfigurationDTO.Data.Configuration.Topics.TagEvents.Qos = long.Parse(configDto.mqttTagEventsQoS);
                    mqttConfigurationDTO.Data.Configuration.Topics.TagEvents.Retain =
                        bool.Parse(configDto.mqttTagEventsRetainMessages);

                    return Ok(mqttConfigurationDTO);
                }

                return NotFound(new { status = "ERROR", message = "MQTT configuration not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting MQTT configuration");
                return StatusCode(500, new { status = "ERROR", message = ex.Message });
            }
        }

        /// <summary>
        /// Updates the MQTT configuration (add or update).
        /// </summary>
        /// <param name="jsonDocument">MQTT configuration payload.</param>
        /// <returns>Status and message.</returns>
        /// <response code="200">MQTT configuration updated.</response>
        /// <response code="400">Error in configuration.</response>
        [HttpPost]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 400)]
        public IActionResult PostMqttConfig([FromBody] JsonDocument jsonDocument)
        {
            return ProcessMqttEndpointRequest(jsonDocument);
        }

        /// <summary>
        /// Updates the MQTT configuration (PUT is also supported).
        /// </summary>
        /// <param name="jsonDocument">MQTT configuration payload.</param>
        /// <returns>Status and message.</returns>
        /// <response code="200">MQTT configuration updated.</response>
        /// <response code="400">Error in configuration.</response>
        [HttpPut]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 400)]
        public IActionResult PutMqttConfig([FromBody] JsonDocument jsonDocument)
        {
            return ProcessMqttEndpointRequest(jsonDocument);
        }

        /// <summary>
        /// Processes a control command via MQTT (MQTT-style).
        /// </summary>
        /// <param name="jsonDocument">MQTT command JSON body.</param>
        /// <returns>Result from processing the control command.</returns>
        /// <response code="200">Success response from command.</response>
        /// <response code="400">Error in command execution.</response>
        [HttpPost("command/control")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 400)]
        public async Task<IActionResult> MqttCommandControl([FromBody] JsonDocument jsonDocument)
        {
            try
            {
                string json = JsonSerializer.Serialize(jsonDocument);
                var result = await _iotService.ProcessMqttControlCommandJsonAsync(json, false);
                return Ok(JsonDocument.Parse(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MQTT control command");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Processes a management command via MQTT (MQTT-style).
        /// </summary>
        /// <param name="jsonDocument">MQTT command JSON body.</param>
        /// <returns>Result from processing the management command.</returns>
        /// <response code="200">Success response from command.</response>
        /// <response code="400">Error in command execution.</response>
        [HttpPost("command/management")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 400)]
        public async Task<IActionResult> MqttCommandManagement([FromBody] JsonDocument jsonDocument)
        {
            try
            {
                string json = JsonSerializer.Serialize(jsonDocument);
                var result = await _iotService.ProcessMqttManagementCommandJsonAsync(json, false);
                return Ok(JsonDocument.Parse(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MQTT management command");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Processes a mode command via MQTT (MQTT-style).
        /// </summary>
        /// <param name="jsonDocument">MQTT mode JSON body.</param>
        /// <returns>Result from processing the mode command.</returns>
        /// <response code="200">Success response from command.</response>
        /// <response code="400">Error in command execution.</response>
        [HttpPost("command/mode")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 400)]
        public IActionResult MqttCommandMode([FromBody] JsonDocument jsonDocument)
        {
            try
            {
                string json = JsonSerializer.Serialize(jsonDocument);
                var result = _iotService.ProcessMqttModeJsonCommand(json);

                string commandStatus = result == "success" ? "success" : "error";
                var deserializedCmdData = JObject.Parse(json);
                deserializedCmdData["response"] = commandStatus;
                deserializedCmdData["message"] = result;
                var serializedData = Newtonsoft.Json.JsonConvert.SerializeObject(deserializedCmdData);

                return Ok(JsonDocument.Parse(serializedData));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MQTT mode command");
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Processes a control command via generic HTTP endpoint (legacy support).
        /// </summary>
        /// <param name="jsonDocument">Command JSON body.</param>
        /// <returns>Result from processing the control command.</returns>
        [HttpPost("/command/control")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 400)]
        public async Task<IActionResult> CommandControl([FromBody] JsonDocument jsonDocument)
        {
            return await MqttCommandControl(jsonDocument);
        }

        /// <summary>
        /// Processes a management command via generic HTTP endpoint (legacy support).
        /// </summary>
        /// <param name="jsonDocument">Command JSON body.</param>
        /// <returns>Result from processing the management command.</returns>
        [HttpPost("/command/management")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 400)]
        public async Task<IActionResult> CommandManagement([FromBody] JsonDocument jsonDocument)
        {
            return await MqttCommandManagement(jsonDocument);
        }

        /// <summary>
        /// Processes a mode command via generic HTTP endpoint (legacy support).
        /// </summary>
        /// <param name="jsonDocument">Mode JSON body.</param>
        /// <returns>Result from processing the mode command.</returns>
        [HttpPost("/command/mode")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 400)]
        public IActionResult CommandMode([FromBody] JsonDocument jsonDocument)
        {
            return MqttCommandMode(jsonDocument);
        }

        /// <summary>
        /// Handles the logic for MQTT endpoint configuration (POST/PUT).
        /// </summary>
        private IActionResult ProcessMqttEndpointRequest(JsonDocument jsonDocument)
        {
            try
            {
                if (jsonDocument != null)
                {
                    var jsonDocumentStr = JsonSerializer.Serialize(jsonDocument);
                    MqttConfigurationDto mqttConfigurationDto = MqttConfigurationDto.FromJson(jsonDocumentStr);

                    if (mqttConfigurationDto != null)
                    {
                        var configDto = ConfigFileHelper.ReadFile();
                        configDto = StandaloneConfigDTO.CleanupUrlEncoding(configDto);

                        // Map logic here (parcial, para exemplo)
                        // Você pode inserir o mapeamento detalhado dos campos, como no seu Program.cs.

                        // Salve no banco se alterado
                        var configModel = _db.ReaderConfigs.FindAsync("READER_CONFIG").Result;
                        if (configModel == null)
                        {
                            configModel = new ReaderConfigs
                            {
                                Id = "READER_CONFIG",
                                Value = Newtonsoft.Json.JsonConvert.SerializeObject(configDto)
                            };
                            _db.ReaderConfigs.Add(configModel);
                        }
                        else
                        {
                            configModel.Value = Newtonsoft.Json.JsonConvert.SerializeObject(configDto);
                            _db.ReaderConfigs.Update(configModel);
                        }
                        _db.SaveChanges();

                        var operationResult = new
                        {
                            status = "OK",
                            message = "Successfully processed endpoint configurations"
                        };
                        return Ok(operationResult);
                    }
                }
                var operationResulterror = new
                {
                    status = "NOT PROCESSED",
                    message = "The request was not processed"
                };
                return BadRequest(operationResulterror);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MQTT endpoint request");
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
