using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using NetValve.Plugin.Config;
using NetValve.Plugin.Models;
using Microsoft.Extensions.Logging;

namespace NetValve.Plugin.Core;

/// <summary>
/// manages outbound delivery of snapshots via websocket with http fallback and backpressure.
/// </summary>
public sealed class SnapshotSender : IAsyncDisposable
{
    private readonly PluginConfig _config;
    private readonly ILogger _logger;
    private readonly Channel<Snapshot> _queue;
    private readonly CancellationTokenSource _cts = new();
    private readonly HttpClient _httpClient = new();
    private ClientWebSocket? _ws;
    private Task? _pumpTask;
    private readonly JsonSerializerOptions _serializerOptions;

    public SnapshotSender(PluginConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
        // bounded queue with drop-oldest backpressure to keep latency low
        _queue = Channel.CreateBounded<Snapshot>(new BoundedChannelOptions(Math.Max(1, config.QueueSize))
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        _pumpTask = Task.Run(PumpAsync, _cts.Token);
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _ws?.Dispose();
        _httpClient.Dispose();
        return _pumpTask != null ? new ValueTask(_pumpTask) : ValueTask.CompletedTask;
    }

    public void Enqueue(Snapshot snapshot)
    {
        if (!_queue.Writer.TryWrite(snapshot))
        {
            _logger.LogWarning("Snapshot queue full; dropping snapshot");
        }
    }

    private async Task PumpAsync()
    {
        if (string.IsNullOrWhiteSpace(_config.ControllerUrl))
        {
            _logger.LogWarning("ControllerUrl not configured; snapshots will not be sent");
            return;
        }

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (_ws is not { State: WebSocketState.Open })
                {
                    await ConnectWebSocketAsync(_cts.Token);
                }

                if (_ws is not { State: WebSocketState.Open })
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), _cts.Token);
                    continue;
                }

                while (await _queue.Reader.WaitToReadAsync(_cts.Token))
                {
                    while (_queue.Reader.TryRead(out var snapshot))
                    {
                        if (await SendOverWebSocketAsync(snapshot, _cts.Token)) continue;

                        // fallback to http if enabled
                        if (_config.EnableHttpFallback)
                            await SendOverHttpAsync(snapshot, _cts.Token);
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Snapshot sender loop encountered an error");
                // brief pause to avoid tight error loop
                await Task.Delay(TimeSpan.FromSeconds(3), _cts.Token);
            }
        }
    }

    private async Task ConnectWebSocketAsync(CancellationToken ct)
    {
        try
        {
            _ws?.Dispose();
            _ws = new ClientWebSocket();
            if (!string.IsNullOrWhiteSpace(_config.AuthToken))
                _ws.Options.SetRequestHeader("X-Auth-Token", _config.AuthToken);
            if (!string.IsNullOrWhiteSpace(_config.ServerId))
                _ws.Options.SetRequestHeader("X-Server-Id", _config.ServerId);

            var uri = new Uri(_config.ControllerUrl);
            _logger.LogInformation("Connecting to NetValve controller at {Uri}", uri);
            await _ws.ConnectAsync(uri, ct);
            _logger.LogInformation("Connected to NetValve controller at {Uri}", uri);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to controller WS");
            _ws?.Dispose();
            _ws = null;
        }
    }

    private async Task<bool> SendOverWebSocketAsync(Snapshot snapshot, CancellationToken ct)
    {
        if (_ws is not { State: WebSocketState.Open })
            return false;

        try
        {
            // serialize once per send; payloads are small
            var payload = JsonSerializer.Serialize(snapshot, _serializerOptions);
            var bytes = Encoding.UTF8.GetBytes(payload);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WebSocket send failed; will reconnect");
            try
            {
                _ws?.Abort();
            }
            catch
            {
                // ignored
            }
            _ws = null;
            return false;
        }
    }

    private async Task SendOverHttpAsync(Snapshot snapshot, CancellationToken ct)
    {
        try
        {
            var uri = !string.IsNullOrWhiteSpace(_config.HttpFallbackUrl)
                ? _config.HttpFallbackUrl
                : _config.ControllerUrl.Replace("ws", "http", StringComparison.OrdinalIgnoreCase).TrimEnd('/') + "/ingest/snapshot";

            using var req = new HttpRequestMessage(HttpMethod.Post, uri);
            req.Content = new StringContent(JsonSerializer.Serialize(snapshot, _serializerOptions), Encoding.UTF8, "application/json");

            if (!string.IsNullOrWhiteSpace(_config.AuthToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.AuthToken);

            req.Headers.Add("X-Auth-Token", _config.AuthToken);
            if (!string.IsNullOrWhiteSpace(_config.ServerId))
                req.Headers.Add("X-Server-Id", _config.ServerId);

            using var resp = await _httpClient.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("HTTP fallback failed with status {Status}", resp.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HTTP fallback send failed");
        }
    }
}
