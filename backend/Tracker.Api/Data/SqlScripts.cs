using System.Reflection;
using System.Text.RegularExpressions;

namespace Tracker.Api.Data;

/// <summary>Reads the embedded scripts in Data/Sql and splits them on GO, for use inside migrations.</summary>
public static partial class SqlScripts
{
    public static IEnumerable<string> Batches(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));
        using var s = new StreamReader(asm.GetManifestResourceStream(name)!);
        return Go().Split(s.ReadToEnd()).Where(b => !string.IsNullOrWhiteSpace(b) && !b.Trim().StartsWith("SET NOCOUNT", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)] private static partial Regex Go();
}
