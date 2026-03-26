using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetValve.Core.Models;
using NetValve.Core.Validation;
using NetValve.Server.Config;
using NetValve.Server.Runtime;
using Serilog;

const long maxSnapshotPayloadBytes = 256 * 1024;
const int maxPlayersPerSnapshot = 128;

var serializerOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};

try
{
    var initialConfig = ConfigLoader.Load();

    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console()
        .CreateLogger();

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog((_, cfg) => cfg.MinimumLevel.Information().WriteTo.Console());

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = maxSnapshotPayloadBytes;
        options.Listen(IPAddress.Parse(initialConfig.Ingest.BindAddress.Trim()), initialConfig.Ingest.Port, listenOptions =>
        {
            if (initialConfig.Ingest.UseHttps && !string.IsNullOrWhiteSpace(initialConfig.Ingest.CertificatePath))
            {
                listenOptions.UseHttps(initialConfig.Ingest.CertificatePath, initialConfig.Ingest.CertificatePassword);
            }
        });
    });

    builder.Services.AddSingleton(initialConfig);
    builder.Services.AddSingleton<SnapshotStore>();
    builder.Services.AddSingleton(new IngestAuthService(initialConfig.Ingest.Servers));
    builder.Services.AddHostedService<LatencyWorker>();

    var app = builder.Build();

    app.UseWebSockets();

    app.MapPost("/ingest/snapshot",
        (HttpContext context, Snapshot snapshot, SnapshotStore store, IngestAuthService auth) =>
        {
            var token = ResolveToken(context);
            if (!auth.TryValidate(token, out var registration))
                return Results.Unauthorized();

            if (!TryNormalizeSnapshot(snapshot, registration, app.Logger, out var normalizedSnapshot, out var serverId))
                return Results.BadRequest();

            // keep only the latest snapshot per server id
            store.Upsert(serverId, normalizedSnapshot);
            return Results.Accepted();
        });

    app.Map("/ws", async context =>
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Expected WebSocket request");
            return;
        }

        var token = ResolveToken(context);
        var store = context.RequestServices.GetRequiredService<SnapshotStore>();
        var auth = context.RequestServices.GetRequiredService<IngestAuthService>();

        if (!auth.TryValidate(token, out var registration))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await HandleWebSocketAsync(socket, registration, store, serializerOptions, context.RequestAborted, app.Logger);
    });

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console()
        .CreateLogger();

    Log.Fatal(ex, "NetValve.Server failed to start");
}
finally
{
    Log.CloseAndFlush();
}

return;

static string? ResolveToken(HttpContext context)
{
    if (context.Request.Headers.TryGetValue("X-Auth-Token", out var headerToken))
    {
        var value = headerToken.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(value))
            return value;
    }

    if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
    {
        var value = authHeader.FirstOrDefault();
        const string bearerPrefix = "Bearer ";
        if (!string.IsNullOrWhiteSpace(value) && value.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            return value[bearerPrefix.Length..].Trim();
    }

    return null;
}

static bool TryNormalizeSnapshot(Snapshot snapshot, IngestRegistration registration, Microsoft.Extensions.Logging.ILogger logger,
    out Snapshot normalizedSnapshot, out string serverId)
{
    normalizedSnapshot = null!;
    serverId = string.Empty;
    var playersIn = snapshot.Players ?? [];

    if (!TryResolveSnapshotPort(snapshot.ServerPort, registration.Port, logger, out var effectivePort))
        return false;

    if (playersIn.Count > maxPlayersPerSnapshot)
    {
        logger.LogWarning("Rejected snapshot with {PlayerCount} players; max allowed is {MaxPlayersPerSnapshot}",
            playersIn.Count, maxPlayersPerSnapshot);
        return false;
    }

    var players = new List<PlayerSnapshot>(playersIn.Count);
    foreach (var player in playersIn)
    {
        if (!InputValidator.TryNormalizeIpv4(player.Ip, out var normalizedIp))
        {
            logger.LogWarning("Ignored player entry with invalid IPv4 address");
            continue;
        }

        players.Add(player with { Ip = normalizedIp });
    }

    if (!TryResolveServerId(registration.ServerId, snapshot.ServerId, effectivePort, out serverId))
    {
        logger.LogWarning("Rejected snapshot with invalid server identifier");
        return false;
    }

    normalizedSnapshot = snapshot with
    {
        ServerId = serverId,
        ServerPort = effectivePort,
        Players = players
    };

    return true;
}

static bool TryResolveSnapshotPort(uint reportedPort, uint? registeredPort, Microsoft.Extensions.Logging.ILogger logger,
    out uint effectivePort)
{
    effectivePort = reportedPort;

    if (registeredPort.HasValue)
    {
        if (!InputValidator.IsValidPort(registeredPort.Value))
        {
            logger.LogError("Configured ingest port {Port} is invalid", registeredPort.Value);
            return false;
        }

        if (reportedPort == 0)
        {
            effectivePort = registeredPort.Value;
            return true;
        }

        if (reportedPort != registeredPort.Value)
        {
            logger.LogWarning("Rejected snapshot for registered port {RegisteredPort}; received {ReportedPort}",
                registeredPort.Value, reportedPort);
            return false;
        }
    }

    if (!InputValidator.IsValidPort(effectivePort))
    {
        logger.LogWarning("Rejected snapshot without a valid server port");
        return false;
    }

    return true;
}

static bool TryResolveServerId(string? registeredServerId, string? reportedServerId, uint effectivePort, out string serverId)
{
    serverId = string.Empty;

    if (!string.IsNullOrWhiteSpace(registeredServerId))
    {
        if (!InputValidator.TryNormalizeServerId(registeredServerId, out serverId))
            return false;

        if (string.IsNullOrWhiteSpace(reportedServerId))
            return true;

        return InputValidator.TryNormalizeServerId(reportedServerId, out var normalizedReportedId)
               && string.Equals(serverId, normalizedReportedId, StringComparison.Ordinal);
    }

    serverId = $"port-{effectivePort}";
    return true;
}

async Task HandleWebSocketAsync(WebSocket socket, IngestRegistration registration, SnapshotStore store,
    JsonSerializerOptions options, CancellationToken ct, Microsoft.Extensions.Logging.ILogger logger)
{
    var buffer = new byte[64 * 1024];

    while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
    {
        WebSocketReceiveResult result;
        using var ms = new MemoryStream();

        do
        {
            result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                // client initiated close; acknowledge and exit
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct);
                return;
            }

            if (ms.Length + result.Count > maxSnapshotPayloadBytes)
            {
                logger.LogWarning("Rejected oversize WebSocket snapshot message");
                await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Snapshot too large", ct);
                return;
            }

            await ms.WriteAsync(buffer.AsMemory(0, result.Count), ct);
        } while (!result.EndOfMessage && !ct.IsCancellationRequested);

        if (result.MessageType != WebSocketMessageType.Text)
            continue;

        ms.Seek(0, SeekOrigin.Begin);
        try
        {
            var snapshot = await JsonSerializer.DeserializeAsync<Snapshot>(ms, options, ct);
            if (snapshot is not null
                && TryNormalizeSnapshot(snapshot, registration, logger, out var normalizedSnapshot, out var serverId))
            {
                store.Upsert(serverId, normalizedSnapshot);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to process snapshot message");
        }
    }
}
