using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;

using UnfoldedCircle.Electrolux.Json;
using UnfoldedCircle.Electrolux.Logging;

namespace UnfoldedCircle.Electrolux.Http;

public sealed class ElectroluxClient(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<ElectroluxClient> logger)
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<ElectroluxClient> _logger = logger;
    private HttpClient HttpClient => field ??= _httpClientFactory.CreateClient("ElectroluxClient");
    private HttpClient LiveStreamClient => field ??= _httpClientFactory.CreateClient("ElectroluxLiveStreamClient");
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
            _currentToken = tokenResult;
        }
        finally
        {
            TokenSemaphore.Release();
        }
    }

    public async ValueTask<TokenResult?> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_currentToken?.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
            return _currentToken;

        if (await TokenSemaphore.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken))
        {
            try
            {
                if (_currentToken?.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
                    return _currentToken;

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

        using var response = await LiveStreamClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var liveStreamResponse = await response.Content.ReadFromJsonAsync<LiveStreamResponse>(ElectroluxJsonSerializerContext.Default.LiveStreamResponse, cancellationToken);
        if (liveStreamResponse is null)
            return null;

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, liveStreamResponse.Url);
        httpRequest.Headers.Authorization = authorizationHeader;
        httpRequest.Headers.Add(ApiKeyHeader, tokenResult.ApiKey);

        HttpResponseMessage? httpResponse = null;
        try
        {
            httpResponse = await LiveStreamClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

    public async IAsyncEnumerable<LiveStreamEvent> GetLiveStreamEventsAsync(Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in SseParser.Create(stream, (type, data)
                           =>
                           {
                               if (_logger.IsEnabled(LogLevel.Trace))
                                   _logger.ReceivedLiveStreamEvent(type, Encoding.UTF8.GetString(data));

                               return JsonSerializer.Deserialize<EmptyStreamEvent>(data, ElectroluxJsonSerializerContext.Default.EmptyStreamEvent) ?? EmptyStreamEvent;
                           })
                           .EnumerateAsync(cancellationToken))
        {
            if (item.Data is not LiveStreamEvent data)
                continue;

            yield return data;
        }
    }
}
