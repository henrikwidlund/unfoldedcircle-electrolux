namespace UnfoldedCircle.Electrolux.Http;

public sealed record ApplianceState([property: JsonPropertyName("properties")] ApplianceProperties Properties);