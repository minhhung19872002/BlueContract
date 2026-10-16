using BlueProcure.Domain.Identity;
using FluentValidation;

namespace BlueProcure.Application.Auth;

public sealed record LoginRequest(string UserName, string Password);

public sealed record TwoFactorLoginRequest(string Challenge, string Code);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record EnableTwoFactorRequest(string Code);

public sealed record DisableTwoFactorRequest(string Password);

public sealed record TwoFactorSetupResponse(string Secret, string OtpAuthUri);

/// <summary>Result of a login step. Either tokens are issued, or a 2FA challenge must be answered.</summary>
public sealed record LoginResult
{
    public bool RequiresTwoFactor { get; init; }

    public string? TwoFactorChallenge { get; init; }

    public string? AccessToken { get; init; }

    public DateTimeOffset? AccessTokenExpiresAt { get; init; }

    public CurrentUserDto? User { get; init; }

    /// <summary>Raw refresh token; the API puts it in an httpOnly cookie and never in the body.</summary>
    internal string? RefreshToken { get; init; }

    internal DateTimeOffset? RefreshTokenExpiresAt { get; init; }

    public (string Token, DateTimeOffset ExpiresAt)? GetRefreshCookie() =>
        RefreshToken is null || RefreshTokenExpiresAt is null ? null : (RefreshToken, RefreshTokenExpiresAt.Value);
}

public sealed record CurrentUserDto(
    Guid Id,
    string UserName,
    string FullName,
    string? Email,
    string? JobTitle,
    UserType UserType,
    Guid? SupplierId,
    string? SupplierName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? FacilityId,
    string? FacilityName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> RoleNames,
    IReadOnlyList<string> Permissions,
    bool TwoFactorEnabled,
    bool MustChangePassword);

internal sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().WithName("Tên đăng nhập").MaximumLength(64);
        RuleFor(x => x.Password).NotEmpty().WithName("Mật khẩu").MaximumLength(128);
    }
}

internal sealed class TwoFactorLoginRequestValidator : AbstractValidator<TwoFactorLoginRequest>
{
    public TwoFactorLoginRequestValidator()
    {
        RuleFor(x => x.Challenge).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().WithName("Mã xác thực").Matches("^[0-9]{6}$").WithMessage("Mã xác thực gồm 6 chữ số.");
    }
}

internal sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithName("Mật khẩu hiện tại");
        RuleFor(x => x.NewPassword).NotEmpty().WithName("Mật khẩu mới")
            .Must(PasswordPolicy.IsStrong)
            .WithMessage("Mật khẩu mới phải có ít nhất 8 ký tự, gồm chữ hoa, chữ thường, chữ số và ký tự đặc biệt.")
            .NotEqual(x => x.CurrentPassword).WithMessage("Mật khẩu mới phải khác mật khẩu hiện tại.");
    }
}
