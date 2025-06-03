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
using SmartReader.Infrastructure.Database;
using SmartReaderStandalone.Authentication;
using SmartReaderStandalone.Entities;
using SmartReaderStandalone.Services;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartReaderStandalone.Controllers
{
    /// <summary>
    /// Controller for system maintenance, utilities, and diagnostic endpoints.
    /// </summary>
    [ApiController]
    [Route("")]
    [AuthorizeBasicAuth]
    public class MaintenanceController : ControllerBase
    {
        private readonly RuntimeDb _db;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MaintenanceController> _logger;

        public MaintenanceController(RuntimeDb db, IServiceProvider serviceProvider, ILogger<MaintenanceController> logger)
        {
            _db = db;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// Cleans software filter history for EPCs.
        /// </summary>
        /// <returns>Result message.</returns>
        /// <response code="200">Success.</response>
        /// <response code="500">Error.</response>
        [HttpGet("api/filter/clean")]
        [ProducesResponseType(typeof(SimpleResponse), 200)]
        [ProducesResponseType(typeof(SimpleResponse), 500)]
        public IActionResult CleanFilter()
        {
            try
            {
                var command = new ReaderCommands
                {
                    Id = "CLEAN_EPC_SOFTWARE_HISTORY_FILTERS",
                    Value = "ALL",
                    Timestamp = DateTime.Now
                };
                _db.ReaderCommands.Add(command);
                _db.SaveChanges();
                return Ok(new SimpleResponse { Message = "Software filter history cleaned." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean filter.");
                return StatusCode(500, new SimpleResponse { Message = "Error cleaning filter." });
            }
        }

        /// <summary>
        /// Reloads inventory by issuing a start command and restarts the application.
        /// </summary>
        /// <returns>Result message.</returns>
        [HttpGet("api/reload")]
        [ProducesResponseType(typeof(SimpleResponse), 200)]
        public async Task<IActionResult> Reload()
        {
            try
            {
                var command = new ReaderCommands
                {
                    Id = "START_INVENTORY",
                    Value = "START",
                    Timestamp = DateTime.Now
                };
                _db.ReaderCommands.Add(command);
                await _db.SaveChangesAsync();
                await Task.Delay(100);

                _logger.LogInformation("Restarting process...");
                Environment.Exit(0); // Will trigger graceful shutdown if running as a service

                return Ok(new SimpleResponse { Message = "Application is restarting." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during reload.");
                return Ok(new SimpleResponse { Message = "Error during reload." });
            }
        }

        /// <summary>
        /// Restores configuration from backup and restarts the application.
        /// </summary>
        /// <returns>Result message.</returns>
        [HttpGet("api/restore")]
        [ProducesResponseType(typeof(SimpleResponse), 200)]
        public async Task<IActionResult> Restore()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/cp",
                    Arguments = "/customer/config/smartreader_backup.json /customer/config/smartreader.json"
                };
                await Task.Delay(TimeSpan.FromSeconds(2));
                _logger.LogInformation("Restoring from backup and restarting...");
                Environment.Exit(1);
                return Ok(new SimpleResponse { Message = "Restore initiated." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during restore.");
                return Ok(new SimpleResponse { Message = "Error during restore." });
            }
        }

        /// <summary>
        /// Cleans USB files (all or by filename).
        /// </summary>
        /// <param name="filename">Optional filename to delete. If omitted, all files are deleted.</param>
        /// <returns>Result message.</returns>
        /// <response code="200">Success.</response>
        /// <response code="404">Directory or file not found.</response>
        /// <response code="500">Error.</response>
        [HttpDelete("cleanup-usb-files")]
        [ProducesResponseType(typeof(SimpleResponse), 200)]
        [ProducesResponseType(typeof(SimpleResponse), 404)]
        [ProducesResponseType(typeof(SimpleResponse), 500)]
        public IActionResult CleanupUsbFiles([FromQuery] string filename)
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "customer", "wwwroot", "files");

            if (!Directory.Exists(path))
                return NotFound(new SimpleResponse { Message = "Directory not found" });

            try
            {
                if (string.IsNullOrEmpty(filename))
                {
                    DirectoryInfo di = new(path);
                    foreach (FileInfo file in di.GetFiles()) file.Delete();
                    foreach (DirectoryInfo dir in di.GetDirectories()) dir.Delete(true);
                    return Ok(new SimpleResponse { Message = "All files cleaned successfully" });
                }
                else
                {
                    string filePath = Path.Combine(path, filename);
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                        return Ok(new SimpleResponse { Message = $"File '{filename}' deleted successfully" });
                    }
                    else
                    {
                        return NotFound(new SimpleResponse { Message = $"File '{filename}' not found" });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new SimpleResponse { Message = $"An error occurred: {ex.Message}" });
            }
        }

        /// <summary>
        /// Tests HTTP authentication with a bearer token (proxy to external API).
        /// </summary>
        /// <param name="request">Bearer token API request body.</param>
        /// <returns>The token returned from the external API.</returns>
        /// <response code="200">Returns the token.</response>
        /// <response code="400">Invalid or error in processing.</response>
        [HttpPost("api/test")]
        [ProducesResponseType(typeof(string), 200)]
        [ProducesResponseType(typeof(string), 400)]
        public async Task<IActionResult> TestBearerApi([FromBody] BearerDTO request)
        {
            string token = "";
            try
            {
                if (!string.IsNullOrEmpty(request.httpAuthenticationTokenApiUrl))
                {
                    var url = request.httpAuthenticationTokenApiUrl;
                    var httpClientHandler = new System.Net.Http.HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                    };
                    var httpClient = new System.Net.Http.HttpClient(httpClientHandler)
                    {
                        BaseAddress = new Uri(url)
                    };

                    var httpRequest = new System.Net.Http.HttpRequestMessage
                    {
                        Method = System.Net.Http.HttpMethod.Post,
                        RequestUri = new Uri(url),
                        Content = new System.Net.Http.StringContent(request.httpAuthenticationTokenApiBody, System.Text.Encoding.UTF8, "application/json")
                    };
                    httpRequest.Headers.Add("Accept", "application/json");

                    var response = await httpClient.SendAsync(httpRequest).ConfigureAwait(false);
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(content))
                    {
                        var tokenJson = System.Text.Json.JsonDocument.Parse(content);
                        if (tokenJson.RootElement.TryGetProperty("token", out var tokenProperty))
                        {
                            token = tokenProperty.GetString();
                        }
                    }
                }
                return Ok(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in test API.");
                return BadRequest(token);
            }
        }

        /// <summary>
        /// Returns metrics in Prometheus format.
        /// </summary>
        /// <returns>Metrics text output.</returns>
        /// <response code="200">Prometheus-compatible metrics text.</response>
        [HttpGet("metrics")]
        [ProducesResponseType(typeof(string), 200)]
        public async Task<IActionResult> GetMetrics()
        {
            var metricsOutput = new List<string>();
            try
            {
                // Retrieve all services that implement IMetricProvider
                var metricProviders = _serviceProvider.GetServices(typeof(IMetricProvider)).Cast<IMetricProvider>().ToList();

                // Add specific services if registered (optional)
                var metricsMonitoringService = _serviceProvider.GetService(typeof(MetricsMonitoringService)) as MetricsMonitoringService;
                if (metricsMonitoringService != null) metricProviders.Add(metricsMonitoringService);

                var tcpSocketService = _serviceProvider.GetService(typeof(ITcpSocketService)) as IMetricProvider;
                if (tcpSocketService != null) metricProviders.Add(tcpSocketService);

                var mqttService = _serviceProvider.GetService(typeof(IMqttService)) as IMetricProvider;
                if (mqttService != null) metricProviders.Add(mqttService);

                // Collect metrics from all providers
                foreach (var provider in metricProviders)
                {
                    var metrics = await provider.GetMetricsAsync();

                    foreach (var metric in metrics)
                    {
                        // Format metric name: lowercase, no spaces, no special chars
                        string metricName = $"{provider.GetType().Name}_{metric.Key}".ToLower();
                        metricName = Regex.Replace(metricName, @"[^a-z0-9_]", "_");

                        string metricValue = metric.Value is bool boolValue
                            ? (boolValue ? "1" : "0")
                            : Convert.ToString(metric.Value, System.Globalization.CultureInfo.InvariantCulture);

                        metricsOutput.Add($"# HELP {metricName} Auto-generated metric");
                        metricsOutput.Add($"# TYPE {metricName} gauge");
                        metricsOutput.Add($"{metricName} {metricValue}");
                    }
                }

                return Content(string.Join("\n", metricsOutput), "text/plain");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting metrics.");
                return Content("# Metrics unavailable", "text/plain");
            }
        }

        /// <summary>
        /// Returns all registered API routes (debug only).
        /// </summary>
        /// <returns>List of endpoint route info.</returns>
        /// <response code="200">Debug information with route patterns.</response>
        [HttpGet("debug/routes")]
        [ProducesResponseType(typeof(IEnumerable<RouteDebugInfo>), 200)]
        public IActionResult DebugRoutes([FromServices] IEnumerable<EndpointDataSource> endpointSources)
        {
            var endpoints = endpointSources.SelectMany(es => es.Endpoints);
            var routes = endpoints.Select(e => new RouteDebugInfo
            {
                DisplayName = e.DisplayName,
                RoutePattern = (e as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern?.RawText
            }).ToList();
            return Ok(routes);
        }

        /// <summary>
        /// Registers a mode command for the reader.
        /// </summary>
        /// <param name="jsonDocument">JSON mode command body.</param>
        /// <returns>Status and info.</returns>
        /// <response code="200">Success.</response>
        /// <response code="400">Bad request.</response>
        [HttpPost("mode")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(object), 400)]
        public IActionResult SetMode([FromBody] JsonDocument jsonDocument)
        {
            var requestResult = new Dictionary<object, object>
            {
                { "", "" }
            };

            try
            {
                try
                {
                    JsonElement commandIdNode = jsonDocument.RootElement.GetProperty("command_id");
                    var commandId = commandIdNode.GetString();
                    requestResult["command_id"] = commandId;
                    requestResult["response"] = "queued";
                }
                catch (Exception ex)
                {
                    requestResult["response"] = "error";
                    requestResult["detail"] = ex.Message;
                    return BadRequest(requestResult);
                }

                using (var stream = new MemoryStream())
                {
                    var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                    jsonDocument.WriteTo(writer);
                    writer.Flush();
                    var jsonDocumentStr = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                    var command = new ReaderCommands
                    {
                        Id = "MODE_COMMAND",
                        Value = jsonDocumentStr,
                        Timestamp = DateTime.Now
                    };
                    _db.ReaderCommands.Add(command);
                    _db.SaveChanges();
                }

                return Ok(requestResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing mode command.");
                return BadRequest(requestResult);
            }
        }
    }



    /// <summary>
    /// Debug route info for API endpoints.
    /// </summary>
    public class RouteDebugInfo
    {
        public string DisplayName { get; set; }
        public string RoutePattern { get; set; }
    }

    /// <summary>
    /// DTO for bearer API test endpoint.
    /// </summary>
    public class BearerDTO
    {
        /// <example>https://someapi.com/token</example>
        public string httpAuthenticationTokenApiUrl { get; set; }
        /// <example>{"user":"a","password":"b"}</example>
        public string httpAuthenticationTokenApiBody { get; set; }
    }


}
