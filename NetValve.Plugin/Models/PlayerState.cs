namespace NetValve.Plugin.Models;

public sealed class PlayerState
{
    public ulong SteamId { get; init; }
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public uint PingMs { get; set; } = 0;
}
