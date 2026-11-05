using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Tracker.Api.Storage;

public sealed class SqlServerEventStore : IEventStore
{
    private readonly string _connectionString;

    public SqlServerEventStore(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is missing.");
    }

    private static readonly (string Name, Type Type)[] Columns =
    {
        ("Id", typeof(Guid)), ("ProjectKey", typeof(string)), ("Source", typeof(string)), ("Type", typeof(string)),
        ("Name", typeof(string)), ("Ts", typeof(DateTime)), ("ReceivedAt", typeof(DateTime)), ("UserId", typeof(string)),
        ("AnonymousId", typeof(string)), ("SessionId", typeof(string)), ("Url", typeof(string)), ("Path", typeof(string)),
        ("Referrer", typeof(string)), ("Title", typeof(string)), ("Route", typeof(string)), ("Method", typeof(string)),
        ("StatusCode", typeof(short)), ("Ip", typeof(string)), ("Browser", typeof(string)), ("Os", typeof(string)),
        ("Device", typeof(string)), ("Screen", typeof(string)), ("Language", typeof(string)), ("Properties", typeof(string)),
    };

    public async Task WriteAsync(IReadOnlyList<StoredEvent> events, CancellationToken ct)
    {
        if (events.Count == 0) return;
        var table = new DataTable();
        foreach (var (name, type) in Columns) table.Columns.Add(name, type);
        foreach (var e in events)
        {
            table.Rows.Add(e.Id, e.ProjectKey, e.Source, e.Type, (object?)e.Name ?? DBNull.Value, e.Ts, e.ReceivedAt,
                (object?)e.UserId ?? DBNull.Value, (object?)e.AnonymousId ?? DBNull.Value, (object?)e.SessionId ?? DBNull.Value,
                (object?)e.Url ?? DBNull.Value, (object?)e.Path ?? DBNull.Value, (object?)e.Referrer ?? DBNull.Value,
                (object?)e.Title ?? DBNull.Value, (object?)e.Route ?? DBNull.Value, (object?)e.Method ?? DBNull.Value,
                (object?)e.StatusCode ?? DBNull.Value, (object?)e.Ip ?? DBNull.Value, (object?)e.Browser ?? DBNull.Value,
                (object?)e.Os ?? DBNull.Value, (object?)e.Device ?? DBNull.Value, (object?)e.Screen ?? DBNull.Value,
                (object?)e.Language ?? DBNull.Value, (object?)e.Properties ?? DBNull.Value);
        }

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // CheckConstraints: lets the writer login work with INSERT permission only (no ALTER TABLE).
        using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.CheckConstraints, null))
        {
            bulk.DestinationTableName = "tracker.Events";
            bulk.BatchSize = events.Count;
            bulk.BulkCopyTimeout = 60;
            foreach (var (name, _) in Columns) bulk.ColumnMappings.Add(name, name);
            await bulk.WriteToServerAsync(table, ct);
        }

        var users = BuildUserUpserts(events);
        if (users.Count > 0)
        {
            await conn.ExecuteAsync(new CommandDefinition("tracker.UpsertUsers",
                new { json = JsonSerializer.Serialize(users) }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        }
    }

    internal static List<Dictionary<string, object?>> BuildUserUpserts(IReadOnlyList<StoredEvent> events)
    {
        var result = new Dictionary<(string, string), Dictionary<string, object?>>();
        foreach (var e in events.Where(e => e.UserId != null).OrderBy(e => e.Ts))
        {
            var key = (e.ProjectKey, e.UserId!);
            if (!result.TryGetValue(key, out var row))
            {
                row = new Dictionary<string, object?> { ["projectKey"] = e.ProjectKey, ["userId"] = e.UserId, ["count"] = 0L };
                result[key] = row;
            }
            row["count"] = (long)row["count"]! + 1;
            row["ts"] = e.Ts.ToString("yyyy-MM-ddTHH:mm:ss.fff");
            if (e.AnonymousId != null) row["anonId"] = e.AnonymousId;
            if (e.Browser != null) row["browser"] = e.Browser;
            if (e.Os != null) row["os"] = e.Os;
            if (e.Device != null) row["device"] = e.Device;
            if (e.Ip != null) row["ip"] = e.Ip;
            if (e.Type == Core.EventTypes.Identify && e.Properties != null) row["traits"] = e.Properties;
        }
        return result.Values.ToList();
    }

    public async Task<int> PurgeAsync(int olderThanDays, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition("tracker.PurgeOldEvents",
            new { days = olderThanDays }, commandType: CommandType.StoredProcedure, commandTimeout: 600, cancellationToken: ct));
    }
}
