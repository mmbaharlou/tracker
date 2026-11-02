namespace Rahgir.Core;

/// <summary>Tiny dependency-free user-agent classifier. Good enough for dashboards, not for fingerprinting.</summary>
public static class UserAgentParser
{
    public readonly record struct Result(string Browser, string Os, string Device);

    public static Result Parse(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return new("Unknown", "Unknown", "Unknown");

        string browser =
            ua.Contains("Edg/") ? "Edge" :
            ua.Contains("OPR/") || ua.Contains("Opera") ? "Opera" :
            ua.Contains("SamsungBrowser") ? "Samsung Internet" :
            ua.Contains("Firefox/") ? "Firefox" :
            ua.Contains("Chrome/") || ua.Contains("CriOS") ? "Chrome" :
            ua.Contains("Safari/") ? "Safari" :
            ua.Contains("bot", StringComparison.OrdinalIgnoreCase) || ua.Contains("spider", StringComparison.OrdinalIgnoreCase) ? "Bot" :
            "Other";

        string os =
            ua.Contains("Windows") ? "Windows" :
            ua.Contains("Android") ? "Android" :
            ua.Contains("iPhone") || ua.Contains("iPad") || ua.Contains("iPod") ? "iOS" :
            ua.Contains("Mac OS X") || ua.Contains("Macintosh") ? "macOS" :
            ua.Contains("Linux") ? "Linux" :
            "Other";

        string device =
            ua.Contains("iPad") || ua.Contains("Tablet") ? "Tablet" :
            ua.Contains("Mobi") || ua.Contains("iPhone") || (ua.Contains("Android") && !ua.Contains("Tablet")) ? "Mobile" :
            browser == "Bot" ? "Bot" :
            "Desktop";

        return new(browser, os, device);
    }
}
