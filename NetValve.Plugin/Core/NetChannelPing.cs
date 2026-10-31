using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;

namespace NetValve.Plugin.Core;

/// <summary>
/// reads player latency straight from the engine net channel, bypassing m_iPing (which plugins can overwrite).
/// mirrors what the engine itself does before writing m_iPing: GetNetChannelBySlot(slot)->GetAvgLatency() * 1000.
/// </summary>
public class NetChannelPing
{
    // keys in gamedata/netvalve.json (shipped to addons/counterstrikesharp/gamedata/)
    private const string GetNetChannelBySlotKey = "NetValve_GetNetChannelBySlot";
    private const string GetAvgLatencyKey = "NetValve_INetChannelInfo_GetAvgLatency";

    private readonly ILogger _logger;
    private readonly MemoryFunctionWithReturn<int, IntPtr>? _getNetChannelBySlot;
    private readonly int _avgLatencyIndex;
    private bool _failed;

    public bool Available => _getNetChannelBySlot != null && !_failed;

    public NetChannelPing(ILogger logger)
    {
        _logger = logger;

        try
        {
            _avgLatencyIndex = GameData.GetOffset(GetAvgLatencyKey);
            _getNetChannelBySlot = new MemoryFunctionWithReturn<int, IntPtr>(GameData.GetSignature(GetNetChannelBySlotKey));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Net channel gamedata missing or signature not found; falling back to m_iPing");
        }
    }

    /// <summary>
    /// returns ping in ms from the net channel, or null if unavailable.
    /// </summary>
    public uint? GetPing(CCSPlayerController player)
    {
        if (!Available) return null;

        try
        {
            var netChan = _getNetChannelBySlot!.Invoke(player.Slot);
            if (netChan == IntPtr.Zero) return null;

            var latency = VirtualFunction.Create<IntPtr, float>(netChan, _avgLatencyIndex)(netChan);
            if (float.IsNaN(latency) || latency < 0f || latency > 10f) return null;

            return (uint)MathF.Round(latency * 1000f);
        }
        catch (Exception ex)
        {
            // disable after first failure to avoid log spam / repeated bad calls
            _failed = true;
            _logger.LogError(ex, "Net channel ping read failed; falling back to m_iPing");
            return null;
        }
    }
}
