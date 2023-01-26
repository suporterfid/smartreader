using Newtonsoft.Json;

namespace SmartReader.Infrastructure.Utils;

internal class EpcJsonConverter : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
        else
        {
            if ((value as Epc) is null)
                throw new JsonSerializationException("Unexpected error - Invalid type.");
            writer.WriteValue(value.ToString());
        }
    }

    public override object ReadJson(
        JsonReader reader,
        Type objectType,
        object existingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType != JsonToken.String)
            throw new JsonSerializationException(string.Format("Unexpected error - Parsing error.", reader.TokenType,
                reader.Value));
        try
        {
            return new Epc(HexHelpers.HexToUshortEnumerable(reader.Value.ToString()));
        }
        catch (Exception ex)
        {
            throw new JsonSerializationException(string.Format("Unexpected error - Unexpected type.", reader.Value),
                ex);
        }
    }

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(Epc);
    }
}