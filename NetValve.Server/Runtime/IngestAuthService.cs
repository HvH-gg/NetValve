using System.Collections.Concurrent;
using NetValve.Server.Config;

namespace NetValve.Server.Runtime;

/// <summary>
/// validates ingest requests using shared secrets configured per server.
/// </summary>
public class IngestAuthService
{
    private readonly ConcurrentDictionary<string, IngestRegistration> _tokenMap;

    public IngestAuthService(IEnumerable<IngestRegistration> registrations)
    {
        _tokenMap = new ConcurrentDictionary<string, IngestRegistration>(
            registrations
                .Where(r => !string.IsNullOrWhiteSpace(r.Token))
                .Select(r => new KeyValuePair<string, IngestRegistration>(r.Token, r)),
            StringComparer.Ordinal);
    }

    public bool TryValidate(string? token, out IngestRegistration registration)
    {
        if (!string.IsNullOrEmpty(token) && _tokenMap.TryGetValue(token, out registration!))
            return true;

        registration = null!;
        return false;
    }
}
