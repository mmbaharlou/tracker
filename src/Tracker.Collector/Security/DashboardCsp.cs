using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Tracker.Collector.Security;

/// <summary>
/// Builds a strict Content-Security-Policy for each static dashboard page: inline scripts that Next.js emits
/// are allowed by their SHA-256 hash only, so any injected script is blocked by the browser.
/// </summary>
public sealed partial class DashboardCsp
{
    private readonly Dictionary<string, string> _byFile = new(StringComparer.OrdinalIgnoreCase);
    private const string Base = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; font-src 'self' data:; " +
                                "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public DashboardCsp(IWebHostEnvironment env)
    {
        var root = env.WebRootPath;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "*.html", SearchOption.AllDirectories))
        {
            var html = File.ReadAllText(file);
            var hashes = InlineScript().Matches(html)
                .Select(m => "'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(m.Groups[1].Value))) + "'")
                .Distinct();
            _byFile[Path.GetFullPath(file)] = $"{Base}; script-src 'self' {string.Join(' ', hashes)}";
        }
    }

    public string For(string physicalPath)
        => _byFile.TryGetValue(Path.GetFullPath(physicalPath), out var csp) ? csp : $"{Base}; script-src 'self'";

    [GeneratedRegex(@"<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)</script>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScript();
}
