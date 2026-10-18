using Infra;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddInfra(builder.Configuration);
using var host = builder.Build();
await host.StartAsync();
await host.StopAsync();
