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

namespace SmartReaderJobs.ViewModel.Mqtt;

public partial class SmartReaderMqtt
{
    [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
    public string? Status { get; set; }

    [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
    public List<SmartReaderMqttData>? Data { get; set; }
}

public class SmartReaderMqttData
{
    [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? Id { get; set; }

    [JsonProperty("ativo", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? Ativo { get; set; }

    [JsonProperty("ativar_tls", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? AtivarTls { get; set; }

    [JsonProperty("endereco_broker", NullValueHandling = NullValueHandling.Ignore)]
    public string? EnderecoBroker { get; set; }

    [JsonProperty("porta_broker", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? PortaBroker { get; set; }

    [JsonProperty("clean_session", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? CleanSession { get; set; }

    [JsonProperty("client_id", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public string? ClientId { get; set; }

    [JsonProperty("tamanho_buffer_eventos", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? TamanhoBufferEventos { get; set; }

    [JsonProperty("limite_de_eventos_por_segundo", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? LimiteDeEventosPorSegundo { get; set; }

    [JsonProperty("limite_eventos_pendentes_de_entrega", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? LimiteEventosPendentesDeEntrega { get; set; }

    [JsonProperty("qos", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? Qos { get; set; }

    [JsonProperty("topico_eventos", NullValueHandling = NullValueHandling.Ignore)]
    public string? TopicoEventos { get; set; }

    [JsonProperty("intervalo_keepalive_segundos", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? IntervaloKeepaliveSegundos { get; set; }

    [JsonProperty("usuario", NullValueHandling = NullValueHandling.Ignore)]
    public string? Usuario { get; set; }

    [JsonProperty("senha", NullValueHandling = NullValueHandling.Ignore)]
    public string? Senha { get; set; }

    [JsonProperty("topico_will", NullValueHandling = NullValueHandling.Ignore)]
    public string? TopicoWill { get; set; }

    [JsonProperty("mensagem_will", NullValueHandling = NullValueHandling.Ignore)]
    public string? MensagemWill { get; set; }

    [JsonProperty("qos_will", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? QosWill { get; set; }

    [JsonProperty("sub_smartreader_client_id", NullValueHandling = NullValueHandling.Ignore)]
    public string? ClientIdSmartReader { get; set; }

    [JsonProperty("sub_smartreader_usuario", NullValueHandling = NullValueHandling.Ignore)]
    public string? UsuarioSmartReader { get; set; }

    [JsonProperty("sub_smartreader_senha", NullValueHandling = NullValueHandling.Ignore)]
    public string? SenhaSmartReader { get; set; }
}

public partial class SmartReaderMqtt
{
    public static SmartReaderMqtt FromJson(string json)
    {
        return JsonConvert.DeserializeObject<SmartReaderMqtt>(json, Converter.Settings);
    }
}

public static class Serialize
{
    public static string ToJson(this SmartReaderMqtt self)
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