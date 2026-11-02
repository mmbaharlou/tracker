using System.Net;

namespace Tracker.Collector.Security;

public static class IpRange
{
    /// <summary>Matches "1.2.3.4" or CIDR "10.0.0.0/8".</summary>
    public static bool Matches(IPAddress? ip, string rule)
    {
        if (ip == null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var parts = rule.Split('/');
        if (!IPAddress.TryParse(parts[0], out var net)) return false;
        if (parts.Length == 1) return net.Equals(ip);
        if (!int.TryParse(parts[1], out var bits)) return false;
        var a = ip.GetAddressBytes(); var b = net.GetAddressBytes();
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length && bits > 0; i++, bits -= 8)
        {
            var mask = bits >= 8 ? 0xFF : (byte)(0xFF << (8 - bits));
            if ((a[i] & mask) != (b[i] & mask)) return false;
        }
        return true;
    }
}
