namespace UnfoldedCircle.Electrolux.Http;

public sealed record LiveStreamResponse(
    [property: JsonPropertyName("url")] Uri Url,
    [property: JsonPropertyName("appliances")] LiveStreamAppliance[] Appliances);