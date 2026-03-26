// NetValve Plugin
// Copyright (c) 2026 imi-tat0r & HvH.gg
// All rights reserved.

using CounterStrikeSharp.API.Core;

namespace NetValve.Plugin;

public static class Extensions
{
    public static bool IsPlayer(this CCSPlayerController? player)
    {
        return player is
            { PlayerPawn.Value: not null, IsValid: true, IsHLTV: false, IsBot: false, UserId: not null, SteamID: > 0, Connected: PlayerConnectedState.PlayerConnected };
    }
}