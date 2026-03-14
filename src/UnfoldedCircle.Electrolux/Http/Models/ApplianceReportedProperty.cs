namespace UnfoldedCircle.Electrolux.Http;

public sealed record ApplianceReportedProperty(
    [property: JsonPropertyName("Workmode")] WorkMode WorkMode,
    [property: JsonPropertyName("Fanspeed")] sbyte FanSpeed,
    [property: JsonPropertyName("TVOC")] ushort Tvoc,
    [property: JsonPropertyName("CO2")] ushort Co2,
    [property: JsonPropertyName("Temp")] short Temperature,
    [property: JsonPropertyName("Humidity")] sbyte Humidity,
    [property: JsonPropertyName("PM1")] ushort Pm1,
    [property: JsonPropertyName("PM2_5")] ushort Pm25,
    [property: JsonPropertyName("PM10")] ushort Pm10,
    [property: JsonPropertyName("ECO2")] ushort Eco2
);