using System.Text.RegularExpressions;
using BlueContract.Domain.Common;

namespace BlueContract.Domain.Organizations;

/// <summary>Nhà cung cấp. Tax code is the business key used with HIS (Số QĐ + MST).</summary>
public sealed partial class Supplier : Entity, IHasHistory
{
    private Supplier()
    {
        TaxCode = string.Empty;
        Name = string.Empty;
    }

    public Supplier(string taxCode, string name)
    {
        TaxCode = NormalizeTaxCode(taxCode);
        Name = Guard.Required(name, "SUPPLIER_NAME_REQUIRED");
        IsActive = true;
    }

    public string TaxCode { get; private set; }

    public string Name { get; private set; }

    public string? ShortName { get; private set; }

    public string? Address { get; private set; }

    public string? RepresentativeName { get; private set; }

    public string? RepresentativeTitle { get; private set; }

    /// <summary>Giấy ủy quyền (number/date) when the signer is not the legal representative.</summary>
    public string? AuthorizationDocument { get; private set; }

    public string? BankAccountNumber { get; private set; }

    public string? BankName { get; private set; }

    public string? NotificationEmail { get; private set; }

    public string? Phone { get; private set; }

    public bool IsActive { get; private set; }


    public void Update(
        string name,
        string? shortName,
        string? address,
        string? representativeName,
        string? representativeTitle,
        string? authorizationDocument,
        string? bankAccountNumber,
        string? bankName,
        string? notificationEmail,
        string? phone,
        bool isActive)
    {
        Name = Guard.Required(name, "SUPPLIER_NAME_REQUIRED");
        ShortName = Guard.Optional(shortName);
        Address = Guard.Optional(address);
        RepresentativeName = Guard.Optional(representativeName);
        RepresentativeTitle = Guard.Optional(representativeTitle);
        AuthorizationDocument = Guard.Optional(authorizationDocument);
        BankAccountNumber = Guard.Optional(bankAccountNumber);
        BankName = Guard.Optional(bankName);
        NotificationEmail = Guard.Optional(notificationEmail);
        Phone = Guard.Optional(phone);
        IsActive = isActive;
    }

    /// <summary>Vietnamese tax code: 10 digits, or 10 digits + "-" + 3 digits for branches.</summary>
    public static string NormalizeTaxCode(string? taxCode)
    {
        var value = Guard.Required(taxCode, "SUPPLIER_TAX_CODE_REQUIRED").Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!TaxCodePattern().IsMatch(value))
        {
            throw new DomainException("SUPPLIER_TAX_CODE_INVALID");
        }

        return value;
    }

    [GeneratedRegex(@"^\d{10}(-\d{3})?$")]
    private static partial Regex TaxCodePattern();
}
