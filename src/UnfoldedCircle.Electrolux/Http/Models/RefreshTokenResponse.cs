namespace UnfoldedCircle.Electrolux.Http;

public sealed record RefreshTokenResponse(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("expiresIn")] uint ExpiresIn,
    [property: JsonPropertyName("refreshToken")] string RefreshToken);