namespace BlueProcure.Application.Common;

/// <summary>Abstraction over time; business dates are interpreted in Asia/Ho_Chi_Minh, stored as UTC.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    DateOnly TodayLocal { get; }
}
