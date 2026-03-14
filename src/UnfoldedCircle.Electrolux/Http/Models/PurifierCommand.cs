namespace UnfoldedCircle.Electrolux.Http;

public sealed record PurifierCommand(
    [property: JsonPropertyName("Workmode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkMode? WorkMode,
    [property: JsonPropertyName("Fanspeed"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] sbyte? FanSpeed);