using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Tracker.Core;

namespace Tracker.AspNetCore;

internal static class RequestInfo
{
    public static void Fill(TrackEvent e, HttpContext ctx, TrackerOptions options)
    {
        var req = ctx.Request;
        e.UserId ??= FindUserId(ctx, options);
        e.SessionId ??= Clean(req.Headers[TrackerHeaders.Session].ToString());
        e.AnonymousId ??= Clean(req.Headers[TrackerHeaders.Anonymous].ToString());
        e.Path ??= req.Path.Value;
        e.Method ??= req.Method;
        e.UserAgent ??= Truncate(req.Headers.UserAgent.ToString(), 512);
        e.Referrer ??= Truncate(req.Headers.Referer.ToString(), 1024);
        e.Language ??= Truncate(req.Headers.AcceptLanguage.ToString().Split(',')[0], 16);
        if (options.CaptureIp && e.Ip == null)
        {
            var ip = ctx.Connection.RemoteIpAddress;
            if (ip != null) e.Ip = (options.AnonymizeIp ? Anonymize(ip) : ip).ToString();
        }
    }

    public static string? FindUserId(HttpContext ctx, TrackerOptions options)
    {
        var user = ctx.User;
        if (user?.Identity?.IsAuthenticated != true) return null;
        foreach (var type in options.UserIdClaimTypes)
        {
            var v = user.FindFirst(type)?.Value;
            if (!string.IsNullOrEmpty(v)) return Truncate(v, 128);
        }
        return Truncate(user.Identity.Name, 128);
    }

    public static IPAddress Anonymize(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork) bytes[3] = 0;
        else for (var i = 6; i < bytes.Length; i++) bytes[i] = 0;
        return new IPAddress(bytes);
    }

    /// <summary>Session/anon ids come from the client: accept only short safe tokens.</summary>
    private static string? Clean(string? v)
    {
        if (string.IsNullOrEmpty(v) || v.Length > 64) return null;
        foreach (var c in v)
            if (!(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_')) return null;
        return v;
    }

    internal static string? Truncate(string? v, int max)
        => string.IsNullOrEmpty(v) ? null : v.Length <= max ? v : v[..max];
}
