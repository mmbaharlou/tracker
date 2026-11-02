using System.Security.Cryptography;
using System.Text;

namespace Rahgir.Core;

/// <summary>
/// HMAC-SHA256 signing of server-to-collector batches. The signature covers the timestamp and the exact body bytes,
/// so events cannot be forged or altered without the project's server secret.
/// </summary>
public static class BatchSigner
{
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public static string Sign(string secret, long unixTimestamp, ReadOnlySpan<byte> body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var prefix = Encoding.UTF8.GetBytes(unixTimestamp.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
        var data = new byte[prefix.Length + body.Length];
        prefix.CopyTo(data, 0);
        body.CopyTo(data.AsSpan(prefix.Length));
        return Convert.ToHexString(hmac.ComputeHash(data)).ToLowerInvariant();
    }

    public static bool Verify(string secret, string? timestampHeader, string? signatureHeader, ReadOnlySpan<byte> body, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signatureHeader)) return false;
        if (!long.TryParse(timestampHeader, out var ts)) return false;
        var sent = DateTimeOffset.FromUnixTimeSeconds(ts);
        if ((now - sent).Duration() > MaxClockSkew) return false;

        var expected = Encoding.ASCII.GetBytes(Sign(secret, ts, body));
        var actual = Encoding.ASCII.GetBytes(signatureHeader.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
