using BlueProcure.Application.Common;

namespace BlueProcure.Infrastructure;

internal sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    // IANA id works on Linux containers and on Windows (.NET uses ICU for IANA ids).
    private static readonly TimeZoneInfo VietnamZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    public DateOnly TodayLocal => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, VietnamZone).DateTime);
}
