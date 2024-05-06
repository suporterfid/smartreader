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

namespace SmartReaderStandalone.ViewModel.Read;

public partial class SmartReaderTagReadEvent
{
    [JsonProperty("readerName", NullValueHandling = NullValueHandling.Ignore)]
    public string? ReaderName { get; set; }

    [JsonProperty("mac", NullValueHandling = NullValueHandling.Ignore)]
    public string? Mac { get; set; }

    [JsonProperty("site", NullValueHandling = NullValueHandling.Ignore)]
    public string? Site { get; set; }

    //[JsonProperty("customField1Name", NullValueHandling = NullValueHandling.Ignore)]
    //public string CustomField1Name { get; set; }

    //[JsonProperty("customField2Name", NullValueHandling = NullValueHandling.Ignore)]
    //public string CustomField2Name { get; set; }

    //[JsonProperty("customField3Name", NullValueHandling = NullValueHandling.Ignore)]
    //public string CustomField3Name { get; set; }

    //[JsonProperty("customField4Name", NullValueHandling = NullValueHandling.Ignore)]
    //public string CustomField4Name { get; set; }

    [JsonProperty("tag_reads", NullValueHandling = NullValueHandling.Ignore)]
    public List<TagRead>? TagReads { get; set; }


    [JsonProperty("tag_reads_positioning", NullValueHandling = NullValueHandling.Ignore)]
    public List<TagReadPositioning>? TagReadsPositioning { get; set; }
}

public class TagRead
{
    [JsonProperty("epc", NullValueHandling = NullValueHandling.Ignore)]
    public string? Epc { get; set; }

    [JsonProperty("isHeartBeat", NullValueHandling = NullValueHandling.Ignore)]
    public bool IsHeartBeat { get; set; }

    [JsonProperty("isInventoryStatus", NullValueHandling = NullValueHandling.Ignore)]
    public bool IsInventoryStatus { get; set; }

    [JsonProperty("inventoryStatus", NullValueHandling = NullValueHandling.Ignore)]
    public string? InventoryStatus { get; set; }

    [JsonProperty("inventoryStatusId", NullValueHandling = NullValueHandling.Ignore)]
    public string? InventoryStatusId { get; set; }

    [JsonProperty("inventoryStatusTotalCount", NullValueHandling = NullValueHandling.Ignore)]
    public int? InventoryStatusTotalCount { get; set; }

    [JsonProperty("firstSeenTimestamp", NullValueHandling = NullValueHandling.Ignore)]
    public long? FirstSeenTimestamp { get; set; }

    [JsonProperty("antennaPort", NullValueHandling = NullValueHandling.Ignore)]
    public long? AntennaPort { get; set; }

    [JsonProperty("antennaZone", NullValueHandling = NullValueHandling.Ignore)]
    public string? AntennaZone { get; set; }

    [JsonProperty("peakRssi", NullValueHandling = NullValueHandling.Ignore)]
    public double? PeakRssi { get; set; }

    [JsonProperty("txPower", NullValueHandling = NullValueHandling.Ignore)]
    public double? TxPower { get; set; }

    [JsonProperty("tid", NullValueHandling = NullValueHandling.Ignore)]
    public string? Tid { get; set; }

    [JsonProperty("rfPhase", NullValueHandling = NullValueHandling.Ignore)]
    public double? RfPhase { get; set; }

    [JsonProperty("frequency", NullValueHandling = NullValueHandling.Ignore)]
    public long? Frequency { get; set; }

    [JsonProperty("rfChannel", NullValueHandling = NullValueHandling.Ignore)]
    public long? RfChannel { get; set; }

    [JsonProperty("gpi1Status", NullValueHandling = NullValueHandling.Ignore)]
    public string? Gpi1Status { get; set; }

    [JsonProperty("gpi2Status", NullValueHandling = NullValueHandling.Ignore)]
    public string? Gpi2Status { get; set; }

    [JsonProperty("gpi3Status", NullValueHandling = NullValueHandling.Ignore)]
    public string? Gpi3Status { get; set; }

    [JsonProperty("gpi4Status", NullValueHandling = NullValueHandling.Ignore)]
    public string? Gpi4Status { get; set; }

    [JsonProperty("tagDataKey", NullValueHandling = NullValueHandling.Ignore)]
    public string? TagDataKey { get; set; }

    [JsonProperty("tagDataKeyName", NullValueHandling = NullValueHandling.Ignore)]
    public string? TagDataKeyName { get; set; }

    [JsonProperty("tagDataSerial", NullValueHandling = NullValueHandling.Ignore)]
    public string? TagDataSerial { get; set; }

    [JsonProperty("tagDataPureIdentity", NullValueHandling = NullValueHandling.Ignore)]
    public string? TagDataPureIdentity { get; set; }
}

public class TagReadPositioning
{
    [JsonProperty("epc", NullValueHandling = NullValueHandling.Ignore)]
    public string? Epc { get; set; }

    [JsonProperty("isHeartBeat", NullValueHandling = NullValueHandling.Ignore)]
    public bool IsHeartBeat { get; set; }

    [JsonProperty("firstSeenTimestamp", NullValueHandling = NullValueHandling.Ignore)]
    public long? FirstSeenTimestamp { get; set; }

    [JsonProperty("antennaPort", NullValueHandling = NullValueHandling.Ignore)]
    public long? AntennaPort { get; set; }

    [JsonProperty("antennaZone", NullValueHandling = NullValueHandling.Ignore)]
    public string? AntennaZone { get; set; }

    [JsonProperty("peakRssi", NullValueHandling = NullValueHandling.Ignore)]
    public double? PeakRssi { get; set; }

    [JsonProperty("txPower", NullValueHandling = NullValueHandling.Ignore)]
    public double? TxPower { get; set; }

    [JsonProperty("tid", NullValueHandling = NullValueHandling.Ignore)]
    public string? Tid { get; set; }

    [JsonProperty("rfPhase", NullValueHandling = NullValueHandling.Ignore)]
    public double? RfPhase { get; set; }

    [JsonProperty("rfDoppler", NullValueHandling = NullValueHandling.Ignore)]
    public long? RfDoppler { get; set; }

    [JsonProperty("rfChannel", NullValueHandling = NullValueHandling.Ignore)]
    public long? RfChannel { get; set; }

    [JsonProperty("gpi1Status", NullValueHandling = NullValueHandling.Ignore)]
    public long? Gpi1Status { get; set; }

    [JsonProperty("gpi2Status", NullValueHandling = NullValueHandling.Ignore)]
    public long? Gpi2Status { get; set; }

    [JsonProperty("gpi3Status", NullValueHandling = NullValueHandling.Ignore)]
    public long? Gpi3Status { get; set; }

    [JsonProperty("gpi4Status", NullValueHandling = NullValueHandling.Ignore)]
    public long? Gpi4Status { get; set; }

    [JsonProperty("last_positioning_referecence_epcs", NullValueHandling = NullValueHandling.Ignore)]
    public List<LastPositioningReferecenceEpc>? LastPositioningReferecenceEpcs { get; set; }
}

public class LastPositioningReferecenceEpc
{
    [JsonProperty("epc", NullValueHandling = NullValueHandling.Ignore)]
    public string? Epc { get; set; }

    [JsonProperty("lastPositionEpcAntTimestamp", NullValueHandling = NullValueHandling.Ignore)]
    public long? LastPositionEpcAntTimestamp { get; set; }

    [JsonProperty("antennaPort", NullValueHandling = NullValueHandling.Ignore)]
    public long? AntennaPort { get; set; }

    [JsonProperty("antennaZone", NullValueHandling = NullValueHandling.Ignore)]
    public string? AntennaZone { get; set; }

    [JsonProperty("peakRssi", NullValueHandling = NullValueHandling.Ignore)]
    public double? PeakRssi { get; set; }
}

public partial class SmartReaderTagReadEvent
{
    public static SmartReaderTagReadEvent FromJson(string json)
    {
        return JsonConvert.DeserializeObject<SmartReaderTagReadEvent>(json, Converter.Settings);
    }
}

public static class Serialize
{
    public static string ToJson(this SmartReaderTagReadEvent self)
    {
        return JsonConvert.SerializeObject(self, Converter.Settings);
    }
}

internal static class Converter
{
    public static readonly JsonSerializerSettings Settings = new()
    {
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
        Converters =
        {
            new IsoDateTimeConverter {DateTimeStyles = DateTimeStyles.AssumeUniversal}
        }
    };
}