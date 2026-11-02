using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Rahgir.AspNetCore;

public static class RahgirInsightsExtensions
{
    private static readonly string[] Allowed = { "overview", "events", "users", "user", "session" };
    private static readonly string[] AllowedQuery = { "from", "to", "tz", "type", "userId", "sessionId", "q", "before", "take", "page", "id" };

    /// <summary>
    /// Exposes this project's own tracking data (from the central Collector) inside this app, e.g. for its admin panel:
    /// GET {pattern}/overview, /events, /users, /user?id=, /session?id=
    /// The read key never leaves the server. An authorization policy is required so the data is never public.
    /// <code>app.MapRahgirInsights("/api/admin/rahgir", "AdminOnly");</code>
    /// </summary>
    public static RouteGroupBuilder MapRahgirInsights(this IEndpointRouteBuilder app, string pattern, string authorizationPolicy)
    {
        if (string.IsNullOrWhiteSpace(authorizationPolicy))
            throw new ArgumentException("An authorization policy is required: tracking data must never be public.", nameof(authorizationPolicy));

        var group = app.MapGroup(pattern);
        group.RequireAuthorization(authorizationPolicy);
        group.WithMetadata(new NoTrackAttribute());

        group.MapGet("/{endpoint}", async (string endpoint, HttpContext ctx, RahgirOptions o, IHttpClientFactory http) =>
        {
            if (!Allowed.Contains(endpoint, StringComparer.Ordinal)) return Results.NotFound();
            if (string.IsNullOrEmpty(o.ReadKey) || string.IsNullOrEmpty(o.CollectorUrl))
                return Results.Problem("Rahgir:ReadKey or Rahgir:CollectorUrl is not configured.", statusCode: 501);

            var query = QueryString.Create(ctx.Request.Query
                .Where(q => AllowedQuery.Contains(q.Key, StringComparer.Ordinal))
                .Select(q => new KeyValuePair<string, string?>(q.Key, q.Value.ToString())));
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{o.CollectorUrl.TrimEnd('/')}/v1/read/{endpoint}{query}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ReadKey);

            using var res = await http.CreateClient(BatchSender.HttpClientName).SendAsync(req, ctx.RequestAborted);
            ctx.Response.Headers.CacheControl = "no-store";
            if (!res.IsSuccessStatusCode) return Results.StatusCode(res.StatusCode == System.Net.HttpStatusCode.Unauthorized ? 502 : (int)res.StatusCode);
            return Results.Content(await res.Content.ReadAsStringAsync(ctx.RequestAborted), "application/json; charset=utf-8");
        });
        return group;
    }
}
