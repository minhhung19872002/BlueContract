namespace BlueContract.Application.Common;

/// <summary>Business error with a stable code; the Vietnamese message is resolved by <see cref="ErrorMessages"/>.</summary>
public sealed record Error(string Code, ErrorKind Kind = ErrorKind.Validation, string? Detail = null)
{
    public static Error NotFound(string code = "NOT_FOUND") => new(code, ErrorKind.NotFound);

    public static Error Forbidden(string code = "FORBIDDEN") => new(code, ErrorKind.Forbidden);

    public static Error Conflict(string code = "CONFLICT") => new(code, ErrorKind.Conflict);

    public static Error Unauthorized(string code = "UNAUTHORIZED") => new(code, ErrorKind.Unauthorized);

    public static Error Validation(string code) => new(code, ErrorKind.Validation);
}

public enum ErrorKind
{
    Validation = 0,
    NotFound = 1,
    Forbidden = 2,
    Conflict = 3,
    Unauthorized = 4,
}
