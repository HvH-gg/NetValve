namespace NetValve.Core.Models;

public record ServerInfo
{
    public uint Port { get; init; }
    public uint TargetPing { get; init; }
    public PingMode Mode { get; init; } = PingMode.Strict;
    public PlayerData[] Players { get; init; } = [];
}

public record PlayerData
{
    public string Ip { get; init; } = string.Empty;
    public uint Ping { get; init; }
    public ulong? SteamId { get; init; }
    public string? Name { get; init; }
}

public enum PingMode
{
    Strict,
    Relaxed
}
