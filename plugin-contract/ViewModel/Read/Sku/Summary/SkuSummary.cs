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

namespace SmartReaderStandalone.ViewModel.Read.Sku.Summary;

public partial class SkuSummary
{
    [JsonProperty("barcode", NullValueHandling = NullValueHandling.Ignore)]
    public string? Barcode { get; set; }

    [JsonProperty("sku", NullValueHandling = NullValueHandling.Ignore)]
    public string? Sku { get; set; }

    [JsonProperty("qty", NullValueHandling = NullValueHandling.Ignore)]
    public long? Qty { get; set; }

    [JsonProperty("eventTimestamp", NullValueHandling = NullValueHandling.Ignore)]
    public long? EventTimestamp { get; set; }

    [JsonProperty("additionalData", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, string>? AdditionalData { get; set; }

    [JsonProperty("epcs", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? Epcs { get; set; }
}

public partial class SkuSummary
{
    public static List<SkuSummary> FromJson(string json)
    {
        return JsonConvert.DeserializeObject<List<SkuSummary>>(json, Converter.Settings);
    }
}

public static class Serialize
{
    public static string ToJson(this List<SkuSummary> self)
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