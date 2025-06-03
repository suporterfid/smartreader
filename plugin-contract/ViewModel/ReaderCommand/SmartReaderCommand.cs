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

namespace SmartReaderJobs.ViewModel.ReaderCommand;

public partial class SmartReaderCommand
{
    [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
    public string? Status { get; set; }

    [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
    public List<SmartReaderCommandData>? Data { get; set; }
}

public class SmartReaderCommandData
{
    [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? Id { get; set; }

    [JsonProperty("id_leitor", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? IdLeitor { get; set; }

    [JsonProperty("nome_comando", NullValueHandling = NullValueHandling.Ignore)]
    public string? NomeComando { get; set; }

    [JsonProperty("tipo_comando", NullValueHandling = NullValueHandling.Ignore)]
    public string? TipoComando { get; set; }

    [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
    public string? Status { get; set; }

    [JsonProperty("data_comando", NullValueHandling = NullValueHandling.Ignore)]
    public DateTimeOffset? DataComando { get; set; }

    [JsonProperty("ultima_atualizacao_comando", NullValueHandling = NullValueHandling.Ignore)]
    public DateTimeOffset? UltimaAtualizacaoComando { get; set; }

    [JsonProperty("tentativas_inicializacao", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? TentativasInicializacao { get; set; }

    [JsonProperty("motivo_parada", NullValueHandling = NullValueHandling.Ignore)]
    public string? MotivoParada { get; set; }

    [JsonProperty("mensagem_erro", NullValueHandling = NullValueHandling.Ignore)]
    public string? MensagemErro { get; set; }

    [JsonProperty("parametros", NullValueHandling = NullValueHandling.Ignore)]
    public string? Parametros { get; set; }
}

public partial class SmartReaderCommand
{
    public static SmartReaderCommand FromJson(string json)
    {
        return JsonConvert.DeserializeObject<SmartReaderCommand>(json, Converter.Settings);
    }
}

public static class Serialize
{
    public static string ToJson(this SmartReaderCommand self)
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