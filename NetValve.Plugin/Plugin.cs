using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Cvars;
using NetValve.Plugin.Config;
using NetValve.Plugin.Core;
using Microsoft.Extensions.Logging;

namespace NetValve.Plugin;

public class Plugin : BasePlugin, IPluginConfig<PluginConfig>
{
    private const string ModeStrict = "strict";
    private const string ModeRelaxed = "relaxed";

    public override string ModuleName => "NetValve™";
    public override string ModuleVersion => "1.0.2";
    public override string ModuleAuthor => "imi-tat0r";
    public PluginConfig Config { get; set; } = new();

    public FakeConVar<float> ExportIntervalCvar = new("netvalve_interval", "Export interval in seconds", 5.0f, ConVarFlags.FCVAR_REPLICATED);

    public FakeConVar<int> OverridePortCvar = new("netvalve_port", "Override server port (0 = auto)", 0, ConVarFlags.FCVAR_REPLICATED);

    public FakeConVar<int> TargetPingCvar = new("netvalve_targetping", "Target ping in ms (0 = off)", 0, ConVarFlags.FCVAR_REPLICATED);

    public FakeConVar<string> PingModeCvar = new("netvalve_pingmode", "Shaping mode: strict|relaxed", ModeStrict, ConVarFlags.FCVAR_REPLICATED);

    private float _lastTickTime;

    private PlayerManager _playerManager = null!;
    private LatencyExporter _exporter = null!;
    private SnapshotSender _sender = null!;
    
    public void OnConfigParsed(PluginConfig config)
    {
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        base.Load(hotReload);

        _playerManager = new PlayerManager();
        _sender = new SnapshotSender(Config, Logger);
        _exporter = new LatencyExporter(_playerManager, _sender, Config, Logger);

        Logger.LogInformation("NetValve™ loading...");
        
        AddCommand("netvalve_export_now", "Export latency snapshot now", OnCmdExportNow);
        AddCommand("netvalve_dump", "Dump known players to console", OnCmdDumpPlayers);
        AddCommand("netvalve_status", "Show plugin status", OnCmdStatus);
        
        RegisterListener<Listeners.OnTick>(OnTick);
        
        Logger.LogInformation("NetValve™ loaded successfully");
    }

    public override void Unload(bool hotReload)
    {
        _sender.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void OnTick()
    {
        var now = Server.CurrentTime;
        if (now - _lastTickTime < GetExportIntervalSeconds()) return;

        _lastTickTime = now;
        _playerManager.OnTick();
        DoExportSafe();
    }

    private void DoExportSafe()
    {
        try
        {
            _exporter.Export(GetOverridePort(), GetTargetPing(), GetPingMode());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Export failed");
        }
    }

    [RequiresPermissions("@css/root")]
    private void OnCmdExportNow(CCSPlayerController? player, CommandInfo info)
    {
        DoExportSafe();
        info.ReplyToCommand("[NetValve] Latency snapshot exported.");
    }

    [RequiresPermissions("@css/root")]
    private void OnCmdDumpPlayers(CCSPlayerController? player, CommandInfo info)
    {
        if (_playerManager.PlayerCount == 0)
        {
            info.ReplyToCommand("[NetValve] No players currently tracked");
            return;
        }

        info.ReplyToCommand("[NetValve] Tracked players:");
        foreach (var state in _playerManager.TrackedPlayers)
        {
            info.ReplyToCommand($"  SteamID={state.SteamId} Name='{state.Name}' IP={state.Ip} Ping={state.PingMs}ms");
        }
    }

    [RequiresPermissions("@css/root")]
    private void OnCmdStatus(CCSPlayerController? player, CommandInfo info)
    {
        var interval = GetExportIntervalSeconds();
        var overridePort = GetOverridePort();
        var port = LatencyExporter.ResolvePort(overridePort);
        var targetPing = GetTargetPing();
        var targetMode = GetPingMode();

        info.ReplyToCommand("[NetValve] Status:");
        info.ReplyToCommand($"  Export interval: {interval}s");
        info.ReplyToCommand($"  Override port: {(overridePort > 0 ? overridePort : "auto")}");
        info.ReplyToCommand($"  Current port: {(port > 0 ? port : "unknown")}");
        info.ReplyToCommand($"  Target ping: {(targetPing > 0 ? $"{targetPing}ms" : "off")}");
        info.ReplyToCommand($"  Ping mode: {targetMode}");
        info.ReplyToCommand($"  Tracked players: {_playerManager.PlayerCount}");
        info.ReplyToCommand($"  Controller URL: {Config.ControllerUrl}");
    }

    private float GetExportIntervalSeconds()
    {
        return ExportIntervalCvar.Value >= 1f ? ExportIntervalCvar.Value : 1f;
    }

    private uint GetOverridePort()
    {
        var port = OverridePortCvar.Value;
        return port is > 0 and <= 65535 ? (uint)port : 0;
    }

    private uint GetTargetPing()
    {
        return TargetPingCvar.Value > 0 ? (uint)TargetPingCvar.Value : 0;
    }

    private string GetPingMode()
    {
        var mode = PingModeCvar.Value.Trim().ToLowerInvariant();
        return mode == ModeRelaxed ? ModeRelaxed : ModeStrict;
    }
}
