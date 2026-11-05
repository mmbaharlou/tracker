using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tracker.Api.Auth;
using Tracker.Api.Data;
using Tracker.Api.Management;
using Tracker.Core;

namespace Tracker.Api.Controllers;

/// <summary>Central management of the sites that send events. Admins only.</summary>
[ApiController]
[Route("api/admin/projects")]
[Authorize(Policy = Policies.Admin)]
public sealed partial class ProjectsController(AppDbContext db, Registry registry, AuditLog audit) : ControllerBase
{
    public sealed record ProjectInput(string? Key, string? Name, string[]? AllowedOrigins, ProjectSettings? Settings, bool? IsEnabled);

    [HttpGet]
    public IActionResult List() => Ok(registry.Projects.OrderBy(p => p.Name).Select(p => new
    {
        p.Key, p.Name, p.AllowedOrigins, p.Settings, p.IsEnabled, p.FromConfig,
        HasServerSecret = !string.IsNullOrEmpty(p.ServerSecret),
        HasReadKey = p.ReadKeyHash != null,
    }));

    [HttpPost]
    public async Task<IActionResult> Create(ProjectInput input, CancellationToken ct)
    {
        var key = input.Key?.Trim().ToLowerInvariant() ?? "";
        if (!KeyRegex().IsMatch(key)) return this.Fail(400, "کلید پروژه فقط حروف کوچک انگلیسی، عدد و خط تیره (۲ تا ۴۰ کاراکتر).");
        if (registry.ProjectIncludingDisabled(key) != null || await db.Projects.AnyAsync(p => p.Key == key, ct)) return this.Fail(400, "پروژه‌ای با این کلید وجود دارد.");
        if (Validate(input) is { } error) return this.Fail(400, error);

        var serverSecret = Registry.NewServerSecret();
        var readKey = Registry.NewReadKey();
        db.Projects.Add(new TrackerProject
        {
            Key = key, Name = Name(input.Name, key), ServerSecretProtected = registry.Protect(serverSecret),
            ReadKeyHash = Registry.HashReadKey(readKey), AllowedOrigins = Json(CleanOrigins(input.AllowedOrigins)),
            Settings = Json(input.Settings ?? new ProjectSettings()), IsEnabled = input.IsEnabled ?? true,
        });
        await db.SaveChangesAsync(ct);
        await registry.RefreshAsync(ct);
        await audit.WriteAsync("project.create", key);
        // Shown once. Only the encrypted server secret and the hash of the read key are stored.
        return Ok(new { key, serverSecret, readKey });
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> Update(string key, ProjectInput input, CancellationToken ct)
    {
        var row = await db.Projects.FirstOrDefaultAsync(p => p.Key == key, ct);
        if (row == null) return NotEditable(key);
        if (Validate(input) is { } error) return this.Fail(400, error);
        var wasEnabled = row.IsEnabled;
        row.Name = Name(input.Name, key);
        row.AllowedOrigins = Json(CleanOrigins(input.AllowedOrigins));
        row.Settings = Json(input.Settings ?? new ProjectSettings());
        row.IsEnabled = input.IsEnabled ?? row.IsEnabled;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await registry.RefreshAsync(ct);
        await audit.WriteAsync("project.update", key, wasEnabled != row.IsEnabled ? (row.IsEnabled ? "enabled" : "disabled") : null);
        return Ok();
    }

    /// <summary>New server secret or read key. The old one stops working immediately.</summary>
    [HttpPost("{key}/rotate")]
    public async Task<IActionResult> Rotate(string key, string what, CancellationToken ct)
    {
        var row = await db.Projects.FirstOrDefaultAsync(p => p.Key == key, ct);
        if (row == null) return NotEditable(key);
        string value;
        if (what == "server") { value = Registry.NewServerSecret(); row.ServerSecretProtected = registry.Protect(value); }
        else if (what == "read") { value = Registry.NewReadKey(); row.ReadKeyHash = Registry.HashReadKey(value); }
        else return this.Fail(400, "what باید server یا read باشد.");
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await registry.RefreshAsync(ct);
        await audit.WriteAsync("project.rotate", key, what);
        return Ok(new { key, what, value });
    }

    /// <summary>Removes the project from the panel. Its events stay in the database.</summary>
    [HttpDelete("{key}")]
    [Authorize(Policy = Policies.Owner)]
    public async Task<IActionResult> Delete(string key, CancellationToken ct)
    {
        var row = await db.Projects.FirstOrDefaultAsync(p => p.Key == key, ct);
        if (row == null) return NotEditable(key);
        db.Projects.Remove(row);
        await db.AdminUserProjects.Where(x => x.ProjectKey == key).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await registry.RefreshAsync(ct);
        await audit.WriteAsync("project.delete", key);
        return Ok();
    }

    IActionResult NotEditable(string key)
        => registry.ProjectIncludingDisabled(key)?.FromConfig == true
            ? this.Fail(400, "این پروژه در فایل تنظیمات تعریف شده؛ آن را همان‌جا ویرایش کنید.")
            : NotFound();

    static string? Validate(ProjectInput input)
    {
        if ((input.Name ?? "").Length > 100) return "نام پروژه حداکثر ۱۰۰ کاراکتر.";
        foreach (var o in (input.AllowedOrigins ?? []).Where(x => !string.IsNullOrWhiteSpace(x)))
            if (!Uri.TryCreate(o.Trim(), UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http") || u.AbsolutePath != "/" || !string.IsNullOrEmpty(u.Query))
                return $"Origin نامعتبر: {o} (مثال درست: https://shop.ir)";
        var s = input.Settings;
        if (s != null)
        {
            if (s.SampleRate is < 0 or > 1 || s.WebSampleRate is < 0 or > 1) return "نرخ نمونه‌گیری باید بین ۰ و ۱ باشد.";
            if ((s.IgnorePathPrefixes?.Length ?? 0) > 50 || (s.IncludePathPrefixes?.Length ?? 0) > 50) return "حداکثر ۵۰ مسیر.";
            if ((s.IgnorePathPrefixes ?? []).Concat(s.IncludePathPrefixes ?? []).Any(p => !p.StartsWith('/') || p.Length > 200))
                return "مسیرها باید با / شروع شوند.";
        }
        return null;
    }

    static string[] CleanOrigins(string[]? origins)
        => (origins ?? []).Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => new Uri(o.Trim()).GetLeftPart(UriPartial.Authority)).Distinct().ToArray();

    static string Name(string? name, string key) => string.IsNullOrWhiteSpace(name) ? key : name.Trim();
    static string Json<T>(T value) => JsonSerializer.Serialize(value, TrackerJson.Options);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,39}$")] private static partial Regex KeyRegex();
}
