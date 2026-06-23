namespace UnfoldedCircle.Electrolux.Http;

public sealed record ApplianceInfoResponse(
    [property: JsonPropertyName("applianceInfo")] ApplianceInfo ApplianceInfo);
