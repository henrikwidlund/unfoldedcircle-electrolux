namespace UnfoldedCircle.Electrolux.Http;

public sealed record RefreshTokenRequest(
    [property: JsonPropertyName("refreshToken")] string RefreshToken);
