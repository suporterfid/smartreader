using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using SmartReader.Infrastructure.ViewModel;
using SmartReaderStandalone.ViewModel.Read;

public class ThreadSafeBatchProcessor
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, JObject> _eventsListBatch;
    private readonly ConcurrentDictionary<string, JObject> _eventsListBatchOnUpdate;
    private readonly SemaphoreSlim _batchLock = new SemaphoreSlim(1, 1);

    public ThreadSafeBatchProcessor(
        ILogger logger,
        ConcurrentDictionary<string, JObject> eventsListBatch,
        ConcurrentDictionary<string, JObject> eventsListBatchOnUpdate)
    {
        _logger = logger;
        _eventsListBatch = eventsListBatch;
        _eventsListBatchOnUpdate = eventsListBatchOnUpdate;
    }

    public async Task ProcessEventBatchAsync(
        TagRead tagRead, 
        JObject dataToPublish, 
        StandaloneConfigDTO config)
    {
        try
        {
            await _batchLock.WaitAsync();
            try
            {
                if (_eventsListBatch.TryGetValue(tagRead.Epc, out JObject retrievedValue))
                {
                    await UpdateExistingEventBatchAsync(
                        tagRead, 
                        dataToPublish, 
                        retrievedValue, 
                        config);
                }
                else
                {
                    await AddNewEventBatchAsync(
                        tagRead, 
                        dataToPublish, 
                        config);
                }
            }
            finally
            {
                _batchLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing event batch for EPC: {tagRead.Epc}");
            throw;
        }
    }

    private async Task UpdateExistingEventBatchAsync(
        TagRead tagRead, 
        JObject dataToPublish, 
        JObject retrievedValue, 
        StandaloneConfigDTO config)
    {
        try
        {
            var existingEventOnCurrentAntenna = (JObject)(retrievedValue["tag_reads"]
                .FirstOrDefault(q => (long)q["antennaPort"] == tagRead.AntennaPort));

            if (ShouldUpdateBasedOnAntennaZone(tagRead, retrievedValue, config))
            {
                await TryAddToUpdateBatch(tagRead, dataToPublish, config);
            }
            else if (existingEventOnCurrentAntenna == null)
            {
                await TryAddToUpdateBatch(tagRead, dataToPublish, config);
            }

            _eventsListBatch.TryUpdate(tagRead.Epc, dataToPublish, retrievedValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating event batch for EPC: {tagRead.Epc}");
            throw;
        }
    }

    private bool ShouldUpdateBasedOnAntennaZone(
        TagRead tagRead, 
        JObject retrievedValue, 
        StandaloneConfigDTO config)
    {
        if (!string.Equals("1", config.filterTagEventsListBatchOnChangeBasedOnAntennaZone, 
            StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var existingEventOnCurrentAntennaZone = (JObject)(retrievedValue["tag_reads"]
            .FirstOrDefault(q => (string)q["epc"] == tagRead.Epc));

        if (existingEventOnCurrentAntennaZone == null)
        {
            return false;
        }

        var currentAntennaZone = existingEventOnCurrentAntennaZone["antennaZone"].Value<string>();
        return !string.IsNullOrEmpty(currentAntennaZone) 
            && !tagRead.AntennaZone.Equals(currentAntennaZone);
    }

    private async Task AddNewEventBatchAsync(
        TagRead tagRead, 
        JObject dataToPublish, 
        StandaloneConfigDTO config)
    {
        if (string.Equals("1", config.updateTagEventsListBatchOnChange, 
            StringComparison.OrdinalIgnoreCase)
            && !_eventsListBatchOnUpdate.ContainsKey(tagRead.Epc))
        {
            _eventsListBatchOnUpdate.TryAdd(tagRead.Epc, dataToPublish);
        }
        else
        {
            _eventsListBatch.TryAdd(tagRead.Epc, dataToPublish);
        }
    }

    private async Task TryAddToUpdateBatch(
        TagRead tagRead, 
        JObject dataToPublish, 
        StandaloneConfigDTO config)
    {
        if (string.Equals("1", config.updateTagEventsListBatchOnChange, 
            StringComparison.OrdinalIgnoreCase)
            && !_eventsListBatchOnUpdate.ContainsKey(tagRead.Epc))
        {
            _eventsListBatchOnUpdate.TryAdd(tagRead.Epc, dataToPublish);
        }
    }
}