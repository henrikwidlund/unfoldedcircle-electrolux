
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;

using Theodicean.SourceGenerators;

using UnfoldedCircle.Electrolux.Json;

namespace UnfoldedCircle.Electrolux.Http;

public class ElectroluxClient(IHttpClientFactory httpClientFactoryFactory, IConfiguration configuration)
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactoryFactory;
    private readonly IConfiguration _configuration = configuration;
    private HttpClient HttpClient => field ??= _httpClientFactory.CreateClient();
    private TokenResult? _currentToken;

    private static readonly SemaphoreSlim TokenSemaphore = new(1, 1);

    public async ValueTask AddTokenAsync(TokenResult tokenResult, CancellationToken cancellationToken)
    {
        // we must wait to ensure that the token can be saved
        await TokenSemaphore.WaitAsync(cancellationToken);
        try
        {
            await using var fileStream = File.Open("token.json", FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(fileStream, tokenResult, ElectroluxJsonSerializerContext.Default.TokenResult,
                CancellationToken.None);
        }
        finally
        {
            TokenSemaphore.Release();
        }
    }

    private async ValueTask<TokenResult?> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_currentToken?.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(-5))
            return _currentToken;

        if (await TokenSemaphore.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken))
        {
            if (_currentToken?.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(-5))
                return _currentToken;

            try
            {
                if (!File.Exists("token.json"))
                    return null;

                await using (var fileStream = File.OpenRead("token.json"))
                {
                    var tokenResult = await JsonSerializer.DeserializeAsync(fileStream, ElectroluxJsonSerializerContext.Default.TokenResult, cancellationToken);
                    _currentToken = tokenResult;
                }

                if (_currentToken is null)
                    return null;

                using var request = new HttpRequestMessage(HttpMethod.Post, _configuration["Electrolux:RefreshTokenEndpoint"]);
                request.Headers.Add("x-api-key", _currentToken.ApiKey);
                request.Content = JsonContent.Create(new RefreshTokenRequest(_currentToken.RefreshToken), ElectroluxJsonSerializerContext.Default.RefreshTokenRequest);

                var response = await HttpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var refreshTokenResponse =
                    await response.Content.ReadFromJsonAsync<RefreshTokenResponse>(ElectroluxJsonSerializerContext.Default.RefreshTokenResponse, cancellationToken);

                if (refreshTokenResponse == null || string.IsNullOrEmpty(refreshTokenResponse.AccessToken))
                    throw new InvalidOperationException("Failed to retrieve access token.");

                _currentToken = new TokenResult(
                    refreshTokenResponse.AccessToken,
                    refreshTokenResponse.RefreshToken,
                    DateTimeOffset.UtcNow.AddSeconds(refreshTokenResponse.ExpiresIn),
                    _currentToken.ApiKey);
                await using (var fileStream = File.Open("token.json", FileMode.Create, FileAccess.Write, FileShare.None))
                    await JsonSerializer.SerializeAsync(fileStream, _currentToken, ElectroluxJsonSerializerContext.Default.TokenResult, cancellationToken);

                return _currentToken;
            }
            finally
            {
                TokenSemaphore.Release();
            }
        }

        return null;
    }

    private async Task<Appliance[]?> GetAppliancesAsync(CancellationToken cancellationToken)
    {
        var tokenResult = await GetTokenAsync(cancellationToken);
        if (tokenResult == null)
            throw new InvalidOperationException("No valid token available.");

        using var request = new HttpRequestMessage(HttpMethod.Get, _configuration["Electrolux:AppliancesEndpoint"]);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);
        request.Headers.Add("x-api-key", tokenResult.ApiKey);

        var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Appliance[]>(ElectroluxJsonSerializerContext.Default.ApplianceArray, cancellationToken);
    }

    private async Task<ApplianceInfo?> GetApplianceInfoAsync(string applianceId, CancellationToken cancellationToken)
    {
        var tokenResult = await GetTokenAsync(cancellationToken);
        if (tokenResult == null)
            throw new InvalidOperationException("No valid token available.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_configuration["Electrolux:AppliancesEndpoint"]}/{applianceId}/info");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);
        request.Headers.Add("x-api-key", tokenResult.ApiKey);

        var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ApplianceInfoResponse>(ElectroluxJsonSerializerContext.Default.ApplianceInfoResponse, cancellationToken))?.ApplianceInfo;
    }

    public async IAsyncEnumerable<ApplianceResult> GetAirPurifiersAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var appliances = await GetAppliancesAsync(cancellationToken);
        if (appliances == null)
            yield break;

        foreach (var appliance in appliances)
        {
            var applianceInfo = await GetApplianceInfoAsync(appliance.ApplianceId, cancellationToken);
            if (applianceInfo?.DeviceType.Equals("AIR_PURIFIER", StringComparison.Ordinal) is not true)
                continue;
            yield return new ApplianceResult(appliance.ApplianceId, applianceInfo.Model, applianceInfo.Brand);
        }
    }

    public async Task SendCommandAsync(string applianceId, WorkMode? workMode, sbyte? fanSpeed, CancellationToken cancellationToken)
    {
        if (workMode is not WorkMode.Manual && fanSpeed is not null)
            throw new ArgumentException("Fan speed can only be set in manual mode.", nameof(fanSpeed));

        var tokenResult = await GetTokenAsync(cancellationToken);
        if (tokenResult == null)
            throw new InvalidOperationException("No valid token available.");

        string commandUri = $"{_configuration["Electrolux:AppliancesEndpoint"]}/{applianceId}/command";
        if (workMode is not null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, commandUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);
            request.Headers.Add("x-api-key", tokenResult.ApiKey);
            request.Content = JsonContent.Create(new PurifierCommand(workMode, null), ElectroluxJsonSerializerContext.Default.PurifierCommand);

            var response = await HttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        if (fanSpeed is not null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, commandUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);
            request.Headers.Add("x-api-key", tokenResult.ApiKey);
            request.Content = JsonContent.Create(new PurifierCommand(null, fanSpeed), ElectroluxJsonSerializerContext.Default.PurifierCommand);

            var response = await HttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task<ApplianceState?> GetApplianceStateAsync(string applianceId, CancellationToken cancellationToken)
    {
        var tokenResult = await GetTokenAsync(cancellationToken);
        if (tokenResult == null)
            throw new InvalidOperationException("No valid token available.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_configuration["Electrolux:AppliancesEndpoint"]}/{applianceId}/state");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);
        request.Headers.Add("x-api-key", tokenResult.ApiKey);

        var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ApplianceState>(ElectroluxJsonSerializerContext.Default.ApplianceState, cancellationToken);
    }
}

public sealed record TokenResult(string? AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, string ApiKey);

public sealed record RefreshTokenRequest(
    [property: JsonPropertyName("refreshToken")] string RefreshToken);

public sealed record RefreshTokenResponse(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("expiresIn")] uint ExpiresIn,
    [property: JsonPropertyName("refreshToken")] string RefreshToken);

public sealed record Appliance(
    [property:JsonPropertyName("applianceId")] string ApplianceId);

public sealed record ApplianceInfo(
    [property: JsonPropertyName("deviceType")] string DeviceType,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("brand")] string Brand);

public sealed record ApplianceInfoResponse(
    [property: JsonPropertyName("applianceInfo")] ApplianceInfo ApplianceInfo);

public sealed record ApplianceResult(string ApplianceId, string Model, string Brand);

[EnumJsonConverter<WorkMode>(CaseSensitive = false, PropertyName = "features")]
[JsonConverter(typeof(WorkModeJsonConverter))]
public enum WorkMode
{
    PowerOff = 1,
    Auto = 2,
    Manual = 3
}

// ReSharper disable once RedundantExtendsListEntry For some reason code won't compile without adding this explicit inheritance on this specific converter - all other work
public partial class WorkModeJsonConverter : JsonConverter<WorkMode>;

public sealed record PurifierCommand(
    [property: JsonPropertyName("Workmode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkMode? WorkMode,
    [property: JsonPropertyName("Fanspeed"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] sbyte? FanSpeed);

public sealed record ApplianceState([property: JsonPropertyName("properties")] ApplianceProperties Properties);

public sealed record ApplianceProperties([property: JsonPropertyName("reported")] ApplianceReportedProperty Reported);

// ReSharper disable InconsistentNaming
public sealed record ApplianceReportedProperty(
    [property: JsonPropertyName("Workmode")] WorkMode WorkMode,
    [property: JsonPropertyName("Fanspeed")] sbyte FanSpeed,
    double? TVOC,
    ushort CO2,
    [property: JsonPropertyName("Temp")] short Temperature,
    sbyte Humidity,
    ushort PM1,
    ushort PM2_5,
    ushort PM10,
    ushort ECO2
);
// ReSharper restore InconsistentNaming