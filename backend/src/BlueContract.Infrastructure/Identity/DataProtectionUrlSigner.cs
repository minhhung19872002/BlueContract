using System.Security.Cryptography;
using BlueContract.Application.Common;
using Microsoft.AspNetCore.DataProtection;

namespace BlueContract.Infrastructure.Identity;

/// <summary>Time-limited, tamper-proof tokens for links handed to external services (ONLYOFFICE file/callback URLs).</summary>
internal sealed class DataProtectionUrlSigner(IDataProtectionProvider provider) : IUrlSigner
{
    private readonly ITimeLimitedDataProtector _protector = provider.CreateProtector("BlueProcure.UrlSigner.v1").ToTimeLimitedDataProtector();

    public string Sign(string value, TimeSpan lifetime) => _protector.Protect(value, lifetime);

    public bool TryValidate(string? token, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            value = _protector.Unprotect(token, out _);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
