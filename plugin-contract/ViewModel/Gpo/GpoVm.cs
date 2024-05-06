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

namespace SmartReaderStandalone.ViewModel.Gpo;

public partial class GpoVm
{
    [JsonProperty("gpoConfigurations", NullValueHandling = NullValueHandling.Ignore)]
    public List<SmartReaderGpoConfiguration>? GpoConfigurations { get; set; }
}

public class SmartReaderGpoConfiguration
{
    [JsonProperty("gpo", NullValueHandling = NullValueHandling.Ignore)]
    public int Gpo { get; set; }

    [JsonProperty("state", NullValueHandling = NullValueHandling.Ignore)]
    public bool State { get; set; }
}

public partial class GpoVm
{
    public static GpoVm FromJson(string json)
    {
        return JsonConvert.DeserializeObject<GpoVm>(json, Converter.Settings);
    }
}

public static class Serialize
{
    public static string ToJson(this GpoVm self)
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