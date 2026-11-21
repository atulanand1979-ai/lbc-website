using Azure.Monitor.OpenTelemetry.Exporter;
using Lbc.Api.Http;
using Lbc.Infrastructure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

// SWA managed functions don't let you set AzureWebJobsStorage, so the data store has its own setting.
var storage = builder.Configuration["TABLES_CONNECTION_STRING"]
              ?? throw new InvalidOperationException("TABLES_CONNECTION_STRING is not configured.");

builder.Services.AddLbc(builder.Configuration, storage);
builder.Services.AddSingleton<ApiHost>();

builder.Build().Run();
