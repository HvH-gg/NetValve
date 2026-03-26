namespace NetValve.Core.Models;

public record ServerConfig
{
    public uint UpdateIntervalSeconds { get; init; } = 10;
    public string NetworkInterface { get; init; } = "eth0";
    public string IfbDevice { get; init; } = "ifb0";
}
