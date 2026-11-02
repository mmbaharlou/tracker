using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rahgir.Core;

/// <summary>Removes secrets and personal data from tracked payloads. Used by both the SDK and the Collector.</summary>
public sealed partial class Masker
{
    public const string Mask = "***";

    public static readonly string[] DefaultSensitiveKeys =
    {
        "password", "passwd", "pwd", "pass", "secret", "token", "accesstoken", "refreshtoken", "apikey", "api_key",
        "authorization", "auth", "cookie", "otp", "code", "pin", "cvv", "cvv2", "card", "cardnumber", "pan",
        "nationalcode", "codemelli", "melli", "ssn", "iban", "sheba", "creditcard",
    };

    private readonly HashSet<string> _keys;

    public Masker(IEnumerable<string>? extraKeys = null)
    {
        _keys = new HashSet<string>(DefaultSensitiveKeys, StringComparer.OrdinalIgnoreCase);
        if (extraKeys != null) foreach (var k in extraKeys) _keys.Add(Normalize(k));
    }

    public static Masker Default { get; } = new();

    public bool IsSensitiveKey(string key)
    {
        var n = Normalize(key);
        if (_keys.Contains(n)) return true;
        foreach (var k in _keys)
            if (k.Length >= 5 && n.Contains(k, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Masks card numbers (13-19 digits) and Iranian national codes / phone-like long digit runs inside free text.</summary>
    public string MaskText(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return CardLike().Replace(value, Mask);
    }

    public Dictionary<string, JsonElement>? MaskProperties(Dictionary<string, JsonElement>? props)
    {
        if (props == null || props.Count == 0) return props;
        var result = new Dictionary<string, JsonElement>(props.Count);
        foreach (var (key, value) in props)
        {
            if (IsSensitiveKey(key)) { result[key] = JsonSerializer.SerializeToElement(Mask); continue; }
            var node = JsonNode.Parse(value.GetRawText());
            result[key] = JsonSerializer.SerializeToElement(MaskNode(node));
        }
        return result;
    }

    /// <summary>Masks a raw JSON string (e.g. a request body). Non-JSON input is text-masked.</summary>
    public string MaskJson(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            return MaskNode(node)?.ToJsonString() ?? "null";
        }
        catch (JsonException)
        {
            return MaskText(json);
        }
    }

    private JsonNode? MaskNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                    obj[key] = IsSensitiveKey(key) ? JsonValue.Create(Mask) : MaskNode(obj[key]?.DeepClone());
                return obj;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++) arr[i] = MaskNode(arr[i]?.DeepClone());
                return arr;
            case JsonValue v when v.TryGetValue<string>(out var s):
                return JsonValue.Create(MaskText(s));
            default:
                return node;
        }
    }

    private static string Normalize(string key) => key.Replace("-", "").Replace("_", "").Replace(" ", "").ToLowerInvariant();

    [GeneratedRegex(@"(?<!\d)(?:\d[ -]?){12,18}\d(?!\d)")]
    private static partial Regex CardLike();
}
