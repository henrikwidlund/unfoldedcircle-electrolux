using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace UnfoldedCircle.Electrolux.Http;

internal sealed class EmptyStreamEventJsonConverter : JsonConverter<EmptyStreamEvent>
{
    private static readonly EmptyStreamEvent EmptyStreamEvent = new();

    public override EmptyStreamEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected start of JSON object.");

        string? applianceId = null;
        PropertyValueType? property = null;
        ReadOnlySpan<byte> valueSpan = default;
        byte[]? rentedBytes = null;

        try
        {
            while (reader.TokenType != JsonTokenType.EndObject && reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                if (reader.ValueTextEquals("applianceId"u8))
                {
                    reader.Read();
                    applianceId = reader.GetString();
                }
                else if (reader.ValueTextEquals("property"u8))
                {
                    reader.Read();
                    property = GetPropertyValueType(reader);
                }
                else if (reader.ValueTextEquals("value"u8))
                {
                    reader.Read();
                    valueSpan = GetValueSpan(reader, out rentedBytes);
                }
            }

            if (string.IsNullOrEmpty(applianceId) || property == null || valueSpan.IsEmpty)
            {
                // this never happens unless we get a ping event, in which case we will return an empty event so it can be ignored
                return EmptyStreamEvent;
            }

            return property switch
            {
                PropertyValueType.WorkMode => new LiveStreamEvent<WorkMode>(applianceId, property.Value, GetWorkMode(valueSpan)),
                PropertyValueType.FanSpeed => new LiveStreamEvent<sbyte>(applianceId, property.Value, sbyte.Parse(valueSpan, NumberFormatInfo.InvariantInfo)),
                PropertyValueType.Tvoc or PropertyValueType.Co2 or PropertyValueType.Pm1 or PropertyValueType.Pm25 or PropertyValueType.Pm10 or PropertyValueType.Eco2
                    => new LiveStreamEvent<ushort>(applianceId, property.Value, ushort.Parse(valueSpan, NumberFormatInfo.InvariantInfo)),
                PropertyValueType.Temperature or PropertyValueType.Humidity
                    => new LiveStreamEvent<sbyte>(applianceId, property.Value, sbyte.Parse(valueSpan, NumberFormatInfo.InvariantInfo)),
                _ => throw new JsonException("Unknown property type") // will not happen, but compiler requires it
            };
        }
        finally
        {
            if (rentedBytes != null)
                ArrayPool<byte>.Shared.Return(rentedBytes);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<byte> GetValueSpan(scoped in Utf8JsonReader reader, out byte[]? rentedBytes)
    {
        if (!reader.HasValueSequence)
        {
            rentedBytes = null;
            return reader.ValueSpan;
        }

        rentedBytes = ArrayPool<byte>.Shared.Rent((int)reader.ValueSequence.Length);
        reader.ValueSequence.CopyTo(rentedBytes);
        return rentedBytes.AsSpan(0, (int)reader.ValueSequence.Length);
    }

    private static PropertyValueType? GetPropertyValueType(in Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("Workmode"))
            return PropertyValueType.WorkMode;
        if (reader.ValueTextEquals("Fanspeed"))
            return PropertyValueType.FanSpeed;
        if (reader.ValueTextEquals("TVOC"))
            return PropertyValueType.Tvoc;
        if (reader.ValueTextEquals("CO2"))
            return PropertyValueType.Co2;
        if (reader.ValueTextEquals("Temp"))
            return PropertyValueType.Temperature;
        if (reader.ValueTextEquals("Humidity"))
            return PropertyValueType.Humidity;
        if (reader.ValueTextEquals("PM1"))
            return PropertyValueType.Pm1;
        if (reader.ValueTextEquals("PM2_5"))
            return PropertyValueType.Pm25;
        if (reader.ValueTextEquals("PM10"))
            return PropertyValueType.Pm10;
        if (reader.ValueTextEquals("ECO2"))
            return PropertyValueType.Eco2;
        return null;
    }

    private static WorkMode GetWorkMode(in ReadOnlySpan<byte> valueSpan)
    {
        if (valueSpan.SequenceEqual("PowerOff"u8))
            return WorkMode.PowerOff;
        if (valueSpan.SequenceEqual("Auto"u8))
            return WorkMode.Auto;
        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (valueSpan.SequenceEqual("Manual"u8))
            return WorkMode.Manual;
        throw new JsonException($"Invalid WorkMode value: {Encoding.UTF8.GetString(valueSpan)}");
    }

    public override void Write(Utf8JsonWriter writer, EmptyStreamEvent value, JsonSerializerOptions options) => throw new NotSupportedException();
}