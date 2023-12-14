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

namespace SmartReaderStandalone.ViewModel.Read.Rci;

public partial class RciSpotReportEvent
{
    public RciSpotReportEvent()
    {
        Report = "TagEvent";
    }

    [JsonProperty("Report", NullValueHandling = NullValueHandling.Ignore)]
    public string Report { get; set; }

    [JsonProperty("PC", NullValueHandling = NullValueHandling.Ignore)]
    public string Pc { get; set; }

    [JsonProperty("Scheme", NullValueHandling = NullValueHandling.Ignore)]
    public string Scheme { get; set; }

    [JsonProperty("EPC", NullValueHandling = NullValueHandling.Ignore)]
    public string Epc { get; set; }

    [JsonProperty("EPC-URI", NullValueHandling = NullValueHandling.Ignore)]
    public string EpcUri { get; set; }

    [JsonProperty("Ant", NullValueHandling = NullValueHandling.Ignore)]
    public long? Ant { get; set; }

    [JsonProperty("DT", NullValueHandling = NullValueHandling.Ignore)]
    public DateTimeOffset? Dt { get; set; }

    [JsonProperty("DwnCnt", NullValueHandling = NullValueHandling.Ignore)]
    public long? DwnCnt { get; set; }

    [JsonProperty("InvCnt", NullValueHandling = NullValueHandling.Ignore)]
    public long? InvCnt { get; set; }

    [JsonProperty("Phase", NullValueHandling = NullValueHandling.Ignore)]
    public long? Phase { get; set; }

    [JsonProperty("Prof", NullValueHandling = NullValueHandling.Ignore)]
    public long? Prof { get; set; }

    [JsonProperty("Range", NullValueHandling = NullValueHandling.Ignore)]
    public long? Range { get; set; }

    [JsonProperty("RSSI", NullValueHandling = NullValueHandling.Ignore)]
    public long? Rssi { get; set; }

    [JsonProperty("RZ", NullValueHandling = NullValueHandling.Ignore)]
    public long? Rz { get; set; }

    [JsonProperty("Spot", NullValueHandling = NullValueHandling.Ignore)]
    public long? Spot { get; set; }

    [JsonProperty("TimeStamp", NullValueHandling = NullValueHandling.Ignore)]
    public long? TimeStamp { get; set; }
}

public partial class RciSpotReportEvent
{
    public static RciSpotReportEvent FromJson(string json)
    {
        return JsonConvert.DeserializeObject<RciSpotReportEvent>(json, Converter.Settings);
    }
}

public static class Serialize
{
    public static string ToJson(this RciSpotReportEvent self)
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