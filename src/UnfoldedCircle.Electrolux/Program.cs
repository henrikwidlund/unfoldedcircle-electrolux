using UnfoldedCircle.Electrolux.Configuration;
using UnfoldedCircle.Electrolux.Http;
using UnfoldedCircle.Server.Configuration;

using ElectroluxWebSocketHandler = UnfoldedCircle.Electrolux.WebSocket.ElectroluxWebSocketHandler;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddHttpClient();
builder.AddUnfoldedCircleServer<ElectroluxWebSocketHandler, ElectroluxConfigurationService, UnfoldedCircleConfigurationItem>();
builder.Services.AddSingleton<ElectroluxClient>();

var app = builder.Build();

app.UseUnfoldedCircleServer<ElectroluxWebSocketHandler, UnfoldedCircleConfigurationItem>();

await app.RunAsync();
