namespace UnfoldedCircle.Electrolux.Http;

public sealed record LiveStreamAppliance(
    [property: JsonPropertyName("applianceId")] string ApplianceId,
    [property: JsonPropertyName("properties")] string[] Properties);