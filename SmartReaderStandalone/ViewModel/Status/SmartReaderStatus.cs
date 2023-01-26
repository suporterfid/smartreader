using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SmartReaderJobs.ViewModel.Status;

public partial class SmartReaderStatus
{
    [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
    public string Status { get; set; }

    [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
    public List<SmartReaderStatusData> Data { get; set; }
}

public class SmartReaderStatusData
{
    [JsonProperty("fabricante", NullValueHandling = NullValueHandling.Ignore)]
    public string Fabricante { get; set; }

    [JsonProperty("firmware", NullValueHandling = NullValueHandling.Ignore)]
    public string Firmware { get; set; }

    [JsonProperty("horario", NullValueHandling = NullValueHandling.Ignore)]
    public DateTimeOffset Horario { get; set; }

    [JsonProperty("hostname", NullValueHandling = NullValueHandling.Ignore)]
    public string Hostname { get; set; }

    [JsonProperty("id_leitor", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(ParseStringConverter))]
    public long? IdLeitor { get; set; }

    [JsonProperty("modelo", NullValueHandling = NullValueHandling.Ignore)]
    public string Modelo { get; set; }

    [JsonProperty("uptime", NullValueHandling = NullValueHandling.Ignore)]
    public long? Uptime { get; set; }

    [JsonProperty("status_leitor", NullValueHandling = NullValueHandling.Ignore)]
    public string StatusLeitor { get; set; }

    [JsonProperty("status_mqtt", NullValueHandling = NullValueHandling.Ignore)]
    public string StatusMqtt { get; set; }

    [JsonProperty("status_http", NullValueHandling = NullValueHandling.Ignore)]
    public string StatusHttp { get; set; }
}

public partial class SmartReaderStatus
{
    public static SmartReaderStatus FromJson(string json)
    {
        return JsonConvert.DeserializeObject<SmartReaderStatus>(json, Converter.Settings);
    }
}

public static class Serialize
{
    public static string ToJson(this SmartReaderStatus self)
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

    public override object ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
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

        var value = (long) untypedValue;
        serializer.Serialize(writer, value.ToString());
    }
}