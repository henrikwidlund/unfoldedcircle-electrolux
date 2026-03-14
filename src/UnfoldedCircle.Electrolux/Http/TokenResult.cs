namespace UnfoldedCircle.Electrolux.Http;

public sealed record TokenResult(string? AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, string ApiKey);