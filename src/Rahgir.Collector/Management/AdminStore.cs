using Dapper;
using Microsoft.Data.SqlClient;

namespace Rahgir.Collector.Management;

public sealed class ProjectRow
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ServerSecretProtected { get; set; }
    public string? ReadKeyHash { get; set; }
    public string? AllowedOrigins { get; set; }
    public string? Settings { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class UserRow
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Projects { get; set; } = "[\"*\"]";
    public bool IsAdmin { get; set; }
}

/// <summary>Persistence for centrally managed projects and dashboard users.</summary>
public interface IAdminStore
{
    Task<List<ProjectRow>> LoadProjectsAsync(CancellationToken ct);
    Task<List<UserRow>> LoadUsersAsync(CancellationToken ct);
    Task<bool> InsertProjectAsync(ProjectRow p, CancellationToken ct);
    Task UpdateProjectAsync(ProjectRow p, CancellationToken ct);
    Task DeleteProjectAsync(string key, CancellationToken ct);
    Task<bool> InsertUserAsync(UserRow u, CancellationToken ct);
    Task UpdateUserAsync(UserRow u, CancellationToken ct);
    Task DeleteUserAsync(string username, CancellationToken ct);
}

public sealed class SqlAdminStore : IAdminStore
{
    private readonly string _cs;
    public SqlAdminStore(IConfiguration config)
        => _cs = config.GetConnectionString("RahgirWriter") ?? throw new InvalidOperationException("ConnectionStrings:RahgirWriter is missing.");

    private SqlConnection Open() => new(_cs);

    public async Task<List<ProjectRow>> LoadProjectsAsync(CancellationToken ct)
    {
        await using var c = Open();
        return (await c.QueryAsync<ProjectRow>(new CommandDefinition(
            "SELECT [Key], Name, ServerSecretProtected, ReadKeyHash, AllowedOrigins, Settings, IsEnabled FROM rahgir.Projects", cancellationToken: ct))).ToList();
    }

    public async Task<List<UserRow>> LoadUsersAsync(CancellationToken ct)
    {
        await using var c = Open();
        return (await c.QueryAsync<UserRow>(new CommandDefinition(
            "SELECT Username, PasswordHash, Projects, IsAdmin FROM rahgir.DashboardUsers", cancellationToken: ct))).ToList();
    }

    public async Task<bool> InsertProjectAsync(ProjectRow p, CancellationToken ct)
    {
        await using var c = Open();
        return await c.ExecuteAsync(new CommandDefinition(@"
INSERT INTO rahgir.Projects ([Key], Name, ServerSecretProtected, ReadKeyHash, AllowedOrigins, Settings, IsEnabled)
SELECT @Key, @Name, @ServerSecretProtected, @ReadKeyHash, @AllowedOrigins, @Settings, @IsEnabled
WHERE NOT EXISTS (SELECT 1 FROM rahgir.Projects WHERE [Key] = @Key)", p, cancellationToken: ct)) == 1;
    }

    public async Task UpdateProjectAsync(ProjectRow p, CancellationToken ct)
    {
        await using var c = Open();
        await c.ExecuteAsync(new CommandDefinition(@"
UPDATE rahgir.Projects SET Name = @Name, ServerSecretProtected = @ServerSecretProtected, ReadKeyHash = @ReadKeyHash,
       AllowedOrigins = @AllowedOrigins, Settings = @Settings, IsEnabled = @IsEnabled, UpdatedAt = SYSUTCDATETIME()
WHERE [Key] = @Key", p, cancellationToken: ct));
    }

    public async Task DeleteProjectAsync(string key, CancellationToken ct)
    {
        await using var c = Open();
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM rahgir.Projects WHERE [Key] = @key", new { key }, cancellationToken: ct));
    }

    public async Task<bool> InsertUserAsync(UserRow u, CancellationToken ct)
    {
        await using var c = Open();
        return await c.ExecuteAsync(new CommandDefinition(@"
INSERT INTO rahgir.DashboardUsers (Username, PasswordHash, Projects, IsAdmin)
SELECT @Username, @PasswordHash, @Projects, @IsAdmin
WHERE NOT EXISTS (SELECT 1 FROM rahgir.DashboardUsers WHERE Username = @Username)", u, cancellationToken: ct)) == 1;
    }

    public async Task UpdateUserAsync(UserRow u, CancellationToken ct)
    {
        await using var c = Open();
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE rahgir.DashboardUsers SET PasswordHash = @PasswordHash, Projects = @Projects, IsAdmin = @IsAdmin WHERE Username = @Username",
            u, cancellationToken: ct));
    }

    public async Task DeleteUserAsync(string username, CancellationToken ct)
    {
        await using var c = Open();
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM rahgir.DashboardUsers WHERE Username = @username", new { username }, cancellationToken: ct));
    }
}

/// <summary>Used when no database is configured (tests, first run).</summary>
public sealed class InMemoryAdminStore : IAdminStore
{
    private readonly Dictionary<string, ProjectRow> _p = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UserRow> _u = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public Task<List<ProjectRow>> LoadProjectsAsync(CancellationToken ct) { lock (_lock) return Task.FromResult(_p.Values.Select(Clone).ToList()); }
    public Task<List<UserRow>> LoadUsersAsync(CancellationToken ct) { lock (_lock) return Task.FromResult(_u.Values.Select(Clone).ToList()); }
    public Task<bool> InsertProjectAsync(ProjectRow p, CancellationToken ct) { lock (_lock) return Task.FromResult(_p.TryAdd(p.Key, Clone(p))); }
    public Task UpdateProjectAsync(ProjectRow p, CancellationToken ct) { lock (_lock) if (_p.ContainsKey(p.Key)) _p[p.Key] = Clone(p); return Task.CompletedTask; }
    public Task DeleteProjectAsync(string key, CancellationToken ct) { lock (_lock) _p.Remove(key); return Task.CompletedTask; }
    public Task<bool> InsertUserAsync(UserRow u, CancellationToken ct) { lock (_lock) return Task.FromResult(_u.TryAdd(u.Username, Clone(u))); }
    public Task UpdateUserAsync(UserRow u, CancellationToken ct) { lock (_lock) if (_u.ContainsKey(u.Username)) _u[u.Username] = Clone(u); return Task.CompletedTask; }
    public Task DeleteUserAsync(string username, CancellationToken ct) { lock (_lock) _u.Remove(username); return Task.CompletedTask; }

    private static ProjectRow Clone(ProjectRow p) => new()
    { Key = p.Key, Name = p.Name, ServerSecretProtected = p.ServerSecretProtected, ReadKeyHash = p.ReadKeyHash, AllowedOrigins = p.AllowedOrigins, Settings = p.Settings, IsEnabled = p.IsEnabled };
    private static UserRow Clone(UserRow u) => new() { Username = u.Username, PasswordHash = u.PasswordHash, Projects = u.Projects, IsAdmin = u.IsAdmin };
}
