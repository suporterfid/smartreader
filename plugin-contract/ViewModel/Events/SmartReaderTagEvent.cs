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
using Newtonsoft.Json.Converters;
using System.Globalization;

namespace SmartReaderJobs.ViewModel.Events;

public partial class SmartReaderTagEvent
{
    [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
    public string? Status { get; set; }

    [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
    public List<SmartReaderTagEventData>? Data { get; set; }
}

public class SmartReaderTagEventData
{
    [JsonProperty("angulo_de_fase", NullValueHandling = NullValueHandling.Ignore)]
    public double? AnguloDeFase { get; set; }

    [JsonProperty("antena", NullValueHandling = NullValueHandling.Ignore)]
    public long? Antena { get; set; }

    [JsonProperty("data_leitura", NullValueHandling = NullValueHandling.Ignore)]
    public DateTimeOffset DataLeitura { get; set; }

    [JsonProperty("descricao_antena", NullValueHandling = NullValueHandling.Ignore)]
    public string? DescricaoAntena { get; set; }

    [JsonProperty("epc", NullValueHandling = NullValueHandling.Ignore)]
    public string? Epc { get; set; }

    [JsonProperty("frequencia", NullValueHandling = NullValueHandling.Ignore)]
    public double? Frequencia { get; set; }

    [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
    public long? Id { get; set; }

    [JsonProperty("leitor", NullValueHandling = NullValueHandling.Ignore)]
    public string? Leitor { get; set; }

    [JsonProperty("pc", NullValueHandling = NullValueHandling.Ignore)]
    public string? Pc { get; set; }

    [JsonProperty("potencia_tx", NullValueHandling = NullValueHandling.Ignore)]
    public double? PotenciaTx { get; set; }

    [JsonProperty("rssi", NullValueHandling = NullValueHandling.Ignore)]
    public double? Rssi { get; set; }

    [JsonProperty("tid", NullValueHandling = NullValueHandling.Ignore)]
    public string? Tid { get; set; }

    [JsonProperty("visto_ultima_vez", NullValueHandling = NullValueHandling.Ignore)]
    public string? VistoUltimaVez { get; set; }

    [JsonProperty("data_leitura_unix", NullValueHandling = NullValueHandling.Ignore)]
    public string? DataLeituraUnix { get; set; }

    [JsonProperty("data_hora_servidor", NullValueHandling = NullValueHandling.Ignore)]
    public DateTimeOffset DataHoraServidor { get; set; }

    [JsonProperty("processado", NullValueHandling = NullValueHandling.Ignore)]
    public long? Processado { get; set; }
}

public partial class SmartReaderTagEvent
{
    public static SmartReaderTagEvent FromJson(string json)
    {
        return JsonConvert.DeserializeObject<SmartReaderTagEvent>(json, Converter.Settings);
    }
}

public static class Serialize
{
    public static string ToJson(this SmartReaderTagEvent self)
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

internal class ParseStringConverter : JsonConverter
{
    public static readonly ParseStringConverter Singleton = new();

    public override bool CanConvert(Type t)
    {
        return t == typeof(long) || t == typeof(long?);
    }

    public override object? ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        var value = serializer.Deserialize<string>(reader);
        long l;
        if (long.TryParse(value, out l)) return l;
        throw new Exception("Cannot unmarshal type long");
    }

    public override void WriteJson(JsonWriter writer, object untypedValue, JsonSerializer serializer)
    {
        if (untypedValue == null)
        {
            serializer.Serialize(writer, null);
            return;
        }

        var value = (long)untypedValue;
        serializer.Serialize(writer, value.ToString());
    }
}