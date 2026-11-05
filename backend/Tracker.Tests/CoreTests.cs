using System.Text;
using System.Text.Json;
using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

public class CoreTests
{
    [Fact]
    public void Masker_masks_sensitive_keys_recursively_and_card_numbers()
    {
        var json = """{"user":"ali","password":"123","nested":{"accessToken":"abc","note":"card 6037 9911 2233 4455"},"items":[{"cvv2":"999"}]}""";
        var masked = Masker.Default.MaskJson(json);
        Assert.DoesNotContain("123\"", masked);
        Assert.DoesNotContain("abc", masked);
        Assert.DoesNotContain("6037 9911", masked);
        Assert.DoesNotContain("999", masked);
        Assert.Contains("ali", masked);
    }

    [Fact]
    public void Masker_keeps_harmless_keys()
    {
        Assert.False(Masker.Default.IsSensitiveKey("productId"));
        Assert.False(Masker.Default.IsSensitiveKey("title"));
        Assert.True(Masker.Default.IsSensitiveKey("new_password"));
        Assert.True(Masker.Default.IsSensitiveKey("X-Refresh-Token"));
    }

    [Fact]
    public void Signer_accepts_valid_and_rejects_tampered_or_old_batches()
    {
        var secret = new string('s', 40);
        var body = Encoding.UTF8.GetBytes("{\"events\":[]}");
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds();
        var sig = BatchSigner.Sign(secret, ts, body);

        Assert.True(BatchSigner.Verify(secret, ts.ToString(), sig, body, now));
        Assert.False(BatchSigner.Verify(secret, ts.ToString(), sig, Encoding.UTF8.GetBytes("{\"events\":[1]}"), now));
        Assert.False(BatchSigner.Verify("other-secret-other-secret-other-secret", ts.ToString(), sig, body, now));
        Assert.False(BatchSigner.Verify(secret, ts.ToString(), sig, body, now.AddMinutes(10)));
        Assert.False(BatchSigner.Verify(secret, null, sig, body, now));
    }

    [Fact]
    public void UserAgent_parser_classifies_common_browsers()
    {
        var r = UserAgentParser.Parse("Mozilla/5.0 (Linux; Android 13; SM-A515F) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Mobile Safari/537.36");
        Assert.Equal(("Chrome", "Android", "Mobile"), (r.Browser, r.Os, r.Device));
        r = UserAgentParser.Parse("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36 Edg/120.0");
        Assert.Equal(("Edge", "Windows", "Desktop"), (r.Browser, r.Os, r.Device));
    }

    [Fact]
    public void Event_round_trips_with_short_wire_names()
    {
        var e = new TrackEvent { Type = "click", AnonymousId = "a1", SessionId = "s1" };
        var json = JsonSerializer.Serialize(e, TrackerJson.Options);
        Assert.Contains("\"anonId\":\"a1\"", json);
        var back = JsonSerializer.Deserialize<TrackEvent>(json, TrackerJson.Options)!;
        Assert.Equal("s1", back.SessionId);
    }
}
