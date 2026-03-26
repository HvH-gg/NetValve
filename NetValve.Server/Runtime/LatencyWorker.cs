using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetValve.Core;
using NetValve.Core.Models;
using NetValve.Server.Config;

namespace NetValve.Server.Runtime;

public class LatencyWorker : BackgroundService
{
    private readonly NetValveConfig _config;
    private readonly ILogger<LatencyWorker> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SnapshotStore _snapshotStore;

    public LatencyWorker(
        NetValveConfig config,
        ILogger<LatencyWorker> logger,
        ILoggerFactory loggerFactory,
        SnapshotStore snapshotStore)
    {
        _config = config;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _snapshotStore = snapshotStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serverConfig = new ServerConfig
        {
            NetworkInterface = _config.NetworkInterface,
            IfbDevice = _config.IfbDevice,
            UpdateIntervalSeconds = _config.UpdateIntervalSeconds
        };

        var tcCommands = new TcCommands(serverConfig, _logger);
        var controller = new LatencyController(tcCommands, serverConfig, _logger);

        var snapshotProvider = new LiveSnapshotProvider(
            _snapshotStore,
            _config.Ingest.SnapshotTtlSeconds,
            _loggerFactory.CreateLogger<LiveSnapshotProvider>());

        _logger.LogInformation("Live snapshot provider ready (TTL={Ttl}s)", _config.Ingest.SnapshotTtlSeconds);
        await tcCommands.EnsureBaseSetupAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var servers = snapshotProvider.GetServers();
                await controller.ProcessServersAsync(servers, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during latency processing");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.UpdateIntervalSeconds), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
