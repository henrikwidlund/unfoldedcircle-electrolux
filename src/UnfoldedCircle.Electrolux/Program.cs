using UnfoldedCircle.Electrolux.Configuration;
using UnfoldedCircle.Electrolux.Http;
using UnfoldedCircle.Electrolux.WebSocket;
using UnfoldedCircle.Server.Configuration;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddHttpClient("ElectroluxClient", static client =>
{
    client.DefaultRequestHeaders.UserAgent.Clear();
    client.DefaultRequestHeaders.UserAgent.ParseAdd("UnfoldedCircle/1.0");
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.AddUnfoldedCircleServer<ElectroluxWebSocketHandler, ElectroluxConfigurationService, UnfoldedCircleConfigurationItem>();
builder.Services.AddSingleton<ElectroluxClient>();

var app = builder.Build();

app.UseUnfoldedCircleServer<ElectroluxWebSocketHandler, UnfoldedCircleConfigurationItem>();

await app.RunAsync();
