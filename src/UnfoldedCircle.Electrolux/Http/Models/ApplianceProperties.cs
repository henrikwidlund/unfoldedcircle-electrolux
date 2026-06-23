namespace UnfoldedCircle.Electrolux.Http;

public sealed record ApplianceProperties([property: JsonPropertyName("reported")] ApplianceReportedProperty Reported);
