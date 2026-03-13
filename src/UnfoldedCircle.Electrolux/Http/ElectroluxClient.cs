using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;

using Theodicean.SourceGenerators;

using UnfoldedCircle.Electrolux.Json;
using UnfoldedCircle.Electrolux.Logging;

namespace UnfoldedCircle.Electrolux.Http;

public class ElectroluxClient(IHttpClientFactory httpClientFactoryFactory, IConfiguration configuration, ILogger<ElectroluxClient> logger)
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactoryFactory;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<ElectroluxClient> _logger = logger;
    private HttpClient HttpClient => field ??= _httpClientFactory.CreateClient("ElectroluxClient");
    private TokenResult? _currentToken;

    private static readonly SemaphoreSlim TokenSemaphore = new(1, 1);

    private const string ApiKeyHeader = "x-api-key";
    private const string NoValidTokenMessage = "No valid token available.";
    private const string ElectroluxAppliancesEndpoint = "Electrolux:AppliancesEndpoint";
    private const string ElectroluxLiveStreamEndpoint = "Electrolux:LiveStreamEndpoint";
    private const string Bearer = nameof(Bearer);

    private string UcConfigHome => field ??= Path.Combine(_configuration["UC_CONFIG_HOME"] ?? string.Empty, "token.json");

    public async ValueTask SetTokenAsync(TokenResult tokenResult, CancellationToken cancellationToken)
    {
        // we must wait to ensure that the token can be saved
        await TokenSemaphore.WaitAsync(cancellationToken);
        try
        {
            await using var fileStream = File.Open(UcConfigHome, FileMode.Create, FileAccess.Write, FileShare.None);
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
                var tokenFilePath = UcConfigHome;
                if (!File.Exists(tokenFilePath))
                    return null;

                await using (var fileStream = File.OpenRead(tokenFilePath))
                {
                    var tokenResult = await JsonSerializer.DeserializeAsync(fileStream, ElectroluxJsonSerializerContext.Default.TokenResult, cancellationToken);
                    _currentToken = tokenResult;
                }

                if (_currentToken is null)
                    return null;

                using var request = new HttpRequestMessage(HttpMethod.Post, _configuration["Electrolux:RefreshTokenEndpoint"]);
                request.Headers.Add(ApiKeyHeader, _currentToken.ApiKey);
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
                await using (var fileStream = File.Open(tokenFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
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
            throw new InvalidOperationException(NoValidTokenMessage);

        using var request = new HttpRequestMessage(HttpMethod.Get, _configuration[ElectroluxAppliancesEndpoint]);
        request.Headers.Authorization = new AuthenticationHeaderValue(Bearer, tokenResult.AccessToken);
        request.Headers.Add(ApiKeyHeader, tokenResult.ApiKey);

        var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<Appliance[]>(ElectroluxJsonSerializerContext.Default.ApplianceArray, cancellationToken);
    }

    private async Task<ApplianceInfo?> GetApplianceInfoAsync(string applianceId, CancellationToken cancellationToken)
    {
        var tokenResult = await GetTokenAsync(cancellationToken);
        if (tokenResult == null)
            throw new InvalidOperationException(NoValidTokenMessage);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_configuration[ElectroluxAppliancesEndpoint]}/{applianceId}/info");
        request.Headers.Authorization = new AuthenticationHeaderValue(Bearer, tokenResult.AccessToken);
        request.Headers.Add(ApiKeyHeader, tokenResult.ApiKey);

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
            throw new InvalidOperationException(NoValidTokenMessage);

        string commandUri = $"{_configuration[ElectroluxAppliancesEndpoint]}/{applianceId}/command";
        if (workMode is not null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, commandUri);
            request.Headers.Authorization = new AuthenticationHeaderValue(Bearer, tokenResult.AccessToken);
            request.Headers.Add(ApiKeyHeader, tokenResult.ApiKey);
            request.Content = JsonContent.Create(new PurifierCommand(workMode, null), ElectroluxJsonSerializerContext.Default.PurifierCommand);

            var response = await HttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        if (fanSpeed is not null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, commandUri);
            request.Headers.Authorization = new AuthenticationHeaderValue(Bearer, tokenResult.AccessToken);
            request.Headers.Add(ApiKeyHeader, tokenResult.ApiKey);
            request.Content = JsonContent.Create(new PurifierCommand(null, fanSpeed), ElectroluxJsonSerializerContext.Default.PurifierCommand);

            var response = await HttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task<ApplianceState?> GetApplianceStateAsync(string applianceId, CancellationToken cancellationToken)
    {
        var tokenResult = await GetTokenAsync(cancellationToken);
        if (tokenResult == null)
            throw new InvalidOperationException(NoValidTokenMessage);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_configuration[ElectroluxAppliancesEndpoint]}/{applianceId}/state");
        request.Headers.Authorization = new AuthenticationHeaderValue(Bearer, tokenResult.AccessToken);
        request.Headers.Add(ApiKeyHeader, tokenResult.ApiKey);

        var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ApplianceState>(ElectroluxJsonSerializerContext.Default.ApplianceState, cancellationToken);
    }

    public async Task<ElectroluxLiveStream?> GetLiveStreamAsync(CancellationToken cancellationToken)
    {
        var tokenResult = await GetTokenAsync(cancellationToken);
        if (tokenResult == null)
            throw new InvalidOperationException(NoValidTokenMessage);

        using var request = new HttpRequestMessage(HttpMethod.Get, _configuration[ElectroluxLiveStreamEndpoint]);
        var authorizationHeader = new AuthenticationHeaderValue(Bearer, tokenResult.AccessToken);
        request.Headers.Authorization = authorizationHeader;
        request.Headers.Add(ApiKeyHeader, tokenResult.ApiKey);

        var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var liveStreamResponse = await response.Content.ReadFromJsonAsync<LiveStreamResponse>(ElectroluxJsonSerializerContext.Default.LiveStreamResponse, cancellationToken);
        if (liveStreamResponse is null)
            return null;

        var httpRequest = new HttpRequestMessage(HttpMethod.Get, liveStreamResponse.Url);
        httpRequest.Headers.Authorization = authorizationHeader;
        httpRequest.Headers.Add(ApiKeyHeader, tokenResult.ApiKey);

        HttpResponseMessage? httpResponse = null;
        try
        {
            httpResponse = await HttpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            httpResponse.EnsureSuccessStatusCode();
            return new ElectroluxLiveStream(httpResponse);
        }
        catch (Exception e)
        {
            _logger.FailureGetLiveStream(e);
            httpResponse?.Dispose();
            return null;
        }
    }

    private static readonly EmptyStreamEvent EmptyStreamEvent = new();

    public static async IAsyncEnumerable<LiveStreamEvent> GetLiveStreamEventsAsync(Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in SseParser.Create<EmptyStreamEvent>(stream, static (type, data) =>
                       {
                           if (!type.Equals("message", StringComparison.OrdinalIgnoreCase))
                               return EmptyStreamEvent;
                           return JsonSerializer.Deserialize<EmptyStreamEvent>(data, ElectroluxJsonSerializerContext.Default.EmptyStreamEvent) ?? EmptyStreamEvent;
                       }).EnumerateAsync(cancellationToken))
        {
            if (item.Data is not LiveStreamEvent data)
                continue;

            yield return data;
        }
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
    [property: JsonPropertyName("applianceId")] string ApplianceId);

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

public sealed record ApplianceReportedProperty(
    [property: JsonPropertyName("Workmode")] WorkMode WorkMode,
    [property: JsonPropertyName("Fanspeed")] sbyte FanSpeed,
    [property: JsonPropertyName("TVOC")] ushort Tvoc,
    [property: JsonPropertyName("CO2")] ushort Co2,
    [property: JsonPropertyName("Temp")] short Temperature,
    [property: JsonPropertyName("Humidity")] sbyte Humidity,
    [property: JsonPropertyName("PM1")] ushort Pm1,
    [property: JsonPropertyName("PM2_5")] ushort Pm25,
    [property: JsonPropertyName("PM10")] ushort Pm10,
    [property: JsonPropertyName("ECO2")] ushort Eco2
);

public sealed record LiveStreamResponse(
    [property: JsonPropertyName("url")] Uri Url,
    [property: JsonPropertyName("appliances")] LiveStreamAppliance[] Appliances);

public sealed record LiveStreamAppliance(
    [property: JsonPropertyName("applianceId")] string ApplianceId,
    [property: JsonPropertyName("properties")] string[] Properties);

[JsonDerivedType(typeof(LiveStreamEvent))]
[JsonDerivedType(typeof(LiveStreamEvent<string>))]
[JsonDerivedType(typeof(LiveStreamEvent<int>))]
[JsonDerivedType(typeof(LiveStreamEvent<bool>))]
[JsonConverter(typeof(EmptyStreamEventJsonConverter))]
public record EmptyStreamEvent;

public record LiveStreamEvent(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("applianceId")] string ApplianceId,
    [property: JsonPropertyName("property")] string Property
): EmptyStreamEvent;

public sealed record LiveStreamEvent<TValue>(
    string UserId,
    string ApplianceId,
    string Property,
    [property: JsonPropertyName("value")] TValue Value
): LiveStreamEvent(UserId, ApplianceId, Property);

internal class EmptyStreamEventJsonConverter : JsonConverter<EmptyStreamEvent>
{
    public override EmptyStreamEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected start of JSON object.");

        string? userId = null;
        string ? applianceId = null;
        string ? property = null;
        int? intValue = null;
        string? stringValue = null;
        bool? boolValue = null;

        while (reader.TokenType != JsonTokenType.EndObject && reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (reader.ValueTextEquals("userId"u8))
            {
                reader.Read();
                userId = reader.GetString();
            }
            else if (reader.ValueTextEquals("applianceId"u8))
            {
                reader.Read();
                applianceId = reader.GetString();
            }
            else if (reader.ValueTextEquals("property"u8))
            {
                reader.Read();
                property = reader.GetString();
            }
            else if (reader.ValueTextEquals("value"))
            {
                reader.Read();
                switch (reader.TokenType)
                {
                    case JsonTokenType.String:
                        stringValue = reader.GetString();
                        break;
                    case JsonTokenType.Number:
                        if (reader.TryGetInt32(out var intResult))
                            intValue = intResult;
                        break;
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                        boolValue = reader.GetBoolean();
                        break;
                }
            }
        }

        userId.ValidateHasValue();
        applianceId.ValidateHasValue();
        property.ValidateHasValue();

        if (intValue.HasValue)
            return new LiveStreamEvent<int>(userId, applianceId, property, intValue.Value);
        if (boolValue.HasValue)
            return new LiveStreamEvent<bool>(userId, applianceId, property, boolValue.Value);
        if (stringValue is not null)
            return new LiveStreamEvent<string>(userId, applianceId, property, stringValue);

        throw new JsonException("value property is missing.");
    }

    public override void Write(Utf8JsonWriter writer, EmptyStreamEvent value, JsonSerializerOptions options) => throw new NotSupportedException();
}

file static class JsonValidationExtensions
{
    // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
    extension([NotNull] string? val)
    {
        public void ValidateHasValue([CallerMemberName]string? memberName = null)
        {
            if (val is null)
                throw new JsonException($"{memberName} should not be null.");
        }
    }
}

public sealed class ElectroluxLiveStream(HttpResponseMessage httpResponseMessage) : IDisposable
{
    private readonly HttpResponseMessage _httpResponseMessage = httpResponseMessage;

    public Task<Stream> GetStreamAsync(CancellationToken cancellationToken) => _httpResponseMessage.Content.ReadAsStreamAsync(cancellationToken);

    public void Dispose() => _httpResponseMessage.Dispose();
}