using BlueContract.Application.Common;

namespace BlueContract.Application.Admin;

/// <summary>SMTP settings in effect. The password is never returned, only whether one is set.</summary>
public sealed record MailSettingsDto(
    bool Enabled,
    string Host,
    int Port,
    bool UseStartTls,
    string? UserName,
    bool HasPassword,
    string FromAddress,
    string FromName,
    bool IsOverridden);

/// <summary>Password: null keeps the saved one, empty removes it.</summary>
public sealed record SaveMailSettingsRequest(
    bool Enabled,
    string Host,
    int Port,
    bool UseStartTls,
    string? UserName,
    string? Password,
    string FromAddress,
    string FromName);

public sealed record TestMailRequest(string To, SaveMailSettingsRequest Settings);

public sealed record SigningProviderOptionDto(string Code, string Name);

public sealed record RemoteCaSettingsDto(string Code, string Name, string? BaseUrl, string? ClientId, bool HasSecret);

/// <summary>Signing settings in effect. Mock providers are switched on/off by the server configuration only.</summary>
public sealed record SigningSettingsDto(
    string DefaultProvider,
    string? TsaUrl,
    bool InitialRequiresCa,
    int MockRemoteDelaySeconds,
    bool EnableMockProviders,
    IReadOnlyList<SigningProviderOptionDto> Providers,
    IReadOnlyList<RemoteCaSettingsDto> RemoteCas,
    bool IsOverridden);

/// <summary>ClientSecret: null keeps the saved one, empty removes it.</summary>
public sealed record RemoteCaSettingsRequest(string Code, string? BaseUrl, string? ClientId, string? ClientSecret);

public sealed record SaveSigningSettingsRequest(
    string DefaultProvider,
    string? TsaUrl,
    bool InitialRequiresCa,
    int MockRemoteDelaySeconds,
    IReadOnlyList<RemoteCaSettingsRequest>? RemoteCas);

/// <summary>"Cấu hình email" and "Cấu hình ký số" (spec §14): values saved here override appsettings / environment.</summary>
public interface ISystemConfigAdmin
{
    Task<MailSettingsDto> GetMailAsync(CancellationToken ct);

    Task<Result<MailSettingsDto>> SaveMailAsync(SaveMailSettingsRequest request, CancellationToken ct);

    /// <summary>Sends a test email with the given (possibly unsaved) settings.</summary>
    Task<Result> TestMailAsync(TestMailRequest request, CancellationToken ct);

    Task ResetMailAsync(CancellationToken ct);

    Task<SigningSettingsDto> GetSigningAsync(CancellationToken ct);

    Task<Result<SigningSettingsDto>> SaveSigningAsync(SaveSigningSettingsRequest request, CancellationToken ct);

    Task ResetSigningAsync(CancellationToken ct);
}
