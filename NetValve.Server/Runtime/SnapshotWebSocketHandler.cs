using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NetValve.Core.Models;
using NetValve.Server.Config;

namespace NetValve.Server.Runtime;

internal class SnapshotWebSocketHandler
{
    private readonly SnapshotIngestService _ingestService;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly SnapshotStore _store;
    private readonly ILogger<SnapshotWebSocketHandler> _logger;

    public SnapshotWebSocketHandler(SnapshotIngestService ingestService, JsonSerializerOptions serializerOptions,
        SnapshotStore store, ILogger<SnapshotWebSocketHandler> logger)
    {
        _ingestService = ingestService;
        _serializerOptions = serializerOptions;
        _store = store;
        _logger = logger;
    }

    public async Task HandleAsync(WebSocket socket, IngestRegistration registration, CancellationToken ct)
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
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct);
                    return;
                }

                if (ms.Length + result.Count > SnapshotIngestService.MaxSnapshotPayloadBytes)
                {
                    _logger.LogWarning("Rejected oversize WebSocket snapshot message");
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
                var snapshot = await JsonSerializer.DeserializeAsync<Snapshot>(ms, _serializerOptions, ct);
                if (snapshot is not null
                    && _ingestService.TryNormalizeSnapshot(snapshot, registration, out var normalizedSnapshot,
                        out var serverId))
                {
                    _store.Upsert(serverId, normalizedSnapshot);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process snapshot message");
            }
        }
    }
}
