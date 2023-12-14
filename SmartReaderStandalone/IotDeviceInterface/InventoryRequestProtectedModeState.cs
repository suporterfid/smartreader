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
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Globalization;

namespace SmartReader.IotDeviceInterface;

public partial class InventoryRequestProtectedModeState
{
    [JsonProperty("eventConfig", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public EventConfig EventConfig { get; set; }

    [JsonProperty("antennaConfigs", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public List<AntennaConfig> AntennaConfigs { get; set; }

    [JsonProperty("channelFrequenciesKHz", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public List<long> ChannelFrequenciesKHz { get; set; }

    [JsonProperty("startTriggers", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public List<Trigger> StartTriggers { get; set; }

    [JsonProperty("stopTriggers", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public List<Trigger> StopTriggers { get; set; }
}

public class AntennaConfig
{
    [JsonProperty("antennaName", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string AntennaName { get; set; }

    [JsonProperty("antennaPort", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long? AntennaPort { get; set; }

    [JsonProperty("transmitPowerCdbm", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long? TransmitPowerCdbm { get; set; }

    [JsonProperty("rfMode", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long? RfMode { get; set; }

    [JsonProperty("inventorySession", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long? InventorySession { get; set; }

    [JsonProperty("inventorySearchMode", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string InventorySearchMode { get; set; }

    [JsonProperty("estimatedTagPopulation", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long? EstimatedTagPopulation { get; set; }

    [JsonProperty("filtering", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public Filtering Filtering { get; set; }

    [JsonProperty("powerSweeping", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public PowerSweeping PowerSweeping { get; set; }

    [JsonProperty("fastId", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string FastId { get; set; }

    [JsonProperty("protectedModePinHex", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string ProtectedModePinHex { get; set; }

    [JsonProperty("receiveSensitivityDbm", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long? ReceiveSensitivityDbm { get; set; }

    [JsonProperty("tagAuthentication", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public TagAuthentication TagAuthentication { get; set; }

    [JsonProperty("tagMemoryReads", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public List<TagMemoryRead> TagMemoryReads { get; set; }

    [JsonProperty("tagAccessPasswordHex", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string TagAccessPasswordHex { get; set; }

    [JsonProperty("tagAccessPasswordWriteHex", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string TagAccessPasswordWriteHex { get; set; }

    [JsonProperty("tagSecurityModesWrite", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public TagSecurityModesWrite TagSecurityModesWrite { get; set; }
}

public class Filtering
{
    [JsonProperty("filters", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public List<Filter> Filters { get; set; }

    [JsonProperty("filterLink", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string FilterLink { get; set; }

    [JsonProperty("filterVerification", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string FilterVerification { get; set; }
}

public class Filter
{
    [JsonProperty("action", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Action { get; set; }

    [JsonProperty("tagMemoryBank", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string TagMemoryBank { get; set; }

    [JsonProperty("bitOffset", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long? BitOffset { get; set; }

    [JsonProperty("mask", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Mask { get; set; }

    [JsonProperty("maskLength", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long? MaskLength { get; set; }
}

public class PowerSweeping
{
    [JsonProperty("minimumPowerCdbm", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long? MinimumPowerCdbm { get; set; }

    [JsonProperty("stepSizeCdb", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long? StepSizeCdb { get; set; }
}

public class TagAuthentication
{
    [JsonProperty("messageHex", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string MessageHex { get; set; }
}

public class TagMemoryRead
{
    [JsonProperty("memoryBank", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string MemoryBank { get; set; }

    [JsonProperty("wordOffset", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long? WordOffset { get; set; }

    [JsonProperty("wordCount", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long? WordCount { get; set; }
}

public class TagSecurityModesWrite
{
    [JsonProperty("protected", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool? Protected { get; set; }

    [JsonProperty("shortRange", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool? ShortRange { get; set; }
}

public class EventConfig
{
    [JsonProperty("common", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public Common Common { get; set; }

    [JsonProperty("tagInventory", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public TagInventory TagInventory { get; set; }
}

public class Common
{
    [JsonProperty("hostname", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Hostname { get; set; }
}

public class TagInventory
{
    [JsonProperty("tagReporting", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public TagReporting TagReporting { get; set; }

    [JsonProperty("epc", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Epc { get; set; }

    [JsonProperty("epcHex", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string EpcHex { get; set; }

    [JsonProperty("tid", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Tid { get; set; }

    [JsonProperty("tidHex", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string TidHex { get; set; }

    [JsonProperty("antennaPort", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string AntennaPort { get; set; }

    [JsonProperty("transmitPowerCdbm", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string TransmitPowerCdbm { get; set; }

    [JsonProperty("peakRssiCdbm", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string PeakRssiCdbm { get; set; }

    [JsonProperty("frequency", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Frequency { get; set; }

    [JsonProperty("pc", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Pc { get; set; }

    [JsonProperty("lastSeenTime", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string LastSeenTime { get; set; }

    [JsonProperty("phaseAngle", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string PhaseAngle { get; set; }
}

public class TagReporting
{
    [JsonProperty("reportingIntervalSeconds", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long? ReportingIntervalSeconds { get; set; }

    [JsonProperty("tagCacheSize", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long? TagCacheSize { get; set; }

    [JsonProperty("antennaIdentifier", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string AntennaIdentifier { get; set; }

    [JsonProperty("tagIdentifier", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string TagIdentifier { get; set; }
}

public class Trigger
{
    [JsonProperty("gpiTransitionEvent", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public GpiTransitionEvent GpiTransitionEvent { get; set; }
}

public class GpiTransitionEvent
{
    [JsonProperty("gpi", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long? Gpi { get; set; }

    [JsonProperty("transition", NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Transition { get; set; }
}

public partial class InventoryRequestProtectedModeState
{
    public static InventoryRequestProtectedModeState FromJson(string json)
    {
        return JsonConvert.DeserializeObject<InventoryRequestProtectedModeState>(json, Converter.Settings);
    }
}

public static class Serialize
{
    public static string ToJson(this InventoryRequestProtectedModeState self)
    {
        return JsonConvert.SerializeObject(self, Converter.Settings);
    }
}

internal static class Converter
{
    public static readonly JsonSerializerSettings Settings = new()
    {
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
        Converters =
        {
            new IsoDateTimeConverter {DateTimeStyles = DateTimeStyles.AssumeUniversal}
        }
    };
}