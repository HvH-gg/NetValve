using Microsoft.Extensions.Logging;
using NetValve.Core.Models;

namespace NetValve.Core;

public class LatencyController
{
    private const double HysteresisMs = 2.0;

    // Key format: "ip:port" to allow same IP on different servers
    private readonly Dictionary<string, PlayerShapingState> _shapingState = new(StringComparer.Ordinal);
    private readonly TcCommands _tcCommands;
    private readonly ServerConfig _config;
    private readonly ILogger _logger;

    public LatencyController(TcCommands tcCommands, ServerConfig config, ILogger logger)
    {
        _tcCommands = tcCommands;
        _config = config;
        _logger = logger;
    }

    public async Task ProcessServersAsync(ServerInfo[] servers, CancellationToken ct)
    {
        foreach (var server in servers)
        {
            var port = server.Port;
            var configuredTarget = server.TargetPing;

            // Skip servers with no target (disabled/shaping off)
            if (configuredTarget == 0)
            {
                await RemoveServerShapingAsync(port, "target disabled", ct);
                continue;
            }

            var effectiveTarget = ResolveEffectiveTarget(server.Mode, configuredTarget, server.Players);
            if (effectiveTarget == 0)
            {
                await RemoveServerShapingAsync(port, "effective target resolved to 0", ct);
                continue;
            }

            foreach (var player in server.Players)
            {
                var ip = player.Ip;
                if (string.IsNullOrWhiteSpace(ip))
                    continue;

                var perceived = (double)player.Ping;

                // State key includes port to disambiguate same IP on multiple servers
                var stateKey = $"{ip}:{port}";
                if (!_shapingState.TryGetValue(stateKey, out var state))
                {
                    state = new PlayerShapingState();
                    _shapingState[stateKey] = state;
                }

                // Skip if updated recently
                if (Environment.TickCount64 - state.LastUpdate < _config.UpdateIntervalSeconds * 1000)
                    continue;

                // Note: perceived ping already includes netem shaping applied
                var diff = effectiveTarget - perceived;

                // Hysteresis check (2ms)
                if (Math.Abs(diff) <= HysteresisMs)
                {
                    state.LastUpdate = Environment.TickCount64;
                    continue;
                }

                // Direct additive adjustment
                var requestedDelta = (long)Math.Ceiling(diff);
                var newDelay = (int)Math.Clamp((long)state.CurrentDelay + requestedDelta, 0L, int.MaxValue);

                if (newDelay == state.CurrentDelay) 
                    continue;
                
                if (newDelay > 0)
                {
                    if (!await _tcCommands.ApplyShapingAsync(ip, port, newDelay, ct))
                    {
                        _logger.LogWarning("Failed to apply shaping for {IP}:{Port}; state not updated", ip, port);
                        continue;
                    }

                    _logger.LogInformation(
                        "Player {IP}:{Port} - Added {Delay}ms (target: {Target}, mode: {Mode})",
                        ip, port, newDelay, effectiveTarget, server.Mode);
                }
                else
                {
                    if (!await _tcCommands.RemoveShapingAsync(ip, port, ct))
                    {
                        _logger.LogWarning("Failed to remove shaping for {IP}:{Port}; state not updated", ip, port);
                        continue;
                    }

                    _logger.LogInformation(
                        "Player {IP}:{Port} - Shaping removed (target: {Target}, mode: {Mode})",
                        ip, port, effectiveTarget, server.Mode);
                }

                state.CurrentDelay = newDelay;
                state.LastUpdate = Environment.TickCount64;
            }

            // Cleanup: remove shaping for disconnected players on this server
            var activeKeys = new HashSet<string>(server.Players.Select(p => $"{p.Ip}:{port}"), StringComparer.Ordinal);
            foreach (var key in _shapingState.Keys.Where(k => k.EndsWith($":{port}", StringComparison.Ordinal) && !activeKeys.Contains(k)).ToList())
            {
                var idx = key.LastIndexOf(':');
                if (idx <= 0)
                    continue;

                var ip = key[..idx];
                if (!await _tcCommands.RemoveShapingAsync(ip, port, ct))
                {
                    _logger.LogWarning("Failed to clean up shaping for disconnected player {IP}:{Port}", ip, port);
                    continue;
                }

                _shapingState.Remove(key);
                _logger.LogInformation("Player disconnected: {IP}:{Port}", ip, port);
            }
        }
    }

    private async Task RemoveServerShapingAsync(uint port, string reason, CancellationToken ct)
    {
        foreach (var key in _shapingState.Keys.Where(k => k.EndsWith($":{port}", StringComparison.Ordinal)).ToList())
        {
            var idx = key.LastIndexOf(':');
            if (idx <= 0)
                continue;

            var ip = key[..idx];
            if (!await _tcCommands.RemoveShapingAsync(ip, port, ct))
            {
                _logger.LogWarning("Failed to remove shaping for {IP}:{Port} while {Reason}", ip, port, reason);
                continue;
            }

            _shapingState.Remove(key);
            _logger.LogInformation("Player {IP}:{Port} - Shaping removed ({Reason})", ip, port, reason);
        }
    }

    private static double ResolveEffectiveTarget(PingMode mode, uint configuredTarget, PlayerData[] players)
    {
        if (mode != PingMode.Relaxed)
            return configuredTarget;

        var highestPing = players.Length == 0 ? 0u : players.Max(p => p.Ping);
        if (highestPing == 0)
            return 0;

        return Math.Min(configuredTarget, highestPing);
    }
}

public class PlayerShapingState
{
    public int CurrentDelay { get; set; }
    public long LastUpdate { get; set; }
}
