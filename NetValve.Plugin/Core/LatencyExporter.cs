using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using NetValve.Plugin.Config;
using NetValve.Plugin.Models;
using Microsoft.Extensions.Logging;

namespace NetValve.Plugin.Core;

public class LatencyExporter
{
    private readonly PlayerManager _playerManager;
    private readonly ILogger _logger;
    private readonly SnapshotSender _sender;
    private readonly PluginConfig _config;

    /// <summary>
    /// builds and forwards snapshots from in-memory player state.
    /// </summary>
    public LatencyExporter(PlayerManager playerManager, SnapshotSender sender, PluginConfig config, ILogger logger)
    {
        _playerManager = playerManager;
        _logger = logger;
        _sender = sender;
        _config = config;
    }

    public void Export(uint overridePort, uint targetPing, string mode)
    {
        var port = ResolvePort(overridePort);
        if (port <= 0)
        {
            _logger.LogWarning("Cannot resolve server port; skipping export");
            return;
        }
        
        var playersOut = new List<PlayerOut>();

        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsPlayer()) continue;

            var ip = PlayerManager.SafeGetIp(player);
            if (string.IsNullOrEmpty(ip)) continue;

            var state = _playerManager.TryEnsureState(player);
            var pingMs = state.PingMs > 0 ? state.PingMs : player.Ping;

            playersOut.Add(new PlayerOut { Ip = ip, Ping = pingMs });
        }

        var serverId = !string.IsNullOrWhiteSpace(_config.ServerId)
            ? _config.ServerId
            : $"port-{port}";

        var snapshot = new Snapshot
        {
            ServerId = serverId,
            ServerPort = port,
            TargetPing = targetPing,
            Mode = mode,
            Players = playersOut,
            ExportTime = DateTime.UtcNow
        };

        // hand off to async sender
        _sender.Enqueue(snapshot);
    }

    public static uint ResolvePort(uint overridePort)
    {
        if (overridePort > 0) return overridePort;

        try
        {
            var portStr = ConVar.Find("hostport")?.StringValue;
            if (!string.IsNullOrEmpty(portStr) && uint.TryParse(portStr, out var p) && p > 0)
                return p;
        }
        catch
        {
            // ignored
        }

        return 0;
    }
}
