using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NetValve.Core.Models;
using NetValve.Server.Runtime;

namespace NetValve.Server.Hosting;

internal static class IngestEndpointExtensions
{
    public static void MapIngestEndpoints(this WebApplication app)
    {
        app.MapPost("/ingest/snapshot",
            (HttpContext context, Snapshot snapshot, SnapshotStore store, SnapshotIngestService ingestService) =>
            {
                if (!ingestService.TryAuthorize(context, out var registration))
                    return Results.Unauthorized();

                if (!ingestService.TryNormalizeSnapshot(snapshot, registration, out var normalizedSnapshot, out var serverId))
                    return Results.BadRequest();

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

            var ingestService = context.RequestServices.GetRequiredService<SnapshotIngestService>();
            if (!ingestService.TryAuthorize(context, out var registration))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var handler = context.RequestServices.GetRequiredService<SnapshotWebSocketHandler>();
            await handler.HandleAsync(socket, registration, context.RequestAborted);
        });
    }
}
