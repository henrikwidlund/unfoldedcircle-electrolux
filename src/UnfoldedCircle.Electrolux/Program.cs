using System.Net;

using Microsoft.Extensions.Http.Resilience;

using UnfoldedCircle.Electrolux.Configuration;
using UnfoldedCircle.Electrolux.Http;
using UnfoldedCircle.Electrolux.WebSocket;
using UnfoldedCircle.Server.Configuration;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddHttpClient("ElectroluxClient", static client =>
{
    client.DefaultRequestHeaders.UserAgent.Clear();
    client.DefaultRequestHeaders.UserAgent.ParseAdd("UnfoldedCircle/1.0");
}).AddStandardResilienceHandler(ConfigureShouldHandle);

builder.Services.AddHttpClient("ElectroluxLiveStreamClient", static client =>
{
    client.DefaultRequestHeaders.UserAgent.Clear();
    client.DefaultRequestHeaders.UserAgent.ParseAdd("UnfoldedCircle/1.0");
}).AddStandardResilienceHandler(static options =>
{
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
    options.CircuitBreaker.SamplingDuration = options.AttemptTimeout.Timeout * 2 + TimeSpan.FromSeconds(1);
    ConfigureShouldHandle(options);
});

builder.AddUnfoldedCircleServer<ElectroluxWebSocketHandler, ElectroluxConfigurationService, UnfoldedCircleGlobalConfiguration, UnfoldedCircleConfigurationItem>(static options =>
{
    options.AdditionalRedactedJsonProperties = [ElectroluxServerConstants.ApiKeyKey, ElectroluxServerConstants.RefreshTokenKey];
});
builder.Services.AddSingleton<ElectroluxClient>();

var app = builder.Build();

app.UseUnfoldedCircleServer<ElectroluxWebSocketHandler, UnfoldedCircleGlobalConfiguration, UnfoldedCircleConfigurationItem>();

await app.RunAsync();
return;

static void ConfigureShouldHandle(HttpStandardResilienceOptions options)
{
    options.Retry.ShouldHandle = static outcome
        => ValueTask.FromResult(outcome.Outcome.Result is
        {
            StatusCode:
            HttpStatusCode.NotAcceptable or
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            >= HttpStatusCode.InternalServerError
        });
}
