namespace BlueContract.Domain.Integration;

/// <summary>
/// A signature of an accepted write call from the HIS. Kept a little longer than the allowed clock skew so that the same
/// signed request is accepted once across every API instance (AUDIT-1 §10.9); the primary key decides the race.
/// </summary>
public sealed class UsedRequestSignature
{
    private UsedRequestSignature()
    {
        Signature = string.Empty;
    }

    public UsedRequestSignature(string signature, DateTimeOffset receivedAt)
    {
        Signature = signature;
        ReceivedAt = receivedAt;
    }

    public string Signature { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }
}
