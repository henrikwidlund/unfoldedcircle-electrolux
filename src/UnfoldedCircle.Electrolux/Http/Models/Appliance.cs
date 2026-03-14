namespace UnfoldedCircle.Electrolux.Http;

public sealed record Appliance(
    [property: JsonPropertyName("applianceId")] string ApplianceId);