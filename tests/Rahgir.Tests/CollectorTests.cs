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
using Rahgir.AspNetCore;
using Rahgir.Collector.Security;
using Rahgir.Collector.Storage;
using Rahgir.Core;
using Xunit;

namespace Rahgir.Tests;

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:RahgirWriter", "Server=none");
        builder.UseSetting("ConnectionStrings:RahgirReader", "Server=none");
        builder.UseSetting("Rahgir:Projects:0:Key", "shop");
        builder.UseSetting("Rahgir:Projects:0:ServerSecret", Secret);
        builder.UseSetting("Rahgir:Projects:0:AllowedOrigins:0", "https://shop.test");
        builder.UseSetting("Rahgir:DashboardUsers:0:Username", "admin");
        builder.UseSetting("Rahgir:DashboardUsers:0:PasswordHash", PasswordHasher.Hash(Password));
        builder.UseSetting("Rahgir:DashboardUsers:0:IsAdmin", "true");
        builder.UseSetting("Rahgir:DashboardUsers:1:Username", "viewer");
        builder.UseSetting("Rahgir:DashboardUsers:1:PasswordHash", PasswordHasher.Hash(Password));
        builder.UseSetting("Rahgir:DashboardUsers:1:Projects:0", "shop");
        builder.UseSetting("Rahgir:DataProtectionKeysPath", Path.Combine(Path.GetTempPath(), "rahgir-test-keys-" + Guid.NewGuid()));
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IEventStore>();
            s.AddSingleton<IEventStore>(Store);
            s.RemoveAll<Rahgir.Collector.Management.IAdminStore>();
            s.AddSingleton<Rahgir.Collector.Management.IAdminStore, Rahgir.Collector.Management.InMemoryAdminStore>();
        });
    }
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
        req.Headers.Add(RahgirHeaders.Project, "shop");
        req.Headers.Add(RahgirHeaders.Timestamp, ts.ToString());
        req.Headers.Add(RahgirHeaders.Signature, BatchSigner.Sign("wrong-secret-wrong-secret-wrong-secret", ts, body));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Backend_sdk_batches_reach_the_collector_end_to_end()
    {
        var options = new RahgirOptions { ProjectKey = "shop", ServerSecret = CollectorFactory.Secret, CollectorUrl = "http://localhost" };
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
    public async Task Dashboard_requires_login_csrf_header_and_correct_password()
    {
        var client = _f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dash/me")).StatusCode);

        // No CSRF header.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/dash/login", new { username = "admin", password = CollectorFactory.Password })).StatusCode);

        var bad = new HttpRequestMessage(HttpMethod.Post, "/api/dash/login") { Content = JsonContent.Create(new { username = "admin", password = "nope" }) };
        bad.Headers.Add("X-Rahgir-Csrf", "1");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(bad)).StatusCode);

        var good = new HttpRequestMessage(HttpMethod.Post, "/api/dash/login") { Content = JsonContent.Create(new { username = "admin", password = CollectorFactory.Password }) };
        good.Headers.Add("X-Rahgir-Csrf", "1");
        var res = await client.SendAsync(good);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var cookie = res.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/dash/me");
        Assert.Equal("admin", me.GetProperty("username").GetString());
        Assert.Equal("shop", me.GetProperty("projects")[0].GetProperty("key").GetString());

        // A project the user cannot see.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dash/events?project=other")).StatusCode);
    }

    [Fact]
    public async Task Dashboard_pages_get_hash_based_csp_and_security_headers()
    {
        var client = _f.CreateClient();
        var res = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var csp = res.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self' 'sha256-", csp);
        Assert.DoesNotContain("unsafe-inline' ;", csp);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp);
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
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
