using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Tracker.AspNetCore;
using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

public class CentralManagementTests : IClassFixture<CollectorFactory>
{
    private readonly CollectorFactory _f;
    public CentralManagementTests(CollectorFactory f) => _f = f;

    private async Task<HttpClient> LoginAsync(string user)
    {
        var client = _f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/dash/login") { Content = JsonContent.Create(new { username = user, password = CollectorFactory.Password }) };
        req.Headers.Add("X-Tracker-Csrf", "1");
        (await client.SendAsync(req)).EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("X-Tracker-Csrf", "1");
        return client;
    }

    private async Task<(string key, string secret, string readKey)> CreateProjectAsync(HttpClient admin, string key, object? settings = null)
    {
        var res = await admin.PostAsJsonAsync("/api/dash/admin/projects", new { key, name = "ورتی", allowedOrigins = new[] { "https://verti.test/" }, settings });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("key").GetString()!, body.GetProperty("serverSecret").GetString()!, body.GetProperty("readKey").GetString()!);
    }

    private sealed class FactoryClient : IHttpClientFactory
    {
        private readonly CollectorFactory _f;
        public FactoryClient(CollectorFactory f) => _f = f;
        public HttpClient CreateClient(string name) => _f.CreateClient();
    }

    [Fact]
    public async Task Project_created_in_dashboard_accepts_signed_events_and_browser_events_from_its_origin()
    {
        var admin = await LoginAsync("admin");
        var (key, secret, readKey) = await CreateProjectAsync(admin, "verti");
        Assert.StartsWith("rk_", readKey);
        Assert.Equal(64, secret.Length);

        var options = new TrackerOptions { ProjectKey = key, ServerSecret = secret, CollectorUrl = "http://localhost" };
        var sender = new BatchSender(new EventQueue(options), options, new FactoryClient(_f), NullLogger<BatchSender>.Instance);
        await sender.SendAsync(new List<TrackEvent> { new() { Type = "api", UserId = "verti-user", Route = "api/x" } }, CancellationToken.None);
        Assert.Contains(await _f.Store.WaitAsync(1), e => e.ProjectKey == "verti" && e.UserId == "verti-user");

        var client = _f.CreateClient();
        var web = new HttpRequestMessage(HttpMethod.Post, "/v1/batch?k=verti")
        { Content = new StringContent(JsonSerializer.Serialize(new { events = new[] { new { type = "pageview", anonId = "verti-anon", path = "/" } } }), Encoding.UTF8, "text/plain") };
        web.Headers.Add("Origin", "https://verti.test");
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(web)).StatusCode);

        // The listing never exposes secrets.
        var list = await admin.GetStringAsync("/api/dash/admin/projects");
        Assert.DoesNotContain(secret, list);
        Assert.DoesNotContain(readKey, list);
    }

    [Fact]
    public async Task Settings_changed_centrally_reach_the_backend_sdk_and_the_browser()
    {
        var admin = await LoginAsync("admin");
        var (key, secret, _) = await CreateProjectAsync(admin, "central-1");

        var put = await admin.PutAsJsonAsync($"/api/dash/admin/projects/{key}", new
        {
            name = "Central", allowedOrigins = new[] { "https://verti.test" }, isEnabled = true,
            settings = new { enabled = false, sampleRate = 0.25, webSampleRate = 0.5, autocapture = false, ignorePathPrefixes = new[] { "/api/internal" } },
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        // Backend SDK poll (signed).
        var options = new TrackerOptions { ProjectKey = key, ServerSecret = secret, CollectorUrl = "http://localhost" };
        var effective = new EffectiveSettings(options);
        var poller = new RemoteSettingsPoller(options, effective, new FactoryClient(_f), NullLogger<RemoteSettingsPoller>.Instance);
        effective.Apply(await poller.FetchAsync(CancellationToken.None));
        Assert.False(effective.Enabled);
        Assert.Equal(0.25, effective.SampleRate);
        Assert.Contains("/api/internal", effective.IgnorePathPrefixes);
        Assert.Contains("/health", effective.IgnorePathPrefixes); // local list still applies

        // Wrong secret cannot read settings.
        var bad = new TrackerOptions { ProjectKey = key, ServerSecret = new string('x', 64), CollectorUrl = "http://localhost" };
        var badPoller = new RemoteSettingsPoller(bad, new EffectiveSettings(bad), new FactoryClient(_f), NullLogger<RemoteSettingsPoller>.Instance);
        await Assert.ThrowsAsync<HttpRequestException>(() => badPoller.FetchAsync(CancellationToken.None));

        // Browser config: only public switches.
        var web = await _f.CreateClient().GetFromJsonAsync<JsonElement>($"/v1/config?k={key}");
        Assert.False(web.GetProperty("enabled").GetBoolean());
        Assert.Equal(0.5, web.GetProperty("sampleRate").GetDouble());
        Assert.False(web.TryGetProperty("ignorePathPrefixes", out _));
    }

    [Fact]
    public async Task Read_key_is_required_and_scoped()
    {
        var admin = await LoginAsync("admin");
        var (_, _, readKey) = await CreateProjectAsync(admin, "read-1");
        var client = _f.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/read/events")).StatusCode);
        var wrong = new HttpRequestMessage(HttpMethod.Get, "/v1/read/events");
        wrong.Headers.Add("Authorization", "Bearer rk_wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(wrong)).StatusCode);

        // A valid key passes auth (the test host has no SQL Server, so the query itself fails afterwards).
        var ok = new HttpRequestMessage(HttpMethod.Get, "/v1/read/events");
        ok.Headers.Add("Authorization", "Bearer " + readKey);
        var res = await client.SendAsync(ok);
        Assert.NotEqual(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Rotating_the_server_secret_invalidates_the_old_one()
    {
        var admin = await LoginAsync("admin");
        var (key, oldSecret, _) = await CreateProjectAsync(admin, "rotate-1");
        var rot = await admin.PostAsync($"/api/dash/admin/projects/{key}/rotate?what=server", null);
        var newSecret = (await rot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetString()!;
        Assert.NotEqual(oldSecret, newSecret);

        async Task<HttpStatusCode> Send(string secret)
        {
            var body = Encoding.UTF8.GetBytes("{\"events\":[]}");
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var req = new HttpRequestMessage(HttpMethod.Post, "/v1/server/batch") { Content = new ByteArrayContent(body) };
            req.Headers.Add(TrackerHeaders.Project, key);
            req.Headers.Add(TrackerHeaders.Timestamp, ts.ToString());
            req.Headers.Add(TrackerHeaders.Signature, BatchSigner.Sign(secret, ts, body));
            return (await _f.CreateClient().SendAsync(req)).StatusCode;
        }
        Assert.Equal(HttpStatusCode.Unauthorized, await Send(oldSecret));
        Assert.Equal(HttpStatusCode.Accepted, await Send(newSecret));
    }

    [Fact]
    public async Task Disabled_project_rejects_data()
    {
        var admin = await LoginAsync("admin");
        var (key, _, _) = await CreateProjectAsync(admin, "off-1");
        await admin.PutAsJsonAsync($"/api/dash/admin/projects/{key}", new { name = "off", allowedOrigins = Array.Empty<string>(), isEnabled = false, settings = new { enabled = true } });
        Assert.Equal(HttpStatusCode.Forbidden, (await _f.CreateClient().GetAsync($"/v1/config?k={key}")).StatusCode);
    }

    [Fact]
    public async Task Only_admins_manage_and_input_is_validated()
    {
        var viewer = await LoginAsync("viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/dash/admin/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/dash/admin/projects", new { key = "hack" })).StatusCode);

        var admin = await LoginAsync("admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/dash/admin/projects", new { key = "Bad Key!" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/dash/admin/projects", new { key = "ok-key", allowedOrigins = new[] { "javascript:alert(1)" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/dash/admin/projects", new { key = "ok-key2", settings = new { enabled = true, sampleRate = 3 } })).StatusCode);
        // Projects from appsettings are read-only here.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/dash/admin/projects/shop", new { name = "x" })).StatusCode);
        // Admin cannot delete themselves.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync("/api/dash/admin/users/admin")).StatusCode);
    }

    [Fact]
    public async Task Users_created_in_dashboard_can_log_in_and_see_only_their_projects()
    {
        var admin = await LoginAsync("admin");
        await CreateProjectAsync(admin, "team-1");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/dash/admin/users", new { username = "sara", password = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/dash/admin/users", new { username = "sara", password = CollectorFactory.Password, projects = new[] { "team-1" } })).StatusCode);

        var sara = await LoginAsync("sara");
        var me = await sara.GetFromJsonAsync<JsonElement>("/api/dash/me");
        var keys = me.GetProperty("projects").EnumerateArray().Select(p => p.GetProperty("key").GetString()).ToList();
        Assert.Equal(new[] { "team-1" }, keys);
        Assert.Equal(HttpStatusCode.Forbidden, (await sara.GetAsync("/api/dash/events?project=shop")).StatusCode);

        // Removing the user ends their access immediately.
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync("/api/dash/admin/users/sara")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await sara.GetAsync("/api/dash/me")).StatusCode);
    }
}
