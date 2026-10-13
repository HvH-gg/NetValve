![Copyright ev0lve Digital](https://img.shields.io/badge/Copyright-ev0lve%20Digital-blue) ![GitHub License](https://img.shields.io/github/license/HvH-gg/NetValve) ![Downloads](https://img.shields.io/github/downloads/HvH-gg/NetValve/total) ![Stars](https://img.shields.io/github/stars/HvH-gg/NetValve)

# NetValve™

NetValve™ is a latency equalization tool for CS2 servers. It applies Linux Traffic Control (`tc netem`) delays to low-ping players so matches feel fair and consistent.

## Quick Install

Run:

```bash
curl -fsSL https://netvalve.hvh.gg/install | sudo bash
```

to launch the interactive setup with the latest version.

To pin a specific release, use this:

```bash
NETVALVE_VERSION=1.0.0 curl -fsSL https://netvalve.hvh.gg/install | sudo bash
```

## Features

- Traffic control based on live player ping data
- Strict mode: raise all players below target to the plugin-provided target ping
- Relaxed mode: raise players to the highest current ping, capped by target
- Bidirectional shaping on inbound and outbound traffic via IFB
- WebSocket ingest with HTTP fallback from the CS2 plugin

## Ping Modes

Set runtime behavior using plugin cvars:

- `netvalve_targetping`
- `netvalve_pingmode`

Shaping stays off until the plugin exports a non-zero `netvalve_targetping` value.

Examples with target `20`:

- `strict` with players `5ms` and `25ms` -> `5` is raised toward `20ms`, `25ms` is unchanged
- `relaxed` with players `5ms` and `13ms` -> `5ms` is raised toward `13ms`
- `relaxed` with players `5ms`, `13ms`, and `25ms` -> `5ms` and `13ms` are raised toward `20ms`

## Configuring Servers

### Multiple Servers Setup

The setup script asks how CS2 servers should authenticate:

1. **Shared token** (default, recommended for many servers): one token for all servers. Every plugin uses the same config and is identified by its game port. See [Shared Binary / Auto ServerId Mode](#shared-binary--auto-serverid-mode).
2. **Per-server**: a unique `ServerId` and token for each server.

Leaving a token prompt empty auto-generates one. The script prints the resulting plugin config(s) at the end.

In per-server mode, enter the count of servers you want to connect. For each server, set:

- `ServerId` (unique)
- `AuthToken`
- `Port`

On each CS2 server, configure:

```json
{
  "ControllerUrl": "ws://host-ip:8443/ws",
  "AuthToken": "server-token",
  "ServerId": "server-1"
}
```

Important: use the host machine IP, this might not always be `127.0.0.1`. In BareMetal + Docker setups, plugins run inside containers and must reach the host network address.

### Shared Binary / Auto ServerId Mode

If multiple CS2 servers share the same plugin config, leave `ServerId` empty:

```json
{
  "ControllerUrl": "ws://host-ip:8443/ws",
  "AuthToken": "shared-token",
  "ServerId": ""
}
```

The plugin auto-generates `ServerId` from port (example: `port-27015`).

Example controller config (`/etc/netvalve/config.json`) for one shared token:

```json
{
  "Ingest": {
    "Servers": [
      { "ServerId": "", "Token": "shared-token" }
    ]
  }
}
```

## Plugin Runtime Settings

Set these in the server console or a cfg file after the plugin is loaded:

- `netvalve_targetping 50`
- `netvalve_pingmode strict`

`netvalve_targetping 0` disables shaping for that server.

## Requirements

- Linux kernel modules: `sch_netem`, `sch_htb`, `sch_ingress`, `cls_flower`, `act_mirred`, `ifb`
- `curl`, `tar`, `tc` (usually from `iproute2`, `iproute`, or `iproute-tc`)
- `aspnetcore-runtime-8.0`
- [Metamod:Source](https://www.sourcemm.net/downloads.php/?branch=master)
- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)

## Install .NET 8 Runtime

### Debian / Ubuntu

```bash
. /etc/os-release
wget "https://packages.microsoft.com/config/${ID}/${VERSION_ID}/packages-microsoft-prod.deb" -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y aspnetcore-runtime-8.0
```

For other Linux distributions, use Microsoft’s official .NET install docs:
https://learn.microsoft.com/en-us/dotnet/core/install/linux

## Manual Install

### 1. Controller Service

- Copy `netvalve-X.Y.X-linux-x64.tar.gz` to the server
- Extract it in a working directory on the server
- Run `NETVALVE_VERSION=X.Y.X sudo ./install.sh --local` from that same directory
- Start service: `sudo systemctl start netvalve-controller`

### 2. CS2 Plugin

- Download `NetValve.Plugin-X.Y.X.zip`
- Extract to `addons/counterstrikesharp/`
- Edit `addons/counterstrikesharp/configs/plugins/NetValve/NetValve.json`
- Restart CS2 server or reload the plugin

## Credits

- [HvH.gg](https://hvh.gg)
- [Metamod:Source](https://www.sourcemm.net/)
- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)
