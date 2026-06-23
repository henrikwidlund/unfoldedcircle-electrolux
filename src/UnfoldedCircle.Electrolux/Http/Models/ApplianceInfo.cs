namespace UnfoldedCircle.Electrolux.Http;

public sealed record ApplianceInfo(
    [property: JsonPropertyName("deviceType")] string DeviceType,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("brand")] string Brand);
