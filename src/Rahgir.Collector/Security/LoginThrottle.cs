using System.Collections.Concurrent;

namespace Rahgir.Collector.Security;

/// <summary>Locks a username+IP pair for 15 minutes after 5 failed logins, and an IP after 20.</summary>
public sealed class LoginThrottle
{
    private readonly ConcurrentDictionary<string, (int Fails, DateTimeOffset Until)> _state = new();
    private const int MaxPerUser = 5, MaxPerIp = 20;
    private static readonly TimeSpan Lock = TimeSpan.FromMinutes(15);

    public bool IsLocked(string username, string ip)
        => Locked("u:" + username.ToLowerInvariant() + "|" + ip, MaxPerUser) || Locked("ip:" + ip, MaxPerIp);

    public void Fail(string username, string ip)
    {
        Bump("u:" + username.ToLowerInvariant() + "|" + ip);
        Bump("ip:" + ip);
        if (_state.Count > 100_000) _state.Clear(); // memory guard against spraying
    }

    public void Success(string username, string ip) => _state.TryRemove("u:" + username.ToLowerInvariant() + "|" + ip, out _);

    private bool Locked(string key, int max)
        => _state.TryGetValue(key, out var s) && s.Fails >= max && s.Until > DateTimeOffset.UtcNow;

    private void Bump(string key)
        => _state.AddOrUpdate(key, _ => (1, DateTimeOffset.UtcNow + Lock),
            (_, s) => s.Until < DateTimeOffset.UtcNow ? (1, DateTimeOffset.UtcNow + Lock) : (s.Fails + 1, DateTimeOffset.UtcNow + Lock));
}
