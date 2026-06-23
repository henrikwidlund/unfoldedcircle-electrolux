namespace UnfoldedCircle.Electrolux.Http;

public abstract record LiveStreamEvent(
    [property: JsonPropertyName("applianceId")] string ApplianceId,
    [property: JsonPropertyName("property")] PropertyValueType Property
) : EmptyStreamEvent;

public sealed record LiveStreamEvent<TValue>(
    string ApplianceId,
    PropertyValueType Property,
    [property: JsonPropertyName("value")] TValue Value
) : LiveStreamEvent(ApplianceId, Property);
