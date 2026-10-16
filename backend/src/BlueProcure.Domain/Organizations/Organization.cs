using BlueProcure.Domain.Common;

namespace BlueProcure.Domain.Organizations;

/// <summary>The hospital operating the portal. Never hard-code its name — always read from here (spec §1).</summary>
public sealed class Organization : Entity
{
    private Organization()
    {
        Name = string.Empty;
        TaxCode = string.Empty;
    }

    public Organization(string name, string taxCode)
    {
        Name = Guard.Required(name, "ORG_NAME_REQUIRED");
        TaxCode = Guard.Required(taxCode, "ORG_TAX_CODE_REQUIRED");
    }

    public string Name { get; private set; }

    public string? ShortName { get; private set; }

    public string TaxCode { get; private set; }

    public string? Address { get; private set; }

    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    public string? RepresentativeName { get; private set; }

    public string? RepresentativeTitle { get; private set; }

    public string? BankAccountNumber { get; private set; }

    public string? BankName { get; private set; }

    /// <summary>Kho bạc Nhà nước account used for public-fund payments.</summary>
    public string? TreasuryAccountNumber { get; private set; }

    public string? TreasuryName { get; private set; }

    public string? LogoFileKey { get; private set; }

    public string? SealImageFileKey { get; private set; }

    public void Update(
        string name,
        string? shortName,
        string taxCode,
        string? address,
        string? phone,
        string? email,
        string? representativeName,
        string? representativeTitle,
        string? bankAccountNumber,
        string? bankName,
        string? treasuryAccountNumber,
        string? treasuryName)
    {
        Name = Guard.Required(name, "ORG_NAME_REQUIRED");
        ShortName = Guard.Optional(shortName);
        TaxCode = Guard.Required(taxCode, "ORG_TAX_CODE_REQUIRED");
        Address = Guard.Optional(address);
        Phone = Guard.Optional(phone);
        Email = Guard.Optional(email);
        RepresentativeName = Guard.Optional(representativeName);
        RepresentativeTitle = Guard.Optional(representativeTitle);
        BankAccountNumber = Guard.Optional(bankAccountNumber);
        BankName = Guard.Optional(bankName);
        TreasuryAccountNumber = Guard.Optional(treasuryAccountNumber);
        TreasuryName = Guard.Optional(treasuryName);
    }

    public void SetLogo(string? fileKey) => LogoFileKey = fileKey;

    public void SetSealImage(string? fileKey) => SealImageFileKey = fileKey;
}
