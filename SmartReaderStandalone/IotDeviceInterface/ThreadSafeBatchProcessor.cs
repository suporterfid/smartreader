using Newtonsoft.Json.Linq;
using SmartReader.Infrastructure.ViewModel;
using SmartReaderStandalone.ViewModel.Read;
using System.Collections.Concurrent;
using System.Globalization;

public class ThreadSafeBatchProcessor : IDisposable
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, TagEventData> _tagEventDataDictionary;
    private readonly ConcurrentDictionary<string, JObject> _eventsListBatch;
    private readonly ConcurrentDictionary<string, JObject> _eventsListBatchOnUpdate;
    private readonly ConcurrentDictionary<string, JObject> _eventsListAbsent;
    private readonly StandaloneConfigDTO _config;
    private readonly SemaphoreSlim _batchLock = new(1, 1);
    private readonly Timer _expirationTimer;
    private readonly int _expirationCheckIntervalMs = 100; // Check every second

    public ThreadSafeBatchProcessor(
        ILogger logger,
        StandaloneConfigDTO config,
        ref ConcurrentDictionary<string, JObject> eventsListBatch,
        ref ConcurrentDictionary<string, JObject> eventsListBatchOnUpdate,
        ref ConcurrentDictionary<string, JObject> eventsListAbsent)
    {
        _logger = logger;
        _config = config;
        _eventsListBatch = eventsListBatch;
        _eventsListAbsent = eventsListAbsent;
        _eventsListBatchOnUpdate = eventsListBatchOnUpdate;
        _tagEventDataDictionary = new ConcurrentDictionary<string, TagEventData>();

        // Initialize and start the expiration timer
        _expirationTimer = new Timer(CheckExpiredTags, null, 0, _expirationCheckIntervalMs);
    }

    private async void CheckExpiredTags(object? state)
    {
        if (_config.tagPresenceTimeoutEnabled != "1")
        {
            return;
        }

        try
        {
            await _batchLock.WaitAsync();
            var timeoutSeconds = double.Parse(_config.tagPresenceTimeoutInSec, CultureInfo.InvariantCulture);
            _ = ProcessExpiredTags(timeoutSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for expired tags");
        }
        finally
        {
            _ = _batchLock.Release();
        }
    }

    private bool ProcessExpiredTags(double expirationInSeconds)
    {
        bool tagsExpired = false;
        var expiredTags = GetExpiredTags(expirationInSeconds).ToList();

        if (expiredTags.Any())
        {
            _logger.LogInformation($"Found {expiredTags.Count} expired tags to process");

            foreach (var expiredEpc in expiredTags)
            {
                if (_eventsListBatch.TryRemove(expiredEpc, out var expiredEvent))
                {
                    _ = _eventsListAbsent.TryAdd(expiredEpc, expiredEvent);
                    _logger.LogInformation($"Moved expired EPC: {expiredEpc} to eventsListAbsent.");

                    // Trigger update with current state when a tag expires
                    foreach (var remainingTag in _eventsListBatch)
                    {
                        _ = _eventsListBatchOnUpdate.AddOrUpdate(
                            remainingTag.Key,
                            remainingTag.Value,
                            (_, __) => remainingTag.Value);
                    }

                    tagsExpired = true;
                }
                _ = _tagEventDataDictionary.TryRemove(expiredEpc, out _);
            }
        }

        return tagsExpired;
    }

    public async Task<bool> ProcessEventBatchAsync(TagRead tagRead, JObject? tagDataJObject)
    {
        try
        {
            await _batchLock.WaitAsync();

            if (_tagEventDataDictionary.TryGetValue(tagRead.Epc, out var existingData))
            {
                UpdateExistingTagEvent(tagRead, existingData, tagDataJObject);
            }
            else
            {
                AddNewTagEvent(tagRead, tagDataJObject);
            }

            return true;
        }
        finally
        {
            _ = _batchLock.Release();
        }
    }

    private void UpdateExistingTagEvent(TagRead tagRead, TagEventData existingData, JObject? tagDataJObject)
    {
        bool shouldPublish = HasAntennaChanged(tagRead, existingData);

        existingData.FirstSeenTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        existingData.AntennaPort = tagRead.AntennaPort;
        existingData.AntennaZone = tagRead.AntennaZone;

        // Update collections if antenna changed
        if (shouldPublish)
        {
            _logger.LogInformation($"Publishing updated tag data for EPC: {tagRead.Epc}");
            _ = _eventsListBatchOnUpdate.AddOrUpdate(tagRead.Epc, tagDataJObject, (_, __) => tagDataJObject);
            _ = _eventsListBatch.AddOrUpdate(tagRead.Epc, tagDataJObject, (_, __) => tagDataJObject);
        }
    }

    private void AddNewTagEvent(TagRead tagRead, JObject? tagDataJObject)
    {
        var newTagEventData = new TagEventData
        {
            Epc = tagRead.Epc,
            FirstSeenTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AntennaPort = tagRead.AntennaPort,
            AntennaZone = tagRead.AntennaZone
        };

        _ = _tagEventDataDictionary.TryAdd(tagRead.Epc, newTagEventData);
        _ = _eventsListBatch.TryAdd(tagRead.Epc, tagDataJObject);

        // Trigger update with all current tags when a new tag enters
        foreach (var tag in _eventsListBatch)
        {
            _ = _eventsListBatchOnUpdate.AddOrUpdate(
                tag.Key,
                tag.Value,
                (_, __) => tag.Value);
        }

        _logger.LogInformation($"Added new tag event for EPC: {tagRead.Epc}");
    }

    private bool HasAntennaChanged(TagRead tagRead, TagEventData existingData)
    {
        if (existingData.AntennaPort != tagRead.AntennaPort ||
            existingData.AntennaZone != tagRead.AntennaZone)
        {
            _logger.LogInformation($"Tag EPC: {tagRead.Epc} moved from AntennaPort: {existingData.AntennaPort} to {tagRead.AntennaPort} or from AntennaZone: {existingData.AntennaZone} to {tagRead.AntennaZone}.");
            return true;
        }
        return false;
    }

    private IEnumerable<string> GetExpiredTags(double expirationInSeconds)
    {
        var currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var kvp in _tagEventDataDictionary)
        {
            var timeElapsed = (currentTimestamp - kvp.Value.FirstSeenTimestamp) / 1000.0;
            if (timeElapsed > expirationInSeconds)
            {
                yield return kvp.Key;
            }
        }
    }

    // Interface methods for TagEventPublisher
    public ConcurrentDictionary<string, JObject> GetCurrentBatch() => _eventsListBatch;
    public ConcurrentDictionary<string, JObject> GetOnUpdateBatch() => _eventsListBatchOnUpdate;
    public ConcurrentDictionary<string, JObject> GetAbsentBatch() => _eventsListAbsent;
    public void ClearOnUpdateBatch() => _eventsListBatchOnUpdate.Clear();
    public void ClearAbsentList() => _eventsListAbsent.Clear();

    public void Dispose()
    {
        _expirationTimer?.Dispose();
        _batchLock?.Dispose();
    }
}

public class TagEventData
{
    public required string Epc { get; set; }
    public long FirstSeenTimestamp { get; set; }
    public long? AntennaPort { get; set; }
    public required string AntennaZone { get; set; }
}




