using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NetValve.Server.Config;
using NetValve.Server.Runtime;
using Serilog;

namespace NetValve.Server.Hosting;

internal static class NetValveServerApplication
{
    public static WebApplication Create(string[] args)
    {
        var config = ConfigLoader.Load();

        Log.Logger = CreateLogger();

        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseSerilog((_, cfg) => cfg.MinimumLevel.Information().WriteTo.Console());

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = SnapshotIngestService.MaxSnapshotPayloadBytes;
            options.Listen(IPAddress.Parse(config.Ingest.BindAddress.Trim()), config.Ingest.Port, listenOptions =>
            {
                if (config.Ingest.UseHttps && !string.IsNullOrWhiteSpace(config.Ingest.CertificatePath))
                {
                    listenOptions.UseHttps(config.Ingest.CertificatePath, config.Ingest.CertificatePassword);
                }
            });
        });

        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        builder.Services.AddSingleton<SnapshotStore>();
        builder.Services.AddSingleton(new IngestAuthService(config.Ingest.Servers));
        builder.Services.AddSingleton<SnapshotIngestService>();
        builder.Services.AddSingleton<SnapshotWebSocketHandler>();
        builder.Services.AddHostedService<LatencyWorker>();

        var app = builder.Build();
        app.UseWebSockets();
        app.MapIngestEndpoints();

        return app;
    }

    private static ILogger CreateLogger()
    {
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();
    }
}
