using BlueProcure.Domain.Identity;

namespace BlueProcure.Application.Auth;

public interface IPasswordService
{
    string Hash(User user, string password);

    /// <summary>Returns (valid, needsRehash).</summary>
    (bool Valid, bool NeedsRehash) Verify(User user, string password);
}

public interface ITokenService
{
    AccessToken CreateAccessToken(User user, IReadOnlyCollection<string> permissions);

    /// <summary>Short-lived token proving the password step passed, used to complete the 2FA step.</summary>
    string CreateTwoFactorChallenge(User user);

    /// <summary>Returns the user id if the challenge is valid and not expired.</summary>
    Guid? ValidateTwoFactorChallenge(string challenge, out string? securityStamp);

    /// <summary>Random opaque refresh token and its storage hash.</summary>
    (string Token, string Hash) CreateRefreshToken();

    string HashRefreshToken(string token);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface ITotpService
{
    string GenerateSecret();

    bool Verify(string secret, string code, DateTimeOffset now);

    string BuildOtpAuthUri(string secret, string accountName, string issuer);
}

/// <summary>Encrypts secrets at rest (TOTP secrets).</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Issuer { get; set; } = "BlueProcure";

    public string Audience { get; set; } = "BlueProcure";

    /// <summary>HMAC-SHA256 signing key, at least 32 bytes. Must be set from secrets in production.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 7;

    public int MaxFailedLogins { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    public int TwoFactorChallengeMinutes { get; set; } = 5;

    /// <summary>Login attempts allowed per client IP per minute (rate limiter).</summary>
    public int LoginRateLimitPerMinute { get; set; } = 10;
}
