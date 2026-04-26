using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NetValve.Core.Models;
using NetValve.Core.Validation;
using NetValve.Server.Config;

namespace NetValve.Server.Runtime;

internal class SnapshotIngestService
{
    public const long MaxSnapshotPayloadBytes = 256 * 1024;
    public const int MaxPlayersPerSnapshot = 128;

    private readonly IngestAuthService _authService;
    private readonly ILogger<SnapshotIngestService> _logger;

    public SnapshotIngestService(IngestAuthService authService, ILogger<SnapshotIngestService> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    public bool TryAuthorize(HttpContext context, out IngestRegistration registration)
    {
        return _authService.TryValidate(AuthTokenResolver.Resolve(context), out registration);
    }

    public bool TryNormalizeSnapshot(Snapshot snapshot, IngestRegistration registration, out Snapshot normalizedSnapshot,
        out string serverId)
    {
        normalizedSnapshot = null!;
        serverId = string.Empty;
        var playersIn = snapshot.Players ?? [];

        if (!TryResolveSnapshotPort(snapshot.ServerPort, registration.Port, out var effectivePort))
            return false;

        if (playersIn.Count > MaxPlayersPerSnapshot)
        {
            _logger.LogWarning("Rejected snapshot with {PlayerCount} players; max allowed is {MaxPlayersPerSnapshot}",
                playersIn.Count, MaxPlayersPerSnapshot);
            return false;
        }

        var players = new List<PlayerSnapshot>(playersIn.Count);
        foreach (var player in playersIn)
        {
            if (!InputValidator.TryNormalizeIpv4(player.Ip, out var normalizedIp))
            {
                _logger.LogWarning("Ignored player entry with invalid IPv4 address");
                continue;
            }

            players.Add(player with { Ip = normalizedIp });
        }

        if (!TryResolveServerId(registration.ServerId, snapshot.ServerId, effectivePort, out serverId))
        {
            _logger.LogWarning("Rejected snapshot with invalid server identifier");
            return false;
        }

        normalizedSnapshot = snapshot with
        {
            ServerId = serverId,
            ServerPort = effectivePort,
            Players = players
        };

        return true;
    }

    private bool TryResolveSnapshotPort(uint reportedPort, uint? registeredPort, out uint effectivePort)
    {
        effectivePort = reportedPort;

        if (registeredPort.HasValue)
        {
            if (!InputValidator.IsValidPort(registeredPort.Value))
            {
                _logger.LogError("Configured ingest port {Port} is invalid", registeredPort.Value);
                return false;
            }

            if (reportedPort == 0)
            {
                effectivePort = registeredPort.Value;
                return true;
            }

            if (reportedPort != registeredPort.Value)
            {
                _logger.LogWarning("Rejected snapshot for registered port {RegisteredPort}; received {ReportedPort}",
                    registeredPort.Value, reportedPort);
                return false;
            }
        }

        if (!InputValidator.IsValidPort(effectivePort))
        {
            _logger.LogWarning("Rejected snapshot without a valid server port");
            return false;
        }

        return true;
    }

    private static bool TryResolveServerId(string? registeredServerId, string? reportedServerId, uint effectivePort,
        out string serverId)
    {
        serverId = string.Empty;

        if (!string.IsNullOrWhiteSpace(registeredServerId))
        {
            if (!InputValidator.TryNormalizeServerId(registeredServerId, out serverId))
                return false;

            if (string.IsNullOrWhiteSpace(reportedServerId))
                return true;

            return InputValidator.TryNormalizeServerId(reportedServerId, out var normalizedReportedId)
                   && string.Equals(serverId, normalizedReportedId, StringComparison.Ordinal);
        }

        serverId = $"port-{effectivePort}";
        return true;
    }
}
