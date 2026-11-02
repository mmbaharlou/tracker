using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Tracker.Core;

namespace Tracker.Collector.Management;

public sealed record ProjectInfo(
    string Key, string Name, string ServerSecret, string? ReadKeyHash, string[] AllowedOrigins,
    ProjectSettings Settings, bool IsEnabled, bool FromConfig)
{
    public ProjectOptions AsOptions() => new() { Key = Key, Name = Name, ServerSecret = ServerSecret, AllowedOrigins = AllowedOrigins };
}

public sealed record UserInfo(string Username, string PasswordHash, string[] Projects, bool IsAdmin, bool FromConfig)
{
    public bool CanSee(string project) => Projects.Contains("*") || Projects.Contains(project, StringComparer.Ordinal);
}

/// <summary>
/// In-memory snapshot of all projects and dashboard users (appsettings + database).
/// Hot paths read the snapshot without I/O; it refreshes every 30 seconds and after every admin change.
/// </summary>
public sealed class Registry
{
    private readonly IAdminStore _store;
    private readonly CollectorOptions _options;
    private readonly IDataProtector _protector;
    private readonly ILogger<Registry> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile Snapshot _snap = new(new(), new());

    private sealed record Snapshot(Dictionary<string, ProjectInfo> Projects, Dictionary<string, UserInfo> Users);

    public Registry(IAdminStore store, CollectorOptions options, IDataProtectionProvider dp, ILogger<Registry> logger)
    {
        _store = store; _options = options; _logger = logger;
        _protector = dp.CreateProtector("Tracker.ProjectSecrets.v1");
        _snap = Build(new(), new());
    }

    public IReadOnlyCollection<ProjectInfo> Projects => _snap.Projects.Values;
    public IReadOnlyCollection<UserInfo> Users => _snap.Users.Values;

    public ProjectInfo? Project(string? key)
        => key != null && _snap.Projects.TryGetValue(key, out var p) && p.IsEnabled ? p : null;

    public ProjectInfo? ProjectIncludingDisabled(string? key)
        => key != null && _snap.Projects.TryGetValue(key, out var p) ? p : null;

    public UserInfo? User(string? username)
        => username != null && _snap.Users.TryGetValue(username, out var u) ? u : null;

    /// <summary>Finds the project a read-only key belongs to (constant-time compare per project).</summary>
    public ProjectInfo? ProjectByReadKey(string? readKey)
    {
        if (string.IsNullOrEmpty(readKey) || readKey.Length > 200) return null;
        var hash = Encoding.ASCII.GetBytes(HashReadKey(readKey));
        ProjectInfo? found = null;
        foreach (var p in _snap.Projects.Values)
            if (p.ReadKeyHash != null && CryptographicOperations.FixedTimeEquals(hash, Encoding.ASCII.GetBytes(p.ReadKeyHash)))
                found = p;
        return found is { IsEnabled: true } ? found : null;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            var projects = await _store.LoadProjectsAsync(ct);
            var users = await _store.LoadUsersAsync(ct);
            _snap = Build(projects, users);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Keep serving the last good snapshot.
            _logger.LogWarning(ex, "Tracker: could not load projects/users from the database");
        }
        finally { _refreshLock.Release(); }
    }

    private Snapshot Build(List<ProjectRow> dbProjects, List<UserRow> dbUsers)
    {
        var projects = new Dictionary<string, ProjectInfo>(StringComparer.Ordinal);
        foreach (var p in dbProjects)
        {
            string secret = "";
            if (!string.IsNullOrEmpty(p.ServerSecretProtected))
            {
                try { secret = _protector.Unprotect(p.ServerSecretProtected); }
                catch (Exception ex) { _logger.LogError(ex, "Tracker: cannot decrypt secret of project {Key} (Data Protection keys changed?)", p.Key); }
            }
            projects[p.Key] = new ProjectInfo(p.Key, p.Name, secret, p.ReadKeyHash,
                Deserialize<string[]>(p.AllowedOrigins) ?? Array.Empty<string>(),
                Deserialize<ProjectSettings>(p.Settings) ?? new ProjectSettings(), p.IsEnabled, false);
        }
        // appsettings wins over the database for the same key.
        foreach (var p in _options.Projects.Where(p => !string.IsNullOrWhiteSpace(p.Key)))
            projects[p.Key] = new ProjectInfo(p.Key, string.IsNullOrEmpty(p.Name) ? p.Key : p.Name, p.ServerSecret,
                string.IsNullOrEmpty(p.ReadKey) ? null : HashReadKey(p.ReadKey), p.AllowedOrigins,
                p.Settings ?? new ProjectSettings(), true, true);

        var users = new Dictionary<string, UserInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in dbUsers)
            users[u.Username] = new UserInfo(u.Username, u.PasswordHash, Deserialize<string[]>(u.Projects) ?? Array.Empty<string>(), u.IsAdmin, false);
        foreach (var u in _options.DashboardUsers.Where(u => !string.IsNullOrWhiteSpace(u.Username)))
            users[u.Username] = new UserInfo(u.Username, u.PasswordHash, u.Projects, u.IsAdmin, true);

        return new Snapshot(projects, users);
    }

    public string Protect(string secret) => _protector.Protect(secret);

    public static string HashReadKey(string key)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    public static string NewServerSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static string NewReadKey() => "rk_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, TrackerJson.Options); } catch { return null; }
    }
}

public sealed class RegistryRefresher : BackgroundService
{
    private readonly Registry _registry;
    public RegistryRefresher(Registry registry) => _registry = registry;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await _registry.RefreshAsync(ct);
            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch (OperationCanceledException) { return; }
        }
    }
}
