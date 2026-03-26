using System.Text.Json;
using NetValve.Core.Validation;

namespace NetValve.Server.Config;

public static class ConfigLoader
{
    public static NetValveConfig Load(string? overridePath = null)
    {
        var path = overridePath ?? Environment.GetEnvironmentVariable("NETVALVE_CONFIG") ?? "/etc/netvalve/config.json";
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"NetValve config not found at {path}");
        }

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var cfg = JsonSerializer.Deserialize<NetValveConfig>(json, options);
        return Validate(cfg ?? new NetValveConfig());
    }

    private static NetValveConfig Validate(NetValveConfig cfg)
    {
        if (!InputValidator.TryNormalizeLinuxDeviceName(cfg.NetworkInterface, out _))
            throw new InvalidOperationException("NetworkInterface must be a valid Linux device name.");

        if (!InputValidator.TryNormalizeLinuxDeviceName(cfg.IfbDevice, out _))
            throw new InvalidOperationException("IfbDevice must be a valid Linux device name.");

        if (cfg.UpdateIntervalSeconds == 0)
            throw new InvalidOperationException("UpdateIntervalSeconds must be greater than zero.");

        if (!InputValidator.TryNormalizeIpAddress(cfg.Ingest.BindAddress, out _))
            throw new InvalidOperationException("Ingest.BindAddress must be a valid IP address.");

        if (!InputValidator.IsValidPort(cfg.Ingest.Port))
            throw new InvalidOperationException("Ingest.Port must be between 1 and 65535.");

        if (cfg.Ingest.SnapshotTtlSeconds <= 0)
            throw new InvalidOperationException("Ingest.SnapshotTtlSeconds must be greater than zero.");

        if (cfg.Ingest.UseHttps && string.IsNullOrWhiteSpace(cfg.Ingest.CertificatePath))
            throw new InvalidOperationException("Ingest.CertificatePath is required when Ingest.UseHttps is enabled.");

        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var registration in cfg.Ingest.Servers)
        {
            if (string.IsNullOrWhiteSpace(registration.Token))
                throw new InvalidOperationException("Each ingest server registration must define a non-empty Token.");

            if (!seenTokens.Add(registration.Token))
                throw new InvalidOperationException("Duplicate ingest tokens are not supported. Use one shared-token registration instead.");

            if (!string.IsNullOrWhiteSpace(registration.ServerId)
                && !InputValidator.TryNormalizeServerId(registration.ServerId, out _))
            {
                throw new InvalidOperationException("Ingest server IDs may only contain letters, numbers, dots, underscores, colons, and hyphens.");
            }

            if (registration.Port.HasValue && !InputValidator.IsValidPort(registration.Port.Value))
                throw new InvalidOperationException("Ingest server ports must be between 1 and 65535.");
        }

        return cfg;
    }
}
