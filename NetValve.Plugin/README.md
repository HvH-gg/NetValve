![Copyright ev0lve Digital](https://img.shields.io/badge/Copyright-ev0lve%20Digital-blue) ![GitHub License](https://img.shields.io/github/license/HvH-gg/NetValve) ![Issues](https://img.shields.io/github/issues/HvH-gg/NetValve) ![Downloads](https://img.shields.io/github/downloads/HvH-gg/NetValve/total) ![Stars](https://img.shields.io/github/stars/HvH-gg/NetValve)

# [HvH.gg](https://hvh.gg) NetValve™ Plugin for Counter-Strike 2

This plugin exports player latency data to the NetValve™ controller via WebSocket/HTTP.

# Features
- Real-time player ping monitoring via WebSocket/HTTP
- Configurable export interval
- Admin commands with `netvalve_` prefix
- Automatic IP address extraction and cleanup
- WebSocket primary transport with HTTP fallback

# Requirements
- [Metamod:Source](https://www.sourcemm.net/downloads.php/?branch=master)
- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)

# Download
Download the latest plugin from [GitHub Releases](https://github.com/HvH-gg/NetValve/releases) - look for the `NetValve.Plugin-X.Y.X.zip` file.

# Install
1. Extract the ZIP file to your CS2 server's `addons/counterstrikesharp/` directory
2. The plugin will be at `addons/counterstrikesharp/plugins/NetValve/`
3. The example config will be at `addons/counterstrikesharp/configs/plugins/NetValve/NetValve.json`
4. Edit the config to set your controller URL, token, and server ID
5. Restart your server or reload the plugin

# Config
The plugin config is located at `addons/counterstrikesharp/configs/plugins/NetValve/NetValve.json`.

## Example Config
```json
{
  "ControllerUrl": "ws://127.0.0.1:8443/ws",
  "AuthToken": "your-auth-token",
  "ServerId": "server-1",
  "HttpFallbackUrl": null,
  "EnableHttpFallback": true,
  "QueueSize": 10,
  "ConfigVersion": 1
}
```

**Note:** Use `ws://` for insecure connections (default controller setup). Use `wss://` only if you've configured SSL/TLS in `/etc/netvalve/config.json`.

Server port is automatically detected from the `hostport` ConVar, or you can override it with the `netvalve_port` fake convar.

# Runtime Settings
Set these in the server console or a cfg file after the plugin is loaded:

| Setting | Description | Usage |
|---------|-------------|-------|
| `netvalve_interval` | Export interval in seconds | `netvalve_interval 10` |
| `netvalve_port` | Override server port (0 = auto) | `netvalve_port 27015` |
| `netvalve_targetping` | Target ping in ms (0 = off) | `netvalve_targetping 20` |
| `netvalve_pingmode` | Shaping mode (`strict` or `relaxed`) | `netvalve_pingmode strict` |

`netvalve_targetping 0` disables shaping. The controller does not apply a fallback target from its own config.

# Commands
All commands require `@css/root` permission:

| Command | Description | Usage |
|---------|-------------|-------|
| `netvalve_export_now` | Force export current player data | `netvalve_export_now` |
| `netvalve_dump` | Show all tracked players | `netvalve_dump` |
| `netvalve_status` | Show plugin status | `netvalve_status` |

# JSON Output Format
```json
{
  "ServerPort": 27015,
  "TargetPing": 50,
  "Mode": "strict",
  "ExportTime": "2026-01-27T12:00:00Z",
  "Players": [
    {
      "Ip": "192.168.1.100",
      "Ping": 45
    }
  ]
}
```

## Ping Modes
- `strict`: All players below `TargetPing` are raised to `TargetPing`.
- `relaxed`: Players are raised to the highest current player ping, capped by `TargetPing`.

Examples with `TargetPing = 20`:
- `strict` and pings `5, 25` -> `5` is raised toward `20`, `25` stays unchanged.
- `relaxed` and pings `5, 13` -> `5` is raised toward `13`.
- `relaxed` and pings `5, 13, 25` -> `5` and `13` are raised toward `20`.

# Integration
The plugin sends snapshots to the NetValve™ controller which applies traffic shaping rules based on the latency data.

# Credits
- [HvH.gg](https://hvh.gg)
- [Metamod:Source](https://www.sourcemm.net/)
- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)

# Buy us a coffee
If you like our work, consider [buying us a coffee](https://hvh.gg/plugins).
