using Rotadesk.Domain;

namespace Rotadesk.Domain.Tests;

/// <summary>A small café in London, so local time is UTC in November and BST before 25 October 2026.</summary>
internal static class Fixtures
{
    public static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    public static readonly TimeZoneInfo Karachi = TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi");
    public static readonly Guid Tenant = Guid.NewGuid();
    public static readonly Guid RotaWeekId = Guid.NewGuid();

    /// <summary>The week of Monday 16 November 2026.</summary>
    public static readonly DateOnly Monday = new(2026, 11, 16);

    public static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    public static StaffMember Person(string name = "Amira", StaffRole role = StaffRole.Staff, double limitHours = 40, Guid? tenant = null) =>
        new(tenant ?? Tenant, name, $"{name.ToLowerInvariant()}@example.test", role, TimeSpan.FromHours(limitHours));

    /// <summary>A UTC instant from a London wall-clock time.</summary>
    public static DateTimeOffset At(DateOnly day, int hour, int minute = 0, TimeZoneInfo? zone = null)
    {
        var local = day.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone ?? London), TimeSpan.Zero);
    }

    public static Shift ShiftFor(StaffMember? person, DateOnly day, int fromHour, int toHour, int fromMinute = 0, int toMinute = 0) =>
        new(Tenant, RotaWeekId, At(day, fromHour, fromMinute), At(day, toHour, toMinute), person?.Id);

    public static ScheduleCheck Check(StaffMember person, Shift candidate, IEnumerable<Shift> existing, IEnumerable<Availability>? availability = null, TimeZoneInfo? zone = null) =>
        SchedulingRules.Check(person, candidate.Id, candidate.Start, candidate.End, existing, availability ?? [], zone ?? London);
}
