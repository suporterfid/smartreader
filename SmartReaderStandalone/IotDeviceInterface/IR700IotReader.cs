using System.Collections.ObjectModel;
using Impinj.Atlas;

namespace SmartReader.IotDeviceInterface;

public interface IR700IotReader
{
    string Nickname { get; set; }

    string UniqueId { get; }

    string MacAddress { get; }

    uint ModelNumber { get; }

    string ProductModel { get; }

    string Hostname { get; }

    string DisplayName { get; }

    List<double> TxPowersInDbm { get; }

    double MinPowerStepDbm { get; }

    string ReaderOperatingRegion { get; }

    bool IsAntennaHubEnabled { get; }

    List<string> IpAddresses { get; }

    Task StartAsync(string presetId);

    Task StartPresetAsync(string presetId);

    Task StopPresetAsync();

    Task StopAsync();

    Task SystemImageUpgradePostAsync(string file);

    Task<ObservableCollection<string>> GetSupportedReaderProfilesAsync();

    Task<ObservableCollection<string>> GetReaderInventoryPresetListAsync();

    Task<InventoryRequest> GetReaderInventoryPresetAsync(string presetId);

    Task SaveInventoryPresetAsync(string presetId, InventoryRequest inventoryRequest);

    Task<object> GetReaderInventorySchemaAsync();

    event EventHandler<TagInventoryEvent> TagInventoryEvent;

    event EventHandler<Impinj.Atlas.GpiTransitionEvent> GpiTransitionEvent;

    event EventHandler<InventoryStatusEvent> InventoryStatusEvent;

    //event EventHandler<Impinj.Atlas.TagInventoryEventConfiguration> TagInventoryEventConfiguration;

    event EventHandler<DiagnosticEvent> DiagnosticEvent;

    event EventHandler<IotDeviceInterfaceException> StreamingErrorEvent;

    Task UpdateInventoryPresetAsync(string presetId, InventoryRequest updatedInventoryRequest);

    Task DeleteInventoryPresetAsync(string presetId);

    Task<TimeInfo> DeviceReaderTimeGetAsync();

    Task<ReaderStatus> GetStatusAsync();

    Task<AntennaHubInfo> GetSystemAntennaHubInfoAsync();

    Task<RegionInfo> GetSystemRegionInfoAsync();

    Task<SystemInfo> GetSystemInfoAsync();

    Task SendCustomInventoryPresetAsync(string presetId, string jsonInventoryRequest);

    Task PostTransientInventoryPresetAsync(string jsonInventoryRequest);

    Task PostTransientInventoryStopPresetsAsync();

    Task<ReaderStatus> DeviceReaderStatusGetAsync();

    Task<RfidInterface> DeviceReaderRfidInterfaceGetAsync();

    Task UpdateReaderRfidInterface(RfidInterface rfidInterface);

    Task<MqttConfiguration> GetReaderMqttAsync();

    Task UpdateReaderMqttAsync(MqttConfiguration mqttConfiguration);

    Task<GpoConfigurations> DeviceGposGetAsync();

    Task<object> DeviceGetInventoryPresetsSchemaAsync();

    Task UpdateReaderGpoAsync(GpoConfigurations gpoConfiguration);
}