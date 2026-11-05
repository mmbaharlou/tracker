using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracker.AspNetCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Hosting;
using Tracker.Api.Auth;
using Tracker.Api.Data;
using Tracker.Api.Storage;
using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

public sealed class FakeStore : IEventStore
{
    public readonly List<StoredEvent> Events = new();
    public Task WriteAsync(IReadOnlyList<StoredEvent> events, CancellationToken ct) { lock (Events) Events.AddRange(events); return Task.CompletedTask; }
    public Task<int> PurgeAsync(int olderThanDays, CancellationToken ct) => Task.FromResult(0);
    public async Task<List<StoredEvent>> WaitAsync(int count)
    {
        for (var i = 0; i < 50; i++) { lock (Events) if (Events.Count >= count) return Events.ToList(); await Task.Delay(100); }
        lock (Events) return Events.ToList();
    }
}

public class CollectorFactory : WebApplicationFactory<Program>
{
    public const string Secret = "0123456789abcdef0123456789abcdef0123456789";
    public const string Password = "correct horse battery";
    public FakeStore Store { get; } = new();
    // One open in-memory SQLite connection = one database shared by every request of this test host.
    private readonly SqliteConnection _db = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _db.Open();
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", "Server=none");
        builder.UseSetting("Bootstrap:OwnerUsername", "");
        builder.UseSetting("Auth:LoginRequestsPerMinute", "100000");
        builder.UseSetting("Tracker:Projects:0:Key", "shop");
        builder.UseSetting("Tracker:Projects:0:ServerSecret", Secret);
        builder.UseSetting("Tracker:Projects:0:AllowedOrigins:0", "https://shop.test");
        builder.UseSetting("Tracker:DataProtectionKeysPath", Path.Combine(Path.GetTempPath(), "tracker-test-keys-" + Guid.NewGuid()));
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IEventStore>();
            s.AddSingleton<IEventStore>(Store);
            s.RemoveAll<DbContextOptions<AppDbContext>>();
            s.RemoveAll<DbContextOptions>();
            s.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            s.AddDbContext<AppDbContext>(o => o.UseSqlite(_db));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.AdminUsers.Any(u => u.Username == "owner"))
        {
            db.AdminUsers.Add(new AdminUser { Username = "owner", PasswordHash = PasswordHasher.Hash(Password), Role = AdminRole.Owner });
            db.AdminUsers.Add(new AdminUser { Username = "admin", PasswordHash = PasswordHasher.Hash(Password), Role = AdminRole.Admin });
            var viewer = new AdminUser { Username = "viewer", PasswordHash = PasswordHasher.Hash(Password), Role = AdminRole.Viewer };
            viewer.Projects.Add(new AdminUserProject { ProjectKey = "shop" });
            db.AdminUsers.Add(viewer);
            db.SaveChanges();
        }
        return host;
    }

    /// <summary>A cookie-keeping client that sends the CSRF header, signed in as the given user.</summary>
    public async Task<HttpClient> LoginAsync(string user, string? password = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Requested-With", "fetch");
        (await client.PostAsJsonAsync("/api/auth/login", new { username = user, password = password ?? Password })).EnsureSuccessStatusCode();
        return client;
    }

    protected override void Dispose(bool disposing) { base.Dispose(disposing); _db.Dispose(); }
}

public class CollectorTests : IClassFixture<CollectorFactory>
{
    private readonly CollectorFactory _f;
    public CollectorTests(CollectorFactory f) => _f = f;

    private static StringContent WebBody(params object[] events)
        => new(JsonSerializer.Serialize(new { events, source = "web" }), Encoding.UTF8, "text/plain");

    [Fact]
    public async Task Web_batch_from_allowed_origin_is_stored_and_cannot_fake_api_events()
    {
        var client = _f.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/batch?k=shop")
        {
            Content = WebBody(
                new { id = Guid.NewGuid(), type = "pageview", ts = DateTimeOffset.UtcNow, anonId = "web-anon-1", sessionId = "s-1", url = "https://shop.test/p/1?token=abc", path = "/p/1" },
                new { id = Guid.NewGuid(), type = "api", ts = DateTimeOffset.UtcNow, anonId = "web-anon-1", route = "fake" },
                new { id = Guid.NewGuid(), type = "custom", name = "AddToCart", ts = DateTimeOffset.UtcNow, anonId = "web-anon-1", props = new { password = "p", sku = "A1" } }),
        };
        req.Headers.Add("Origin", "https://shop.test");
        req.Headers.Add("User-Agent", "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Safari/604.1");
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        Assert.Equal("https://shop.test", res.Headers.GetValues("Access-Control-Allow-Origin").Single());

        var stored = (await _f.Store.WaitAsync(2)).Where(e => e.AnonymousId == "web-anon-1").ToList();
        Assert.Equal(2, stored.Count);
        Assert.DoesNotContain(stored, e => e.Type == "api");
        var pv = stored.Single(e => e.Type == "pageview");
        Assert.Equal("web", pv.Source);
        Assert.Contains("token=***", pv.Url);
        Assert.Equal("iOS", pv.Os);
        var custom = stored.Single(e => e.Type == "custom");
        Assert.Contains("\"***\"", custom.Properties);
        Assert.Contains("A1", custom.Properties);
    }

    [Fact]
    public async Task Web_batch_from_other_origin_or_unknown_project_is_rejected()
    {
        var client = _f.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/batch?k=shop") { Content = WebBody() };
        req.Headers.Add("Origin", "https://evil.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/v1/batch?k=nope") { Content = WebBody() };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req2)).StatusCode);
    }

    [Fact]
    public async Task Oversized_web_body_is_rejected()
    {
        var client = _f.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/batch?k=shop") { Content = new StringContent(new string('x', 300 * 1024)) };
        req.Headers.Add("Origin", "https://shop.test");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Server_batch_requires_valid_signature()
    {
        var client = _f.CreateClient();
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { events = new[] { new { type = "api", anonId = "srv-x" } } }));
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/server/batch") { Content = new ByteArrayContent(body) };
        req.Headers.Add(TrackerHeaders.Project, "shop");
        req.Headers.Add(TrackerHeaders.Timestamp, ts.ToString());
        req.Headers.Add(TrackerHeaders.Signature, BatchSigner.Sign("wrong-secret-wrong-secret-wrong-secret", ts, body));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Backend_sdk_batches_reach_the_collector_end_to_end()
    {
        var options = new TrackerOptions { ProjectKey = "shop", ServerSecret = CollectorFactory.Secret, CollectorUrl = "http://localhost" };
        var sender = new BatchSender(new EventQueue(options), options, new FactoryClient(_f), NullLogger<BatchSender>.Instance);
        await sender.SendAsync(new List<TrackEvent>
        {
            new() { Type = EventTypes.Api, Route = "api/orders/{id}", Method = "POST", StatusCode = 201, UserId = "u-e2e", Ip = "5.6.7.8", UserAgent = "Mozilla/5.0 (Windows NT 10.0) Chrome/120.0" },
        }, CancellationToken.None);

        var stored = (await _f.Store.WaitAsync(1)).Where(e => e.UserId == "u-e2e").ToList();
        var e = Assert.Single(stored);
        Assert.Equal("server", e.Source);
        Assert.Equal("api", e.Type);
        Assert.Equal((short)201, e.StatusCode);
        Assert.Equal("5.6.7.8", e.Ip);
        Assert.Equal("Windows", e.Os);
    }

    private sealed class FactoryClient : IHttpClientFactory
    {
        private readonly CollectorFactory _f;
        public FactoryClient(CollectorFactory f) => _f = f;
        public HttpClient CreateClient(string name) => _f.CreateClient();
    }

    [Fact]
    public async Task Panel_requires_login_csrf_header_and_correct_password()
    {
        var client = _f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        // No CSRF header.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login", new { username = "viewer", password = CollectorFactory.Password })).StatusCode);

        client.DefaultRequestHeaders.Add("X-Requested-With", "fetch");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { username = "viewer", password = "nope" })).StatusCode);

        var res = await client.PostAsJsonAsync("/api/auth/login", new { username = "viewer", password = CollectorFactory.Password });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var cookie = res.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("viewer", me.GetProperty("username").GetString());
        Assert.Equal("shop", me.GetProperty("projects")[0].GetProperty("key").GetString());

        // A project the user cannot see.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/data/events?project=other")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/projects")).StatusCode);
    }

    [Fact]
    public async Task Api_sends_security_headers_and_no_store()
    {
        var client = _f.CreateClient();
        var res = await client.GetAsync("/api/auth/me");
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-store", res.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/nothing")).StatusCode);
    }

    [Fact]
    public void Password_hash_round_trips()
    {
        var h = PasswordHasher.Hash("a-long-password");
        Assert.True(PasswordHasher.Verify("a-long-password", h));
        Assert.False(PasswordHasher.Verify("a-long-passwore", h));
        Assert.False(PasswordHasher.Verify("x", "garbage"));
    }

    [Fact]
    public void User_upserts_take_traits_from_identify_and_link_anonymous_ids()
    {
        var now = DateTime.UtcNow;
        var rows = SqlServerEventStore.BuildUserUpserts(new List<StoredEvent>
        {
            new() { ProjectKey = "shop", UserId = "u1", AnonymousId = "a1", Type = "pageview", Ts = now },
            new() { ProjectKey = "shop", UserId = "u1", Type = "identify", Properties = "{\"name\":\"Ali\"}", Ts = now.AddSeconds(1) },
            new() { ProjectKey = "shop", AnonymousId = "a2", Type = "pageview", Ts = now },
        });
        var r = Assert.Single(rows);
        Assert.Equal(2L, r["count"]);
        Assert.Equal("a1", r["anonId"]);
        Assert.Equal("{\"name\":\"Ali\"}", r["traits"]);
    }
}
