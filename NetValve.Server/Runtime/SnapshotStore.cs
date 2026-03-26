using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NetValve.Core.Models;

namespace NetValve.Server.Runtime;

/// <summary>
/// caches the latest snapshot per server id and exposes age-filtered reads.
/// </summary>
public class SnapshotStore
{
    private readonly ConcurrentDictionary<string, StoredSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly ILogger<SnapshotStore> _logger;

    public SnapshotStore(ILogger<SnapshotStore> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// upserts a snapshot keyed by server id.
    /// </summary>
    public void Upsert(string serverId, Snapshot snapshot)
    {
        if (snapshot.ServerPort == 0)
        {
            _logger.LogWarning("Received snapshot with no port for {ServerId}; ignoring", serverId);
            return;
        }

        var stored = new StoredSnapshot
        {
            ServerId = serverId,
            Port = snapshot.ServerPort,
            TargetPing = snapshot.TargetPing ?? snapshot.MinPing ?? 0,
            Mode = ParseMode(snapshot.Mode),
            Players = snapshot.Players.Select(p => new PlayerData
            {
                Ip = p.Ip,
                Ping = p.Ping,
                SteamId = p.SteamId,
                Name = p.Name
            }).ToArray(),
            ReceivedAt = DateTime.UtcNow
        };

        _snapshots.AddOrUpdate(serverId, stored, (_, _) => stored);
    }

    /// <summary>
    /// returns snapshots newer than maxAge.
    /// </summary>
    public IReadOnlyCollection<StoredSnapshot> GetActive(TimeSpan maxAge)
    {
        var now = DateTime.UtcNow;
        var active = new List<StoredSnapshot>();

        foreach (var entry in _snapshots)
        {
            if (now - entry.Value.ReceivedAt <= maxAge)
            {
                active.Add(entry.Value);
                continue;
            }

            _snapshots.TryRemove(entry.Key, out _);
        }

        return active;
    }

    private static PingMode ParseMode(string? mode)
    {
        return string.Equals(mode, "relaxed", StringComparison.OrdinalIgnoreCase)
            ? PingMode.Relaxed
            : PingMode.Strict;
    }
}

/// <summary>
/// flattened snapshot state ready for shaping.
/// </summary>
public class StoredSnapshot
{
    public string ServerId { get; init; } = string.Empty;
    public uint Port { get; init; }
    public uint TargetPing { get; init; }
    public PingMode Mode { get; init; } = PingMode.Strict;
    public PlayerData[] Players { get; init; } = [];
    public DateTime ReceivedAt { get; init; }
}
