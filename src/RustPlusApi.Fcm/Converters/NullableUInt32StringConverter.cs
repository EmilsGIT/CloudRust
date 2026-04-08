using System.Text.Json;
using System.Text.Json.Serialization;

namespace RustPlusApi.Fcm.Converters;

public sealed class NullableUInt32StringConverter : JsonConverter<uint?>
{
    public override uint? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetUInt32(out var numberValue))
            return numberValue;

        if (reader.TokenType == JsonTokenType.String && uint.TryParse(reader.GetString(), out var stringValue))
            return stringValue;

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, uint? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.ToString());
    }
}