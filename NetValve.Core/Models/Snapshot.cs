namespace NetValve.Core.Models;

public sealed record Snapshot
{
    public string? ServerId { get; init; }
    public uint ServerPort { get; init; }
    public uint? TargetPing { get; init; }
    public string? Mode { get; init; }
    // Backward-compatibility for older plugin builds.
    public uint? MinPing { get; init; }
    public List<PlayerSnapshot> Players { get; init; } = [];
    public DateTime ExportTime { get; init; }
}

public sealed record PlayerSnapshot
{
    public string Ip { get; init; } = string.Empty;
    public uint Ping { get; init; }
    public ulong? SteamId { get; init; }
    public string? Name { get; init; }
}
