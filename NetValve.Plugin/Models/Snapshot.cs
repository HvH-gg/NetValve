namespace NetValve.Plugin.Models;

public sealed class Snapshot
{
    public string? ServerId { get; set; }
    public uint ServerPort { get; set; }
    public uint TargetPing { get; set; }
    public string Mode { get; set; } = "strict";
    public List<PlayerOut> Players { get; set; } = [];
    public DateTime ExportTime { get; set; }
}

public sealed class PlayerOut
{
    public string Ip { get; set; } = "";
    public uint Ping { get; set; }
}
