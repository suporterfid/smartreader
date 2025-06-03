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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartReader.Infrastructure.ViewModel;
using SmartReaderStandalone.ViewModel.Read;
using System.Diagnostics;
using System.Globalization;

namespace SmartReaderStandalone.IotDeviceInterface
{
    public class TagEventPublisher
    {
        private readonly ILogger _logger;
        private readonly StandaloneConfigDTO _standaloneConfig;
        private readonly MqttPublishingConfiguration _mqttConfig;
        private readonly ThreadSafeBatchProcessor _batchProcessor;
        private readonly Stopwatch _publishingStopwatch;
        private bool _emptyEventPublished;
        private DateTime _lastEmptyCheck;
        private string _readerName;
        private string _macAddress;

        public TagEventPublisher(
            ILogger logger,
            StandaloneConfigDTO standaloneConfig,
            MqttPublishingConfiguration mqttConfig,
            ThreadSafeBatchProcessor batchProcessor,
            string readerName,
            string macAddress)
        {
            _logger = logger;
            _standaloneConfig = standaloneConfig;
            _mqttConfig = mqttConfig;
            _batchProcessor = batchProcessor;
            _publishingStopwatch = new Stopwatch();
            _publishingStopwatch.Start();
            _lastEmptyCheck = DateTime.UtcNow;
            _readerName = readerName;
            _macAddress = macAddress;
        }

        public void ProcessPublishing(ref JObject? smartReaderTagReadEventAggregated, JArray smartReaderTagReadEventsArray)
        {
            try
            {
                // Check for empty zone condition first
                if (ShouldPublishEmptyEvent())
                {
                    PublishEmptyEvent(ref smartReaderTagReadEventAggregated);
                    _batchProcessor.ClearAbsentList();
                    return;
                }

                // Process immediate publishing if enabled
                if (_mqttConfig.BatchListUpdateTagEventsOnChange)
                {
                    ProcessImmediateUpdates(ref smartReaderTagReadEventAggregated, smartReaderTagReadEventsArray);
                }

                // Process periodic batch publishing if enabled
                if (_mqttConfig.BatchListPublishingEnabled)
                {
                    ProcessBatchUpdates(ref smartReaderTagReadEventAggregated, smartReaderTagReadEventsArray);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing tag event publishing");
            }
        }

        private bool ShouldPublishEmptyEvent()
        {
            var currentBatch = _batchProcessor.GetCurrentBatch();
            var absentBatch = _batchProcessor.GetAbsentBatch();

            bool isEmptyZone = currentBatch.Count == 0;
            bool hasAbsentTags = absentBatch.Count > 0;

            if (isEmptyZone && hasAbsentTags && !_emptyEventPublished)
            {
                if (_standaloneConfig.tagPresenceTimeoutEnabled == "1")
                {
                    double timeoutSec = double.Parse(_standaloneConfig.tagPresenceTimeoutInSec, CultureInfo.InvariantCulture);
                    var timeSinceLastCheck = (DateTime.UtcNow - _lastEmptyCheck).TotalSeconds;

                    if (timeSinceLastCheck >= timeoutSec)
                    {
                        _lastEmptyCheck = DateTime.UtcNow;
                        return true;
                    }
                }
            }
            else if (!isEmptyZone)
            {
                _emptyEventPublished = false;
            }

            return false;
        }

        private void ProcessImmediateUpdates(ref JObject? aggregatedEvent, JArray eventsArray)
        {
            var eventsListBatchOnUpdate = _batchProcessor.GetOnUpdateBatch();

            if (eventsListBatchOnUpdate.Count > 0)
            {
                _logger.LogInformation($"Publishing {eventsListBatchOnUpdate.Count} immediate updates");
                _emptyEventPublished = false;

                AggregateEvents(eventsListBatchOnUpdate.Values, ref aggregatedEvent, eventsArray);
                _batchProcessor.ClearOnUpdateBatch();
            }
        }

        private void ProcessBatchUpdates(ref JObject? aggregatedEvent, JArray eventsArray)
        {
            if (_publishingStopwatch.ElapsedMilliseconds < _mqttConfig.BatchUpdateIntervalMs)
            {
                return;
            }

            var eventsListBatch = _batchProcessor.GetCurrentBatch();
            _publishingStopwatch.Restart();

            if (eventsListBatch.Count > 0)
            {
                AggregateEvents(eventsListBatch.Values, ref aggregatedEvent, eventsArray);
            }
        }

        private void PublishEmptyEvent(ref JObject? aggregatedEvent)
        {
            _logger.LogInformation("Tags left read zone, publishing empty event list");

            Dictionary<string, object> emptyEvent = new()
            {
                { "readerName", _readerName },
                { "mac", _macAddress },
                { "tag_reads", new List<TagRead>() }
            };
            //var smartReaderTagReadEvent = new SmartReaderTagReadEvent();
            //smartReaderTagReadEvent.TagReads = new List<TagRead>();
            //var tagRead = new TagRead();
            //// tagRead.FirstSeenTimestamp = Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now);
            //smartReaderTagReadEvent.ReaderName = _readerName;
            //smartReaderTagReadEvent.Mac = _macAddress;

            var jsonData = JsonConvert.SerializeObject(emptyEvent);
            aggregatedEvent = JObject.Parse(jsonData);

            _emptyEventPublished = true;
            _logger.LogDebug("Empty event published, waiting for new tags");

            //if (tagRead != null)
            //{
            //    aggregatedEvent = new JObject(smartReaderTagReadEvent)
            //    {
            //        ["tag_reads"] = new JArray()
            //    };

            //    _emptyEventPublished = true;
            //    _logger.LogDebug("Empty event published, waiting for new tags");
            //}
        }
        //private void PublishEmptyEvent(ref JObject? aggregatedEvent)
        //{
        //    _logger.LogInformation("Publishing empty event - No tags in reading zone");
        //    aggregatedEvent = new JObject
        //    {
        //        ["type"] = "empty",
        //        ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        //    };
        //    _emptyEventPublished = true;
        //}

        /// <summary>
        /// Extracts the first tag read from a tag read event object
        /// </summary>
        /// <param name="eventObject">The JObject containing tag read data</param>
        /// <returns>The first tag read token or null if none exists</returns>
        private JToken? ExtractTagRead(JObject eventObject)
        {
            try
            {
                // Get the "tag_reads" property
                var tagReadsProperty = eventObject.Property("tag_reads");
                if (tagReadsProperty == null)
                {
                    _logger.LogDebug("No tag_reads property found in event object");
                    return null;
                }

                // Get all tag reads as a list
                var tagReadsList = tagReadsProperty.ToList();
                if (!tagReadsList.Any())
                {
                    _logger.LogDebug("Tag reads list is empty");
                    return null;
                }

                // Get the first tag read entry
                var firstTagReadEntry = tagReadsList.FirstOrDefault();
                if (firstTagReadEntry == null)
                {
                    _logger.LogDebug("First tag read entry is null");
                    return null;
                }

                // Get the first tag read data
                var firstTagRead = firstTagReadEntry.FirstOrDefault();
                if (firstTagRead == null)
                {
                    _logger.LogDebug("First tag read data is null");
                    return null;
                }

                return firstTagRead;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting tag read from event object");
                return null;
            }
        }

        /// <summary>
        /// Validates if a tag read token contains the required data
        /// </summary>
        /// <param name="tagRead">The tag read token to validate</param>
        /// <returns>True if the tag read is valid, false otherwise</returns>
        private bool IsValidTagRead(JToken? tagRead)
        {
            if (tagRead == null)
            {
                _logger.LogDebug("Tag read is null");
                return false;
            }

            try
            {
                // Check for required fields
                bool hasEpc = tagRead["epc"] != null;
                bool hasTimestamp = tagRead["firstSeenTimestamp"] != null;
                bool hasAntennaPort = tagRead["antennaPort"] != null;
                bool hasAntennaZone = tagRead["antennaZone"] != null;
                bool hasRssi = tagRead["peakRssi"] != null;

                // Log which fields are missing if validation fails
                if (!hasEpc || !hasTimestamp || !hasAntennaPort || !hasAntennaZone || !hasRssi)
                {
                    _logger.LogDebug("Tag read missing required fields: " +
                        $"EPC: {hasEpc}, Timestamp: {hasTimestamp}, " +
                        $"Antenna Port: {hasAntennaPort}, Antenna Zone: {hasAntennaZone}, RSSI: {hasRssi}");
                    return false;
                }

                // Optional: Add additional validation for field values
                var epc = tagRead["epc"].ToString();
                if (string.IsNullOrEmpty(epc))
                {
                    _logger.LogDebug("Tag read has empty EPC value");
                    return false;
                }

                // Validate timestamp format if needed
                if (tagRead["firstSeenTimestamp"] != null)
                {
                    try
                    {
                        long firstSeenTimestamp = tagRead["firstSeenTimestamp"].ToObject<long>() / 1000; // Convert microseconds to milliseconds
                    }
                    catch (Exception)
                    {
                        _logger.LogDebug("Tag read has invalid timestamp format");
                        return false;
                    }

                }

                // Validate RSSI is a number
                if (!double.TryParse(tagRead["peakRssi"].ToString(),
                    out double rssi))
                {
                    _logger.LogDebug("Tag read has invalid RSSI format");
                    return false;
                }

                // Validate antenna is a positive number
                if (!int.TryParse(tagRead["antennaPort"].ToString(),
                    out int antenna) || antenna <= 0)
                {
                    _logger.LogDebug("Tag read has invalid antenna number");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating tag read");
                return false;
            }
        }

        private void AggregateEvents(
    IEnumerable<JObject> events,
    ref JObject? smartReaderTagReadEventAggregated,
    JArray smartReaderTagReadEventsArray)
        {
            try
            {
                Dictionary<string, object> newEvent = [];
                var tagReadList = new List<JToken>();
                newEvent.Add("readerName", _readerName);
                newEvent.Add("mac", _macAddress);
                newEvent.Add("tag_reads", new List<JToken>());

                foreach (var smartReaderTagReadEvent in events)
                {
                    if (smartReaderTagReadEvent == null)
                        continue;

                    try
                    {
                        var tagRead = ExtractTagRead(smartReaderTagReadEvent);
                        if (IsValidTagRead(tagRead))
                        {
                            tagReadList.Add(tagRead);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing event during aggregation");
                    }
                }

                newEvent["tag_reads"] = tagReadList;

                var jsonData = JsonConvert.SerializeObject(newEvent);
                smartReaderTagReadEventAggregated = JObject.Parse(jsonData);

                //_emptyEventPublished = true;
                //_logger.LogDebug("Empty event published, waiting for new tags");

                LogAggregationResults(smartReaderTagReadEventAggregated, smartReaderTagReadEventsArray);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error during event aggregation");
                throw;
            }
        }

        private void LogAggregationResults(JObject? aggregatedEvent, JArray tagReadsArray)
        {
            if (aggregatedEvent == null || tagReadsArray.Count == 0)
            {
                _logger.LogDebug("No events were aggregated");
            }
            else
            {
                _logger.LogDebug("Successfully aggregated {Count} tag read events", tagReadsArray.Count);
            }
        }

        //private void AggregateEvents(IEnumerable<JObject> events, ref JObject? aggregatedEvent, JArray eventsArray)
        //{
        //    aggregatedEvent = new JObject();
        //    eventsArray.Clear();

        //    foreach (var evt in events)
        //    {
        //        eventsArray.Add(evt);
        //    }

        //    aggregatedEvent["events"] = eventsArray;
        //    aggregatedEvent["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        //}

    }
}
