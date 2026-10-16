namespace BlueContract.Application.Common;

/// <summary>Abstraction over time; business dates are interpreted in Asia/Ho_Chi_Minh, stored as UTC.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    DateOnly TodayLocal { get; }
}

/// <summary>Times shown to people (emails, notifications, documents) are Vietnam time (UTC+7, no daylight saving).</summary>
public static class VietnamTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    /// <summary>"dd/MM/yyyy HH:mm" in Vietnam time.</summary>
    public static string Format(DateTimeOffset at) =>
        at.ToOffset(Offset).ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
