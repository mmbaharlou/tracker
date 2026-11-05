using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Tracker.Api.Controllers;
using Tracker.Api.Data;
using Tracker.Core;

namespace Tracker.Api.Management;

public sealed record ProjectInfo(
    string Key, string Name, string ServerSecret, string? ReadKeyHash, string[] AllowedOrigins,
    ProjectSettings Settings, bool IsEnabled, bool FromConfig)
{
    public ProjectOptions AsOptions() => new() { Key = Key, Name = Name, ServerSecret = ServerSecret, AllowedOrigins = AllowedOrigins };
}

/// <summary>Loads projects from the database. Replaced by an in-memory store in tests.</summary>
public interface IProjectStore
{
    Task<List<TrackerProject>> LoadAsync(CancellationToken ct);
}

public sealed class EfProjectStore(IServiceScopeFactory scopes) : IProjectStore
{
    public async Task<List<TrackerProject>> LoadAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Projects.AsNoTracking().ToListAsync(ct);
    }
}

/// <summary>
/// In-memory snapshot of all projects (appsettings + database).
/// Ingestion reads the snapshot without I/O; it refreshes every 30 seconds and right after every change in the panel.
/// </summary>
public sealed class Registry : IProjectCatalog
{
    private readonly IProjectStore _store;
    private readonly CollectorOptions _options;
    private readonly IDataProtector _protector;
    private readonly ILogger<Registry> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile Dictionary<string, ProjectInfo> _projects;

    public Registry(IProjectStore store, CollectorOptions options, IDataProtectionProvider dp, ILogger<Registry> logger)
    {
        _store = store; _options = options; _logger = logger;
        _protector = dp.CreateProtector("Tracker.ProjectSecrets.v1");
        _projects = Build([]);
    }

    public IReadOnlyCollection<ProjectInfo> Projects => _projects.Values;

    public ProjectInfo? Project(string? key)
        => key != null && _projects.TryGetValue(key, out var p) && p.IsEnabled ? p : null;

    public ProjectInfo? ProjectIncludingDisabled(string? key)
        => key != null && _projects.TryGetValue(key, out var p) ? p : null;

    public Task<IReadOnlyList<ProjectRef>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ProjectRef>>(_projects.Values.OrderBy(p => p.Name).Select(p => new ProjectRef(p.Key, p.Name, p.IsEnabled)).ToList());

    /// <summary>Finds the project a read-only key belongs to (constant-time compare per project).</summary>
    public ProjectInfo? ProjectByReadKey(string? readKey)
    {
        if (string.IsNullOrEmpty(readKey) || readKey.Length > 200) return null;
        var hash = Encoding.ASCII.GetBytes(HashReadKey(readKey));
        ProjectInfo? found = null;
        foreach (var p in _projects.Values)
            if (p.ReadKeyHash != null && CryptographicOperations.FixedTimeEquals(hash, Encoding.ASCII.GetBytes(p.ReadKeyHash)))
                found = p;
        return found is { IsEnabled: true } ? found : null;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _refreshLock.WaitAsync(ct);
        try { _projects = Build(await _store.LoadAsync(ct)); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Keep serving the last good snapshot.
            _logger.LogWarning(ex, "Tracker: could not load projects from the database");
        }
        finally { _refreshLock.Release(); }
    }

    private Dictionary<string, ProjectInfo> Build(List<TrackerProject> dbProjects)
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
            projects[p.Key] = new ProjectInfo(p.Key, p.Name, secret, p.ReadKeyHash?.Trim(),
                Deserialize<string[]>(p.AllowedOrigins) ?? [],
                Deserialize<ProjectSettings>(p.Settings) ?? new ProjectSettings(), p.IsEnabled, false);
        }
        // appsettings wins over the database for the same key.
        foreach (var p in _options.Projects.Where(p => !string.IsNullOrWhiteSpace(p.Key)))
            projects[p.Key] = new ProjectInfo(p.Key, string.IsNullOrEmpty(p.Name) ? p.Key : p.Name, p.ServerSecret,
                string.IsNullOrEmpty(p.ReadKey) ? null : HashReadKey(p.ReadKey), p.AllowedOrigins,
                p.Settings ?? new ProjectSettings(), true, true);
        return projects;
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

public sealed class RegistryRefresher(Registry registry) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await registry.RefreshAsync(ct);
            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch (OperationCanceledException) { return; }
        }
    }
}
