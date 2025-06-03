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
using SmartReaderStandalone.Services;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartReaderStandalone.Controllers
{
    /// <summary>
    /// Controller for server-sent events (SSE) streams (volumes and tags).
    /// </summary>
    [ApiController]
    [Route("api/stream")]
    [AuthorizeBasicAuth]
    public class StreamController : ControllerBase
    {
        private readonly RuntimeDb _db;
        private readonly ISummaryQueueBackgroundService _summaryService;
        private readonly ILogger<StreamController> _logger;

        public StreamController(RuntimeDb db, ISummaryQueueBackgroundService summaryService, ILogger<StreamController> logger)
        {
            _db = db;
            _summaryService = summaryService;
            _logger = logger;
        }

        /// <summary>
        /// Server-sent event stream of SmartReader SKU summaries (volumes).
        /// </summary>
        /// <remarks>
        /// Returns a continuous SSE stream (Content-Type: text/event-stream) with volume summary data in JSON format.
        /// </remarks>
        /// <returns>Async event stream of summary documents.</returns>
        /// <response code="200">Returns the SSE event stream.</response>
        [HttpGet("volumes")]
        [ProducesResponseType(typeof(IAsyncEnumerable<List<JsonDocument>>), 200)]
        public async IAsyncEnumerable<List<JsonDocument>> StreamVolumes()
        {
            Response.Headers.ContentType = "text/event-stream";
            var keepaliveStopWatch = new Stopwatch();
            keepaliveStopWatch.Start();

            while (true)
            {
                string dataModel = null;
                if (_summaryService.HasDataAvailable())
                {
                    dataModel = _summaryService.GetData();
                    if (!string.IsNullOrEmpty(dataModel))
                    {
                        var jsonOject = JsonDocument.Parse(dataModel);
                        var jsonString = JsonSerializer.Serialize(jsonOject);
                        var returnedData = Regex.Unescape(jsonString);

                        if (returnedData.StartsWith("[")) returnedData = returnedData.Substring(1);

                        List<JsonDocument> result = new() { jsonOject };
                        yield return result;
                    }
                }
                else if (keepaliveStopWatch.IsRunning && keepaliveStopWatch.Elapsed.TotalSeconds > 10)
                {
                    keepaliveStopWatch.Restart();
                    var jsonOject = JsonDocument.Parse(@"{}");
                    List<JsonDocument> result = new() { jsonOject };
                    yield return result;
                }

                await Task.Delay(100);
            }
        }

        /// <summary>
        /// Server-sent event stream of SmartReader tag read models.
        /// </summary>
        /// <remarks>
        /// Returns a continuous SSE stream (Content-Type: text/event-stream) with tag read data in JSON format.
        /// </remarks>
        /// <returns>Async event stream of tag read documents.</returns>
        /// <response code="200">Returns the SSE event stream.</response>
        [HttpGet("tags")]
        [ProducesResponseType(typeof(IAsyncEnumerable<List<JsonDocument>>), 200)]
        public async IAsyncEnumerable<List<JsonDocument>> StreamTags()
        {
            Response.Headers.ContentType = "text/event-stream";

            while (true)
            {
                var dataModel = _db.SmartReaderTagReadModels.LastOrDefault();
                if (dataModel != null && !string.IsNullOrEmpty(dataModel.Value))
                {
                    var json = dataModel.Value;
                    _logger.LogInformation("Publishing tag data: " + json);

                    var jsonOject = JsonDocument.Parse(json);
                    var jsonString = JsonSerializer.Serialize(jsonOject);
                    var returnedData = Regex.Unescape(jsonString);

                    if (returnedData.StartsWith("[")) returnedData = returnedData.Substring(1);

                    _db.SmartReaderTagReadModels.Remove(dataModel);
                    await _db.SaveChangesAsync();

                    List<JsonDocument> result = new() { jsonOject };
                    yield return result;
                }

                await Task.Delay(100);
            }
        }
    }
}

