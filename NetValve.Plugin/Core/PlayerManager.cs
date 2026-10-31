using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using NetValve.Plugin.Models;

namespace NetValve.Plugin.Core;

public class PlayerManager
{
    private readonly Dictionary<ulong, PlayerState> _players = new();
    private readonly NetChannelPing _netChannelPing;
    public IReadOnlyCollection<PlayerState> TrackedPlayers => _players.Values;
    public int PlayerCount => _players.Count;
    public bool NetChannelPingAvailable => _netChannelPing.Available;

    public PlayerManager(NetChannelPing netChannelPing)
    {
        _netChannelPing = netChannelPing;
    }

    public void OnTick()
    {
        var seen = new HashSet<ulong>();
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsPlayer()) continue;

            var state = TryEnsureState(player);
            seen.Add(state.SteamId);
            
            var ip = SafeGetIp(player);
            if (!string.IsNullOrEmpty(ip) && ip != state.Ip)
                state.Ip = ip;
            
            var ping = GetPing(player);
            if (ping > 0)
                state.PingMs = ping;

            var name = player.PlayerName;
            if (!string.IsNullOrEmpty(name) && name != state.Name)
                state.Name = name;
        }

        if (seen.Count == 0)
        {
            _players.Clear();
            return;
        }

        var stale = _players.Keys.Where(id => !seen.Contains(id)).ToList();
        foreach (var id in stale)
            _players.Remove(id);
    }

    public PlayerState TryEnsureState(CCSPlayerController player)
    {
        var steamId = player.SteamID;
        if (steamId == 0) steamId = (ulong)(player.UserId ?? 0);
        
        if (_players.TryGetValue(steamId, out var state)) 
            return state;
        
        state = new PlayerState
        {
            SteamId = steamId,
            Name = player.PlayerName,
            Ip = SafeGetIp(player),
            PingMs = GetPing(player)
        };
        
        _players[steamId] = state;
        return state;
    }

    /// <summary>
    /// net channel latency first (not affected by m_iPing overrides), m_iPing as fallback.
    /// </summary>
    public uint GetPing(CCSPlayerController player)
    {
        return _netChannelPing.GetPing(player) ?? player.Ping;
    }

    public static string SafeGetIp(CCSPlayerController player)
    {
        try
        {
            var ip = player.IpAddress;
            if (string.IsNullOrEmpty(ip)) return "";
            
            var idx = ip.LastIndexOf(':');
            if (idx > 0 && ip.Count(c => c == ':') == 1) 
                ip = ip[..idx];
            
            return ip;
        }
        catch
        {
            return "";
        }
    }
}
