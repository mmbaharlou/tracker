using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Tracker.Api.Auth;
using Xunit;

namespace Tracker.Tests;

public class AuthTests : IClassFixture<CollectorFactory>
{
    private readonly CollectorFactory _f;
    public AuthTests(CollectorFactory f) => _f = f;

    private async Task<int> CreateViewerAsync(HttpClient owner, string username)
    {
        var res = await owner.PostAsJsonAsync("/api/admin/users", new { username, password = CollectorFactory.Password, projects = new[] { "shop" } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        // Clear "must change password" by changing it once.
        var u = await _f.LoginAsync(username);
        Assert.Equal(HttpStatusCode.OK, (await u.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = CollectorFactory.Password, newPassword = "Viewer-Passw9rd#Zx1" })).StatusCode);
        return id;
    }

    [Fact]
    public async Task Account_locks_after_repeated_failures_and_owner_can_unlock()
    {
        var owner = await _f.LoginAsync("owner");
        var id = await CreateViewerAsync(owner, "lock-me");

        var client = _f.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "fetch");
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { username = "lock-me", password = "wrong-" + i })).StatusCode);
        // Even the right password is refused while locked.
        Assert.Equal((HttpStatusCode)423, (await client.PostAsJsonAsync("/api/auth/login", new { username = "lock-me", password = "Viewer-Passw9rd#Zx1" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/admin/users/{id}/unlock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { username = "lock-me", password = "Viewer-Passw9rd#Zx1" })).StatusCode);
    }

    [Fact]
    public async Task Logout_and_reset_password_end_sessions()
    {
        var owner = await _f.LoginAsync("owner");
        var id = await CreateViewerAsync(owner, "multi");

        var a = await _f.LoginAsync("multi", "Viewer-Passw9rd#Zx1");
        var b = await _f.LoginAsync("multi", "Viewer-Passw9rd#Zx1");
        var sessions = await a.GetFromJsonAsync<JsonElement>("/api/auth/sessions");
        Assert.True(sessions.GetArrayLength() >= 2);

        Assert.Equal(HttpStatusCode.OK, (await a.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/admin/users/{id}/reset-password", new { password = "Temp-Passw9rd#Reset" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Last_owner_cannot_be_removed_or_demoted_and_admins_cannot_touch_owners()
    {
        var owner = await _f.LoginAsync("owner");
        var list = await owner.GetFromJsonAsync<JsonElement>("/api/admin/users");
        var ownerId = list.GetProperty("users").EnumerateArray().First(u => u.GetProperty("username").GetString() == "owner").GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.DeleteAsync($"/api/admin/users/{ownerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/admin/users/{ownerId}", new { role = "Viewer" })).StatusCode);

        var admin = await _f.LoginAsync("admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/admin/users/{ownerId}/reset-password", new { password = "Hijack-Passw9rd#x1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync($"/api/admin/users/{ownerId}")).StatusCode);
    }

    [Fact]
    public async Task Audit_log_records_logins()
    {
        await _f.LoginAsync("viewer");
        var owner = await _f.LoginAsync("owner");
        var log = await owner.GetFromJsonAsync<JsonElement>("/api/admin/audit?action=login");
        Assert.Contains(log.EnumerateArray(), e => e.GetProperty("action").GetString() == "login.ok" && e.GetProperty("username").GetString() == "viewer");
    }

    [Theory]
    [InlineData("short", false)]
    [InlineData("password123456", false)]
    [InlineData("aaaaaaaaaaaaaaaa", false)]
    [InlineData("Lo9#ngEnough-x", true)]
    [InlineData("four little words here ok", true)]
    public void Password_policy(string password, bool ok) => Assert.Equal(ok, PasswordPolicy.Check(password, "someone") is null);
}
