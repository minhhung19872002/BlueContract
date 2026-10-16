using BlueProcure.Domain.Common;

namespace BlueProcure.Domain.Identity;

/// <summary>
/// Ủy quyền ký: e.g. the Director lets a Deputy Director sign on their behalf in a date range.
/// Documents then show "KT. GIÁM ĐỐC / PHÓ GIÁM ĐỐC".
/// </summary>
public sealed class SigningDelegation : Entity
{
    private SigningDelegation()
    {
        DelegatorTitle = string.Empty;
        DelegateTitle = string.Empty;
    }

    public SigningDelegation(
        Guid delegatorUserId,
        Guid delegateUserId,
        string delegatorTitle,
        string delegateTitle,
        DateTimeOffset validFrom,
        DateTimeOffset validTo,
        string? reason)
    {
        if (delegatorUserId == delegateUserId)
        {
            throw new DomainException("DELEGATION_SAME_USER");
        }

        if (validTo <= validFrom)
        {
            throw new DomainException("DELEGATION_INVALID_PERIOD");
        }

        DelegatorUserId = delegatorUserId;
        DelegateUserId = delegateUserId;
        DelegatorTitle = Guard.Required(delegatorTitle, "DELEGATION_TITLE_REQUIRED");
        DelegateTitle = Guard.Required(delegateTitle, "DELEGATION_TITLE_REQUIRED");
        ValidFrom = validFrom;
        ValidTo = validTo;
        Reason = Guard.Optional(reason);
    }

    public Guid DelegatorUserId { get; private set; }

    public Guid DelegateUserId { get; private set; }

    /// <summary>e.g. "GIÁM ĐỐC".</summary>
    public string DelegatorTitle { get; private set; }

    /// <summary>e.g. "PHÓ GIÁM ĐỐC".</summary>
    public string DelegateTitle { get; private set; }

    public DateTimeOffset ValidFrom { get; private set; }

    public DateTimeOffset ValidTo { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsEffectiveAt(DateTimeOffset at) => RevokedAt is null && at >= ValidFrom && at < ValidTo;

    /// <summary>Signature caption, e.g. "KT. GIÁM ĐỐC" over "PHÓ GIÁM ĐỐC".</summary>
    public (string Line1, string Line2) SignatureCaption() =>
        ($"KT. {DelegatorTitle.ToUpperInvariant()}", DelegateTitle.ToUpperInvariant());

    public void Revoke(DateTimeOffset at)
    {
        if (RevokedAt is not null)
        {
            throw new DomainException("DELEGATION_ALREADY_REVOKED");
        }

        RevokedAt = at;
    }
}
