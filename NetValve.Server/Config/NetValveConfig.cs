namespace NetValve.Server.Config;

public class NetValveConfig
{
    public string NetworkInterface { get; init; } = "eth0";
    public string IfbDevice { get; init; } = "ifb0";
    public uint UpdateIntervalSeconds { get; init; } = 10;
    public IngestOptions Ingest { get; init; } = new();
}

public class IngestOptions
{
    public string BindAddress { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 8443;
    public bool UseHttps { get; init; } = false;
    public string? CertificatePath { get; init; }
    public string? CertificatePassword { get; init; }
    public int SnapshotTtlSeconds { get; init; } = 20;
    public IngestRegistration[] Servers { get; init; } = [];
}

public class IngestRegistration
{
    public string ServerId { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    public uint? Port { get; init; }
}
