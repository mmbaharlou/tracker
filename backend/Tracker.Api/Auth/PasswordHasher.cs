using System.Security.Cryptography;

namespace Tracker.Api.Auth;

/// <summary>PBKDF2-SHA256 with a random salt, stored as "pbkdf2$iterations$salt$hash".</summary>
public static class PasswordHasher
{
    public const int Iterations = 210_000;

    /// <summary>Hash of a random password: verified when the username does not exist, so timing does not reveal valid usernames.</summary>
    public static readonly string Dummy = Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)));

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        try
        {
            var p = stored.Split('$');
            if (p.Length != 4 || p[0] != "pbkdf2") return false;
            var iterations = int.Parse(p[1]);
            if (iterations is < 10_000 or > 5_000_000) return false;
            var expected = Convert.FromBase64String(p[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(p[2]), iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch { return false; }
    }

    /// <summary>True when the hash was made with fewer iterations than today's default (rehash on next login).</summary>
    public static bool NeedsRehash(string stored) => !stored.StartsWith($"pbkdf2${Iterations}$", StringComparison.Ordinal);
}

public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    static readonly string[] Common =
    [
        "password", "123456", "qwerty", "admin", "welcome", "letmein", "iloveyou", "monkey", "dragon", "abc123",
        "111111", "000000", "passw0rd", "p@ssw0rd", "1q2w3e", "qazwsx", "zaq1", "asdfgh",
    ];

    /// <summary>Null when acceptable, otherwise a Persian message for the user.</summary>
    public static string? Check(string? password, string username)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength) return $"رمز عبور باید حداقل {MinLength} کاراکتر باشد.";
        if (password.Length > MaxLength) return $"رمز عبور حداکثر {MaxLength} کاراکتر است.";
        var lower = password.ToLowerInvariant();
        if (username.Length >= 3 && lower.Contains(username.ToLowerInvariant())) return "رمز عبور نباید شامل نام کاربری باشد.";
        if (password.Distinct().Count() < 5) return "رمز عبور خیلی تکراری است؛ از حروف و نمادهای متنوع‌تر استفاده کنید.";
        if (Common.Any(c => lower.Contains(c)) && password.Length < 20) return "رمز عبور شامل الگوی رایج و قابل حدس است.";
        var classes = new[] { password.Any(char.IsLower), password.Any(char.IsUpper), password.Any(char.IsDigit), password.Any(c => !char.IsLetterOrDigit(c)) }.Count(x => x);
        if (classes < 3 && password.Length < 16) return "در رمز عبور حداقل سه مورد از این‌ها باشد: حرف کوچک، حرف بزرگ، عدد، نماد (یا رمز ۱۶ کاراکتری به بالا).";
        return null;
    }
}
