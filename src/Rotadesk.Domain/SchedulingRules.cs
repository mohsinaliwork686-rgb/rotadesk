namespace Rotadesk.Domain;

/// <summary>
/// The rota rules for one person: no overlapping shifts and a weekly hours limit, both refused, and
/// availability, which only warns.
/// </summary>
public static class SchedulingRules
{
    /// <summary>
    /// Checks giving <paramref name="member"/> a shift from <paramref name="start"/> to <paramref name="end"/>.
    /// </summary>
    /// <param name="existingShifts">The person's other shifts. A shift with <paramref name="shiftId"/> is the one being
    /// changed and is ignored, so an edited shift never clashes with itself.</param>
    public static ScheduleCheck Check(
        StaffMember member,
        Guid shiftId,
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<Shift> existingShifts,
        IEnumerable<Availability> availability,
        TimeZoneInfo zone)
    {
        var others = existingShifts.Where(s => s.Id != shiftId && s.StaffMemberId == member.Id).ToList();
        var violations = new List<RuleViolation>();

        var clash = others.FirstOrDefault(s => s.Overlaps(start, end));
        if (clash is not null)
        {
            violations.Add(new RuleViolation(
                "shift.overlap",
                $"{member.Name} already works {Describe(clash.Start, clash.End, zone)}."));
        }

        foreach (var week in WeeksTouched(start, end, zone))
        {
            var total = others.Aggregate(week.Overlap(start, end), (sum, s) => sum + week.Overlap(s.Start, s.End));
            if (total > member.WeeklyLimit)
            {
                violations.Add(new RuleViolation(
                    "shift.weekly-limit",
                    $"{member.Name} would work {Hours(total)} in the week of {week.Start:d MMM}, over the limit of {Hours(member.WeeklyLimit)}."));
            }
        }

        var warnings = new List<string>();
        if (!FitsAvailability(start, end, availability.Where(a => a.StaffMemberId == member.Id).ToList(), zone, out var day))
        {
            warnings.Add($"Outside {member.Name}'s availability on {day}.");
        }

        return new ScheduleCheck(violations, warnings);
    }

    private static IEnumerable<Week> WeeksTouched(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        var week = Week.Containing(start, zone);
        while (week.StartUtc < end)
        {
            yield return week;
            week = Week.Starting(week.Start.AddDays(7), zone);
        }
    }

    /// <summary>
    /// Someone who hasn't set any availability gets no warnings. Otherwise the shift has to sit inside a window on the
    /// day it starts, in the business's time. A shift past midnight also needs a window from midnight on the next day.
    /// </summary>
    private static bool FitsAvailability(
        DateTimeOffset start,
        DateTimeOffset end,
        IReadOnlyList<Availability> windows,
        TimeZoneInfo zone,
        out DayOfWeek day)
    {
        var localStart = TimeZoneInfo.ConvertTime(start, zone).DateTime;
        var localEnd = TimeZoneInfo.ConvertTime(end, zone).DateTime;
        day = localStart.DayOfWeek;

        if (windows.Count == 0)
        {
            return true;
        }

        bool Covered(DayOfWeek d, TimeOnly from, TimeOnly to) => windows.Any(w => w.Day == d && w.From <= from && to <= w.To);

        var from = TimeOnly.FromDateTime(localStart);
        if (localEnd.Date == localStart.Date)
        {
            return Covered(day, from, TimeOnly.FromDateTime(localEnd));
        }

        var untilMidnight = Covered(day, from, Availability.EndOfDay);
        return localEnd.TimeOfDay == TimeSpan.Zero
            ? untilMidnight
            : untilMidnight && Covered(localEnd.DayOfWeek, TimeOnly.MinValue, TimeOnly.FromDateTime(localEnd));
    }

    private static string Describe(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        var localStart = TimeZoneInfo.ConvertTime(start, zone);
        var localEnd = TimeZoneInfo.ConvertTime(end, zone);
        return $"{localStart:ddd d MMM HH:mm} to {localEnd:HH:mm}";
    }

    private static string Hours(TimeSpan span) =>
        span.Minutes == 0 ? $"{(int)span.TotalHours}h" : $"{(int)span.TotalHours}h {span.Minutes}m";
}
