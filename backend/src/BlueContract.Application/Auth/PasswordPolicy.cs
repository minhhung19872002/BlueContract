namespace BlueContract.Application.Auth;

/// <summary>Password rules (spec §11): ≥ 8 chars with upper, lower, digit and special character.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    public static bool IsStrong(string? password) =>
        password is { Length: >= MinLength and <= 128 }
        && password.Any(char.IsUpper)
        && password.Any(char.IsLower)
        && password.Any(char.IsDigit)
        && password.Any(c => !char.IsLetterOrDigit(c));

    /// <summary>Generates a temporary password that satisfies the policy (for new supplier accounts / resets).</summary>
    public static string GenerateTemporary()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string special = "@#$%&*!";
        const string all = upper + lower + digits + special;

        var chars = new List<char>
        {
            upper[System.Security.Cryptography.RandomNumberGenerator.GetInt32(upper.Length)],
            lower[System.Security.Cryptography.RandomNumberGenerator.GetInt32(lower.Length)],
            digits[System.Security.Cryptography.RandomNumberGenerator.GetInt32(digits.Length)],
            special[System.Security.Cryptography.RandomNumberGenerator.GetInt32(special.Length)],
        };
        while (chars.Count < 12)
        {
            chars.Add(all[System.Security.Cryptography.RandomNumberGenerator.GetInt32(all.Length)]);
        }

        // Fisher–Yates shuffle with CSPRNG.
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = System.Security.Cryptography.RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string([.. chars]);
    }
}
