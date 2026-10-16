using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BlueProcure.Application.Auth;
using BlueProcure.Application.Common;
using BlueProcure.Domain.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BlueProcure.Infrastructure.Identity;

internal sealed class PasswordService : IPasswordService
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public (bool Valid, bool NeedsRehash) Verify(User user, string password)
    {
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            return (false, false);
        }

        return _hasher.VerifyHashedPassword(user, user.PasswordHash, password) switch
        {
            PasswordVerificationResult.Success => (true, false),
            PasswordVerificationResult.SuccessRehashNeeded => (true, true),
            _ => (false, false),
        };
    }
}

/// <summary>Claim names shared by token issuance and the API's ICurrentUser.</summary>
public static class AppClaims
{
    public const string UserId = "sub";
    public const string UserName = "name";
    public const string FullName = "full_name";
    public const string UserType = "user_type";
    public const string SupplierId = "supplier_id";
    public const string Permission = "perm";
    public const string Scope = "dscope";
    public const string SecurityStamp = "stamp";
    public const string Purpose = "purpose";
    public const string MustChangePassword = "mcp";
}

public sealed class JwtTokenService(IOptions<AuthOptions> options, IClock clock) : ITokenService
{
    private const string TwoFactorPurpose = "2fa";
    private readonly AuthOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public static SymmetricSecurityKey CreateKey(AuthOptions options)
    {
        var bytes = Encoding.UTF8.GetBytes(options.SigningKey);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Auth:SigningKey must be at least 32 bytes.");
        }

        return new SymmetricSecurityKey(bytes);
    }

    public AccessToken CreateAccessToken(User user, IReadOnlyCollection<string> permissions)
    {
        var now = clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(AppClaims.UserId, user.Id.ToString()),
            new(AppClaims.UserName, user.UserName),
            new(AppClaims.FullName, user.FullName),
            new(AppClaims.UserType, user.UserType.ToString()),
            new(AppClaims.SecurityStamp, user.SecurityStamp),
        };
        if (user.SupplierId is { } supplierId)
        {
            claims.Add(new Claim(AppClaims.SupplierId, supplierId.ToString()));
        }

        if (user.MustChangePassword)
        {
            claims.Add(new Claim(AppClaims.MustChangePassword, "true"));
        }

        claims.AddRange(permissions.Select(p => new Claim(AppClaims.Permission, p)));
        claims.AddRange(user.DataScopes.Select(s => new Claim(AppClaims.Scope, $"{s.ScopeType}:{s.Value}")));

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(CreateKey(_options), SecurityAlgorithms.HmacSha256),
        });
        return new AccessToken(token, expires);
    }

    public string CreateTwoFactorChallenge(User user)
    {
        var now = clock.UtcNow;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience + ":2fa",
            Subject = new ClaimsIdentity(
            [
                new Claim(AppClaims.UserId, user.Id.ToString()),
                new Claim(AppClaims.SecurityStamp, user.SecurityStamp),
                new Claim(AppClaims.Purpose, TwoFactorPurpose),
            ]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(_options.TwoFactorChallengeMinutes).UtcDateTime,
            SigningCredentials = new SigningCredentials(CreateKey(_options), SecurityAlgorithms.HmacSha256),
        });
    }

    public Guid? ValidateTwoFactorChallenge(string challenge, out string? securityStamp)
    {
        securityStamp = null;
        var result = _handler.ValidateTokenAsync(challenge, new TokenValidationParameters
        {
            ValidIssuer = _options.Issuer,
            ValidAudience = _options.Audience + ":2fa",
            IssuerSigningKey = CreateKey(_options),
            ClockSkew = TimeSpan.FromSeconds(30),
            LifetimeValidator = (_, expires, _, _) => expires is not null && expires.Value > clock.UtcNow.UtcDateTime,
        }).GetAwaiter().GetResult();

        if (!result.IsValid
            || !result.Claims.TryGetValue(AppClaims.Purpose, out var purpose) || purpose as string != TwoFactorPurpose
            || !result.Claims.TryGetValue(AppClaims.UserId, out var sub) || !Guid.TryParse(sub as string, out var userId))
        {
            return null;
        }

        securityStamp = result.Claims.TryGetValue(AppClaims.SecurityStamp, out var stamp) ? stamp as string : null;
        return userId;
    }

    public (string Token, string Hash) CreateRefreshToken()
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        return (token, HashRefreshToken(token));
    }

    public string HashRefreshToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>RFC 6238 TOTP (SHA-1, 6 digits, 30 s step) — compatible with Google/Microsoft Authenticator.</summary>
internal sealed class TotpService : ITotpService
{
    private const int Digits = 6;
    private const int StepSeconds = 30;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public string GenerateSecret() => ToBase32(RandomNumberGenerator.GetBytes(20));

    public bool Verify(string secret, string code, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != Digits || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        var key = FromBase32(secret);
        var counter = now.ToUnixTimeSeconds() / StepSeconds;

        // Accept one step of clock drift either way.
        for (var drift = -1; drift <= 1; drift++)
        {
            if (CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(Compute(key, counter + drift)),
                Encoding.ASCII.GetBytes(code)))
            {
                return true;
            }
        }

        return false;
    }

    public string BuildOtpAuthUri(string secret, string accountName, string issuer) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits={Digits}&period={StepSeconds}";

    internal static string Compute(byte[] key, long counter)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);
#pragma warning disable CA5350 // RFC 6238 TOTP mandates HMAC-SHA1 for authenticator-app compatibility
        var hash = HMACSHA1.HashData(key, counterBytes);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static string ToBase32(byte[] data)
    {
        var sb = new StringBuilder((data.Length + 4) / 5 * 8);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return sb.ToString();
    }

    internal static byte[] FromBase32(string input)
    {
        var clean = input.Trim().TrimEnd('=').Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var value = Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("Invalid base32 character.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }
}

internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("BlueProcure.Secrets.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
