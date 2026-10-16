namespace BlueProcure.Domain.Common;

internal static class Guard
{
    public static string Required(string? value, string code)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException(code);
        }

        return value.Trim();
    }

    public static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
