namespace Rotadesk.Domain;

/// <summary>A Monday-to-Monday week in a business's local time, as a UTC range.</summary>
public readonly record struct Week(DateOnly Start, DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public static Week Starting(DateOnly monday, TimeZoneInfo zone)
    {
        if (monday.DayOfWeek != DayOfWeek.Monday)
        {
            throw new DomainException("week.not-monday", $"A week starts on a Monday, not on {monday.DayOfWeek}.");
        }

        return new Week(monday, LocalMidnightToUtc(monday, zone), LocalMidnightToUtc(monday.AddDays(7), zone));
    }

    /// <summary>The week, in the business's time, that contains this instant.</summary>
    public static Week Containing(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
        var daysSinceMonday = ((int)local.DayOfWeek + 6) % 7;
        return Starting(local.AddDays(-daysSinceMonday), zone);
    }

    /// <summary>How much of [start, end) falls inside this week.</summary>
    public TimeSpan Overlap(DateTimeOffset start, DateTimeOffset end)
    {
        var from = start > StartUtc ? start : StartUtc;
        var to = end < EndUtc ? end : EndUtc;
        return to > from ? to - from : TimeSpan.Zero;
    }

    private static DateTimeOffset LocalMidnightToUtc(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        // A few zones change their clocks at midnight, so that midnight doesn't exist. The week then starts at
        // the first moment that does.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
}
