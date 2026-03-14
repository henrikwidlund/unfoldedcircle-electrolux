namespace UnfoldedCircle.Electrolux.Http;

[JsonConverter(typeof(EmptyStreamEventJsonConverter))]
public record EmptyStreamEvent;