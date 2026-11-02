using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tracker.Collector.Dashboard;
using Tracker.Collector.Security;
using Tracker.Core;

namespace Tracker.Collector.Management;

/// <summary>Central management of projects and dashboard users. Admins only.</summary>
public static partial class AdminEndpoints
{
    public sealed record ProjectInput(string? Key, string? Name, string[]? AllowedOrigins, ProjectSettings? Settings, bool? IsEnabled);
    public sealed record UserInput(string? Username, string? Password, string[]? Projects, bool? IsAdmin);

    public static void MapAdmin(this WebApplication app)
    {
        var admin = app.MapGroup("/api/dash/admin").RequireAuthorization()
            .AddEndpointFilter(async (ctx, next) =>
            {
                var r = ctx.HttpContext.RequestServices.GetRequiredService<Registry>();
                return DashboardEndpoints.FindUser(ctx.HttpContext.User, r)?.IsAdmin == true ? await next(ctx) : Results.Forbid();
            });

        admin.MapGet("/projects", (Registry r) => Results.Ok(r.Projects.OrderBy(p => p.Name).Select(p => new
        {
            p.Key, p.Name, p.AllowedOrigins, p.Settings, p.IsEnabled, p.FromConfig,
            HasServerSecret = !string.IsNullOrEmpty(p.ServerSecret),
            HasReadKey = p.ReadKeyHash != null,
        })));

        admin.MapPost("/projects", async (ProjectInput input, Registry r, IAdminStore store, CancellationToken ct) =>
        {
            var key = input.Key?.Trim().ToLowerInvariant() ?? "";
            if (!KeyRegex().IsMatch(key)) return Problem("کلید پروژه فقط حروف کوچک انگلیسی، عدد و خط تیره (۲ تا ۴۰ کاراکتر).");
            if (r.ProjectIncludingDisabled(key) != null) return Problem("پروژه‌ای با این کلید وجود دارد.");
            if (Validate(input) is { } error) return Problem(error);

            var serverSecret = Registry.NewServerSecret();
            var readKey = Registry.NewReadKey();
            var row = new ProjectRow
            {
                Key = key, Name = Name(input.Name, key), ServerSecretProtected = r.Protect(serverSecret),
                ReadKeyHash = Registry.HashReadKey(readKey), AllowedOrigins = Json(CleanOrigins(input.AllowedOrigins)),
                Settings = Json(input.Settings ?? new ProjectSettings()), IsEnabled = input.IsEnabled ?? true,
            };
            if (!await store.InsertProjectAsync(row, ct)) return Problem("پروژه‌ای با این کلید وجود دارد.");
            await r.RefreshAsync(ct);
            // Shown once. Only the hash of the read key and the encrypted server secret are stored.
            return Results.Ok(new { key, serverSecret, readKey });
        });

        admin.MapPut("/projects/{key}", async (string key, ProjectInput input, Registry r, IAdminStore store, CancellationToken ct) =>
        {
            var row = await LoadDbProject(store, key, ct);
            if (row == null) return NotEditable(r, key);
            if (Validate(input) is { } error) return Problem(error);
            row.Name = Name(input.Name, key);
            row.AllowedOrigins = Json(CleanOrigins(input.AllowedOrigins));
            row.Settings = Json(input.Settings ?? new ProjectSettings());
            row.IsEnabled = input.IsEnabled ?? row.IsEnabled;
            await store.UpdateProjectAsync(row, ct);
            await r.RefreshAsync(ct);
            return Results.Ok();
        });

        admin.MapPost("/projects/{key}/rotate", async (string key, string what, Registry r, IAdminStore store, CancellationToken ct) =>
        {
            var row = await LoadDbProject(store, key, ct);
            if (row == null) return NotEditable(r, key);
            string value;
            if (what == "server") { value = Registry.NewServerSecret(); row.ServerSecretProtected = r.Protect(value); }
            else if (what == "read") { value = Registry.NewReadKey(); row.ReadKeyHash = Registry.HashReadKey(value); }
            else return Problem("what باید server یا read باشد.");
            await store.UpdateProjectAsync(row, ct);
            await r.RefreshAsync(ct);
            return Results.Ok(new { key, what, value });
        });

        admin.MapDelete("/projects/{key}", async (string key, Registry r, IAdminStore store, CancellationToken ct) =>
        {
            if (await LoadDbProject(store, key, ct) == null) return NotEditable(r, key);
            await store.DeleteProjectAsync(key, ct); // events stay in the database
            await r.RefreshAsync(ct);
            return Results.Ok();
        });

        admin.MapGet("/users", (Registry r) => Results.Ok(r.Users.OrderBy(u => u.Username).Select(u => new { u.Username, u.Projects, u.IsAdmin, u.FromConfig })));

        admin.MapPost("/users", async (UserInput input, Registry r, IAdminStore store, CancellationToken ct) =>
        {
            var username = input.Username?.Trim() ?? "";
            if (!UserRegex().IsMatch(username)) return Problem("نام کاربری ۳ تا ۶۴ کاراکتر: حروف انگلیسی، عدد، نقطه، خط تیره یا زیرخط.");
            if (r.User(username) != null) return Problem("این نام کاربری وجود دارد.");
            if ((input.Password ?? "").Length < 10) return Problem("رمز عبور حداقل ۱۰ کاراکتر باشد.");
            var row = new UserRow
            {
                Username = username, PasswordHash = PasswordHasher.Hash(input.Password!),
                Projects = Json(CleanProjects(r, input.Projects)), IsAdmin = input.IsAdmin ?? false,
            };
            if (!await store.InsertUserAsync(row, ct)) return Problem("این نام کاربری وجود دارد.");
            await r.RefreshAsync(ct);
            return Results.Ok();
        });

        admin.MapPut("/users/{username}", async (string username, UserInput input, ClaimsPrincipal me, Registry r, IAdminStore store, CancellationToken ct) =>
        {
            var row = (await store.LoadUsersAsync(ct)).FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
            if (row == null) return Problem("این کاربر در فایل تنظیمات تعریف شده یا وجود ندارد؛ از اینجا قابل ویرایش نیست.");
            if (!string.IsNullOrEmpty(input.Password))
            {
                if (input.Password.Length < 10) return Problem("رمز عبور حداقل ۱۰ کاراکتر باشد.");
                row.PasswordHash = PasswordHasher.Hash(input.Password);
            }
            row.Projects = Json(CleanProjects(r, input.Projects));
            var isSelf = string.Equals(me.Identity?.Name, row.Username, StringComparison.OrdinalIgnoreCase);
            if (!isSelf) row.IsAdmin = input.IsAdmin ?? row.IsAdmin; // nobody removes their own admin rights by accident
            await store.UpdateUserAsync(row, ct);
            await r.RefreshAsync(ct);
            return Results.Ok();
        });

        admin.MapDelete("/users/{username}", async (string username, ClaimsPrincipal me, Registry r, IAdminStore store, CancellationToken ct) =>
        {
            if (string.Equals(me.Identity?.Name, username, StringComparison.OrdinalIgnoreCase)) return Problem("نمی‌توانید خودتان را حذف کنید.");
            var u = r.User(username);
            if (u == null || u.FromConfig) return Problem("این کاربر در فایل تنظیمات تعریف شده یا وجود ندارد.");
            await store.DeleteUserAsync(u.Username, ct);
            await r.RefreshAsync(ct);
            return Results.Ok();
        });
    }

    private static async Task<ProjectRow?> LoadDbProject(IAdminStore store, string key, CancellationToken ct)
        => (await store.LoadProjectsAsync(ct)).FirstOrDefault(p => p.Key == key);

    private static IResult NotEditable(Registry r, string key)
        => r.ProjectIncludingDisabled(key)?.FromConfig == true
            ? Problem("این پروژه در فایل تنظیمات تعریف شده؛ آن را همان‌جا ویرایش کنید.")
            : Results.NotFound();

    private static string? Validate(ProjectInput input)
    {
        if ((input.Name ?? "").Length > 100) return "نام پروژه حداکثر ۱۰۰ کاراکتر.";
        foreach (var o in input.AllowedOrigins ?? Array.Empty<string>())
            if (!Uri.TryCreate(o.Trim(), UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http") || u.AbsolutePath != "/" || !string.IsNullOrEmpty(u.Query))
                return $"Origin نامعتبر: {o} (مثال درست: https://verti.ir)";
        var s = input.Settings;
        if (s != null)
        {
            if (s.SampleRate is < 0 or > 1 || s.WebSampleRate is < 0 or > 1) return "نرخ نمونه‌گیری باید بین ۰ و ۱ باشد.";
            if ((s.IgnorePathPrefixes?.Length ?? 0) > 50 || (s.IncludePathPrefixes?.Length ?? 0) > 50) return "حداکثر ۵۰ مسیر.";
            if ((s.IgnorePathPrefixes ?? Array.Empty<string>()).Concat(s.IncludePathPrefixes ?? Array.Empty<string>()).Any(p => !p.StartsWith('/') || p.Length > 200))
                return "مسیرها باید با / شروع شوند.";
        }
        return null;
    }

    private static string[] CleanOrigins(string[]? origins)
        => (origins ?? Array.Empty<string>()).Select(o => new Uri(o.Trim()).GetLeftPart(UriPartial.Authority)).Distinct().ToArray();

    private static string[] CleanProjects(Registry r, string[]? projects)
    {
        var list = (projects ?? Array.Empty<string>()).Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().ToArray();
        return list.Contains("*") ? new[] { "*" } : list.Where(p => r.ProjectIncludingDisabled(p) != null).ToArray();
    }

    private static string Name(string? name, string key) => string.IsNullOrWhiteSpace(name) ? key : name.Trim();
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, TrackerJson.Options);
    private static IResult Problem(string message) => Results.BadRequest(new { error = message });

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,39}$")] private static partial Regex KeyRegex();
    [GeneratedRegex(@"^[A-Za-z0-9._-]{3,64}$")] private static partial Regex UserRegex();
}
