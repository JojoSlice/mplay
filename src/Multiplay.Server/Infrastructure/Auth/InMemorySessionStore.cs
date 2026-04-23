using System.Collections.Concurrent;

namespace Multiplay.Server.Infrastructure.Auth;

public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();

    public void Set(string token, SessionInfo info) => _sessions[token] = info;

    public bool TryGet(string token, out SessionInfo? info) =>
        _sessions.TryGetValue(token, out info);

    public void Remove(string token) => _sessions.TryRemove(token, out _);
}
