using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tracker.AspNetCore;
using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

public class SdkTests
{
    private static async Task<(IHost host, HttpClient client, EventQueue queue)> StartAsync(Action<TrackerOptions>? configure = null)
    {
        var host = await new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(s =>
            {
                s.AddRouting();
                // No secret: the sender stays idle so we can inspect the queue.
                s.AddTracker(o => { o.ProjectKey = "test"; o.CollectorUrl = "http://collector"; configure?.Invoke(o); });
            });
            web.Configure(app =>
            {
                app.Use(async (ctx, next) =>
                {
                    if (ctx.Request.Headers.ContainsKey("X-Test-User"))
                        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", ctx.Request.Headers["X-Test-User"]!) }, "test"));
                    await next();
                });
                app.UseRouting();
                app.UseTracker();
                app.UseEndpoints(e =>
                {
                    e.MapPost("/api/orders/{id}", (int id) => Results.Ok()).TrackAs("ثبت سفارش");
                    e.MapGet("/api/secret", () => "x").NoTrack();
                    e.MapGet("/api/track", (ITracker t) => { t.Track("OrderPaid", new { amount = 10, cardNumber = "6037991122334455" }); return "ok"; });
                    e.MapGet("/api/boom", () => { throw new InvalidOperationException(); });
                });
            });
        }).StartAsync();
        return (host, host.GetTestClient(), host.Services.GetRequiredService<EventQueue>());
    }

    private static List<TrackEvent> Drain(EventQueue q)
    {
        var list = new List<TrackEvent>();
        while (q.Reader.TryRead(out var e)) list.Add(e);
        return list;
    }

    [Fact]
    public async Task Records_api_call_with_user_route_name_and_masked_query()
    {
        var (host, client, queue) = await StartAsync();
        using var _ = host;
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/orders/42?coupon=X1&token=secret");
        req.Headers.Add("X-Test-User", "user-7");
        req.Headers.Add(TrackerHeaders.Session, "sess-1");
        req.Headers.Add(TrackerHeaders.Anonymous, "anon-1");
        (await client.SendAsync(req)).EnsureSuccessStatusCode();

        var e = Assert.Single(Drain(queue));
        Assert.Equal(EventTypes.Api, e.Type);
        Assert.Equal("ثبت سفارش", e.Name);
        Assert.Equal("/api/orders/{id}", e.Route);
        Assert.Equal("user-7", e.UserId);
        Assert.Equal("sess-1", e.SessionId);
        Assert.Equal("anon-1", e.AnonymousId);
        Assert.Equal(200, e.StatusCode);
        Assert.Equal("42", e.Properties!["routeValues"].GetProperty("id").GetString());
        Assert.Equal(Masker.Mask, e.Properties["query"].GetProperty("token").GetString());
        Assert.Equal("X1", e.Properties["query"].GetProperty("coupon").GetString());
    }

    [Fact]
    public async Task Skips_NoTrack_and_ignored_paths_and_rejects_bad_session_ids()
    {
        var (host, client, queue) = await StartAsync();
        using var _ = host;
        await client.GetAsync("/api/secret");
        await client.GetAsync("/health");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/orders/1");
        req.Headers.Add(TrackerHeaders.Session, "<script>alert(1)</script>");
        await client.SendAsync(req);

        var e = Assert.Single(Drain(queue));
        Assert.Null(e.SessionId);
    }

    [Fact]
    public async Task Track_masks_sensitive_properties()
    {
        var (host, client, queue) = await StartAsync();
        using var _ = host;
        await client.GetAsync("/api/track");
        var events = Drain(queue);
        var custom = Assert.Single(events, x => x.Type == EventTypes.Custom);
        Assert.Equal("OrderPaid", custom.Name);
        Assert.Equal(Masker.Mask, custom.Properties!["cardNumber"].GetString());
        Assert.Equal(10, custom.Properties["amount"].GetInt32());
    }

    [Fact]
    public async Task Exceptions_are_recorded_and_rethrown_unchanged()
    {
        var (host, client, queue) = await StartAsync();
        using var _ = host;
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("/api/boom"));
        var e = Assert.Single(Drain(queue));
        Assert.Equal(500, e.StatusCode);
    }

    [Fact]
    public async Task Disabled_records_nothing()
    {
        var (host, client, queue) = await StartAsync(o => o.Enabled = false);
        using var _ = host;
        await client.PostAsync("/api/orders/1", null);
        Assert.Empty(Drain(queue));
    }

    [Fact]
    public void Full_queue_drops_instead_of_blocking()
    {
        var q = new EventQueue(new TrackerOptions { QueueCapacity = 100 });
        for (var i = 0; i < 150; i++) q.TryEnqueue(new TrackEvent());
        Assert.Equal(50, q.Dropped);
    }
}

public class InsightsTests
{
    [Fact]
    public async Task Insights_route_requires_a_policy_and_rejects_anonymous_callers()
    {
        using var host = await new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(s =>
            {
                s.AddRouting();
                s.AddAuthentication("test").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, NoAuth>("test", _ => { });
                s.AddAuthorization(o => o.AddPolicy("AdminOnly", p => p.RequireRole("admin")));
                s.AddTracker(o => { o.ProjectKey = "verti"; o.CollectorUrl = "http://collector"; o.ReadKey = "rk_x"; });
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(e =>
                {
                    Assert.Throws<ArgumentException>(() => e.MapTrackerInsights("/x", ""));
                    e.MapTrackerInsights("/api/admin/tracker", "AdminOnly");
                });
            });
        }).StartAsync();
        var res = await host.GetTestClient().GetAsync("/api/admin/tracker/events");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Empty(Drain(host.Services.GetRequiredService<EventQueue>()));
    }

    private static List<TrackEvent> Drain(EventQueue q) { var l = new List<TrackEvent>(); while (q.Reader.TryRead(out var e)) l.Add(e); return l; }

    private sealed class NoAuth : Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>
    {
        public NoAuth(Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> o,
            Microsoft.Extensions.Logging.ILoggerFactory l, System.Text.Encodings.Web.UrlEncoder e) : base(o, l, e) { }
        protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.NoResult());
    }
}
