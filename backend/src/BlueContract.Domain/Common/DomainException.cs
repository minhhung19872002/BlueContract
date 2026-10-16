namespace BlueContract.Domain.Common;

/// <summary>
/// Raised when a business rule is violated inside the domain model.
/// <see cref="Code"/> is a stable machine code (e.g. CONTRACT_QTY_EXCEEDED) translated to Vietnamese in one place.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string code, string? message = null)
        : base(message ?? code)
    {
        Code = code;
    }

    public DomainException()
        : this("DOMAIN_ERROR")
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "DOMAIN_ERROR";
    }

    public string Code { get; }
}
