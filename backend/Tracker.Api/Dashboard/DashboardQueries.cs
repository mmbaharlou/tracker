using Dapper;
using Microsoft.Data.SqlClient;

namespace Tracker.Api.Dashboard;

/// <summary>Read side for the dashboard. Parameterized Dapper queries only.</summary>
public sealed class DashboardQueries
{
    private readonly string _cs;
    public DashboardQueries(IConfiguration config)
        => _cs = config.GetConnectionString("Reader") ?? config.GetConnectionString("Default")
                 ?? throw new InvalidOperationException("ConnectionStrings:Default is missing.");

    private SqlConnection Open() => new(_cs);

    public const string EventColumns = @"Id, Source, Type, Name, Ts, UserId, AnonymousId, SessionId, Url, Path, Referrer, Title,
        Route, Method, StatusCode, Ip, Browser, Os, Device, Screen, Language, Properties";

    public async Task<object> OverviewAsync(string project, DateTime from, DateTime to, int tz)
    {
        const string sql = @"
DECLARE @f DATETIME2(3) = @from, @t DATETIME2(3) = @to;
SELECT COUNT_BIG(*) AS Events,
       COUNT(DISTINCT COALESCE(UserId, AnonymousId)) AS Visitors,
       COUNT(DISTINCT UserId) AS Users,
       COUNT(DISTINCT SessionId) AS Sessions,
       SUM(CASE WHEN Type = 'pageview' THEN 1 ELSE 0 END) AS PageViews,
       SUM(CASE WHEN Type = 'api' THEN 1 ELSE 0 END) AS ApiCalls
FROM tracker.Events WHERE ProjectKey = @p AND Ts >= @f AND Ts < @t;

SELECT CAST(DATEADD(MINUTE, @tz, Ts) AS DATE) AS Day,
       COUNT(DISTINCT COALESCE(UserId, AnonymousId)) AS Visitors,
       SUM(CASE WHEN Type = 'pageview' THEN 1 ELSE 0 END) AS PageViews,
       COUNT_BIG(*) AS Events
FROM tracker.Events WHERE ProjectKey = @p AND Ts >= @f AND Ts < @t
GROUP BY CAST(DATEADD(MINUTE, @tz, Ts) AS DATE) ORDER BY Day;

SELECT TOP 10 Path AS Label, COUNT_BIG(*) AS Count, COUNT(DISTINCT COALESCE(UserId, AnonymousId)) AS Visitors
FROM tracker.Events WHERE ProjectKey = @p AND Ts >= @f AND Ts < @t AND Type = 'pageview' AND Path IS NOT NULL
GROUP BY Path ORDER BY Count DESC;

SELECT TOP 10 COALESCE(Name, CONCAT(Method, ' ', COALESCE(Route, Path))) AS Label, COUNT_BIG(*) AS Count,
       COUNT(DISTINCT UserId) AS Visitors,
       SUM(CASE WHEN StatusCode >= 400 THEN 1 ELSE 0 END) AS Failed
FROM tracker.Events WHERE ProjectKey = @p AND Ts >= @f AND Ts < @t AND Type = 'api'
GROUP BY COALESCE(Name, CONCAT(Method, ' ', COALESCE(Route, Path))) ORDER BY Count DESC;

SELECT TOP 10 Name AS Label, COUNT_BIG(*) AS Count, COUNT(DISTINCT COALESCE(UserId, AnonymousId)) AS Visitors
FROM tracker.Events WHERE ProjectKey = @p AND Ts >= @f AND Ts < @t AND Type = 'custom' AND Name IS NOT NULL
GROUP BY Name ORDER BY Count DESC;

SELECT TOP 10 Referrer AS Label, COUNT_BIG(*) AS Count, COUNT(DISTINCT AnonymousId) AS Visitors
FROM tracker.Events WHERE ProjectKey = @p AND Ts >= @f AND Ts < @t AND Type = 'pageview' AND Referrer IS NOT NULL
GROUP BY Referrer ORDER BY Count DESC;

SELECT 'device' AS Kind, Device AS Label, COUNT(DISTINCT COALESCE(UserId, AnonymousId)) AS Count
FROM tracker.Events WHERE ProjectKey = @p AND Ts >= @f AND Ts < @t AND Source = 'web' GROUP BY Device
UNION ALL
SELECT 'browser', Browser, COUNT(DISTINCT COALESCE(UserId, AnonymousId))
FROM tracker.Events WHERE ProjectKey = @p AND Ts >= @f AND Ts < @t AND Source = 'web' GROUP BY Browser
UNION ALL
SELECT 'os', Os, COUNT(DISTINCT COALESCE(UserId, AnonymousId))
FROM tracker.Events WHERE ProjectKey = @p AND Ts >= @f AND Ts < @t AND Source = 'web' GROUP BY Os;";

        await using var c = Open();
        using var m = await c.QueryMultipleAsync(sql, new { p = project, from, to, tz }, commandTimeout: 120);
        return new
        {
            totals = await m.ReadFirstAsync(),
            daily = await m.ReadAsync(),
            topPages = await m.ReadAsync(),
            topActions = await m.ReadAsync(),
            topEvents = await m.ReadAsync(),
            topReferrers = await m.ReadAsync(),
            breakdown = await m.ReadAsync(),
        };
    }

    public async Task<IEnumerable<dynamic>> EventsAsync(string project, string? type, string? userId, string? sessionId, string? q, DateTime? before, int take)
    {
        var sql = $@"SELECT TOP (@take) {EventColumns}, ReceivedAt FROM tracker.Events
WHERE ProjectKey = @project
  AND (@type IS NULL OR Type = @type)
  AND (@userId IS NULL OR UserId = @userId)
  AND (@sessionId IS NULL OR SessionId = @sessionId)
  AND (@before IS NULL OR ReceivedAt < @before)
  AND (@q IS NULL OR Path LIKE @q OR Name LIKE @q OR Route LIKE @q OR UserId LIKE @q)
ORDER BY ReceivedAt DESC";
        await using var c = Open();
        return await c.QueryAsync(sql, new { project, type, userId, sessionId, before, take, q = Like(q) });
    }

    public async Task<object> UsersAsync(string project, string? q, int page, int size)
    {
        const string sql = @"
SELECT COUNT(*) FROM tracker.Users WHERE ProjectKey = @project AND (@q IS NULL OR UserId LIKE @q OR Traits LIKE @q);
SELECT UserId, Traits, FirstSeen, LastSeen, EventCount, LastBrowser, LastOs, LastDevice
FROM tracker.Users WHERE ProjectKey = @project AND (@q IS NULL OR UserId LIKE @q OR Traits LIKE @q)
ORDER BY LastSeen DESC OFFSET @skip ROWS FETCH NEXT @size ROWS ONLY;";
        await using var c = Open();
        using var m = await c.QueryMultipleAsync(sql, new { project, q = Like(q), skip = (page - 1) * size, size });
        return new { total = await m.ReadFirstAsync<int>(), items = await m.ReadAsync() };
    }

    public async Task<object?> UserAsync(string project, string userId, DateTime? before, int take)
    {
        var sql = $@"
SELECT UserId, Traits, FirstSeen, LastSeen, EventCount, LastBrowser, LastOs, LastDevice, LastIp
FROM tracker.Users WHERE ProjectKey = @project AND UserId = @userId;

SELECT AnonymousId FROM tracker.Aliases WHERE ProjectKey = @project AND UserId = @userId;

SELECT TOP (@take) {EventColumns} FROM tracker.Events
WHERE ProjectKey = @project
  AND (UserId = @userId OR AnonymousId IN (SELECT AnonymousId FROM tracker.Aliases WHERE ProjectKey = @project AND UserId = @userId))
  AND (@before IS NULL OR Ts < @before)
ORDER BY Ts DESC;

SELECT COUNT(DISTINCT SessionId) AS Sessions, MIN(Ts) AS FirstEvent, MAX(Ts) AS LastEvent,
       SUM(CASE WHEN Type = 'pageview' THEN 1 ELSE 0 END) AS PageViews,
       SUM(CASE WHEN Type = 'api' THEN 1 ELSE 0 END) AS ApiCalls
FROM tracker.Events
WHERE ProjectKey = @project
  AND (UserId = @userId OR AnonymousId IN (SELECT AnonymousId FROM tracker.Aliases WHERE ProjectKey = @project AND UserId = @userId));";
        await using var c = Open();
        using var m = await c.QueryMultipleAsync(sql, new { project, userId, before, take }, commandTimeout: 60);
        var profile = await m.ReadFirstOrDefaultAsync();
        var aliases = await m.ReadAsync<string>();
        var events = await m.ReadAsync();
        var stats = await m.ReadFirstOrDefaultAsync();
        return new { profile, aliases, events, stats };
    }

    public async Task<IEnumerable<dynamic>> VisitorAsync(string project, string anonId, int take)
    {
        await using var c = Open();
        return await c.QueryAsync($@"SELECT TOP (@take) {EventColumns} FROM tracker.Events
WHERE ProjectKey = @project AND AnonymousId = @anonId ORDER BY Ts DESC", new { project, anonId, take });
    }

    public async Task<IEnumerable<dynamic>> SessionAsync(string project, string sessionId)
    {
        await using var c = Open();
        return await c.QueryAsync($@"SELECT TOP (2000) {EventColumns} FROM tracker.Events
WHERE ProjectKey = @project AND SessionId = @sessionId ORDER BY Ts", new { project, sessionId });
    }

    public async Task ForgetUserAsync(string project, string userId)
    {
        await using var c = Open();
        await c.ExecuteAsync("tracker.ForgetUser", new { projectKey = project, userId },
            commandType: System.Data.CommandType.StoredProcedure, commandTimeout: 600);
    }

    /// <summary>User search text becomes a LIKE parameter with wildcards escaped.</summary>
    private static string? Like(string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return null;
        q = q.Trim();
        if (q.Length > 100) q = q[..100];
        return "%" + q.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
    }
}
