using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BlueContract.Application.Integration;

/// <summary>
/// HMAC-SHA256 request signature of the HIS inbound API (spec §7.2, docs/HIS_INTEGRATION.md):
/// <code>
/// canonical = METHOD + "\n" + PATH_AND_QUERY + "\n" + X-Timestamp + "\n" + hex(SHA256(body))
/// X-Signature = hex(HMAC_SHA256(secret, canonical))   (lower-case)
/// </code>
/// X-Timestamp is Unix time in seconds (UTC); requests outside ±5 minutes are rejected (anti-replay).
/// </summary>
public static class HisRequestSignature
{
    public static readonly TimeSpan AllowedSkew = TimeSpan.FromMinutes(5);

    public static string Canonical(string method, string pathAndQuery, string timestamp, ReadOnlySpan<byte> body) =>
        string.Join('\n', method.ToUpperInvariant(), pathAndQuery, timestamp, Convert.ToHexStringLower(SHA256.HashData(body)));

    public static string Sign(string secret, string method, string pathAndQuery, string timestamp, ReadOnlySpan<byte> body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(Canonical(method, pathAndQuery, timestamp, body))));

    /// <summary>Constant-time comparison of the presented signature (hex, case-insensitive).</summary>
    public static bool Verify(string secret, string method, string pathAndQuery, string timestamp, ReadOnlySpan<byte> body, string? presented)
    {
        if (string.IsNullOrWhiteSpace(presented))
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Sign(secret, method, pathAndQuery, timestamp, body));
        var actual = Encoding.ASCII.GetBytes(presented.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>Parses X-Timestamp (Unix seconds) and checks it is within ±<see cref="AllowedSkew"/> of <paramref name="now"/>.</summary>
    public static bool IsFresh(string? timestamp, DateTimeOffset now)
    {
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        DateTimeOffset at;
        try
        {
            at = DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        return (now - at).Duration() <= AllowedSkew;
    }

    public static string Timestamp(DateTimeOffset now) => now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
}
