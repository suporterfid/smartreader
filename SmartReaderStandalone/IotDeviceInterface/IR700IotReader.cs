#region copyright
//****************************************************************************************************
// Copyright ©2023 Impinj, Inc.All rights reserved.              
//                                    
// You may use and modify this code under the terms of the Impinj Software Tools License & Disclaimer. 
// Visit https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer   
// for full license details, or contact Impinj, Inc.at support@impinj.com for a copy of the license.   
//
//****************************************************************************************************
#endregion
using Impinj.Atlas;
using plugin_contract.ViewModel.Gpi;
using System.Collections.ObjectModel;

namespace SmartReader.IotDeviceInterface;

public interface IR700IotReader : IDisposable
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

    bool IsNetworkConnected { get; }

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

    event EventHandler<GpiTransitionVm> GpiTransitionEvent;

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

    Task<PowerConfiguration> GetSystemPowerAsync();

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

    Task UpdateReaderGpiAsync(GpiConfigRoot gpiConfiguration);
}