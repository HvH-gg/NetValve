using Microsoft.Extensions.Logging;
using NetValve.Core.Models;

namespace NetValve.Server.Runtime;

public class LiveSnapshotProvider
{
    private readonly SnapshotStore _store;
    private readonly TimeSpan _maxAge;
    private readonly ILogger<LiveSnapshotProvider> _logger;

    public LiveSnapshotProvider(SnapshotStore store, int ttlSeconds, ILogger<LiveSnapshotProvider> logger)
    {
        _store = store;
        _maxAge = TimeSpan.FromSeconds(Math.Max(ttlSeconds, 1));
        _logger = logger;
    }

    public ServerInfo[] GetServers()
    {
        return _store.GetActive(_maxAge)
            .Select(s => new ServerInfo
            {
                Port = s.Port,
                TargetPing = s.TargetPing,
                Mode = s.Mode,
                Players = s.Players
            })
            .ToArray();
    }
}
