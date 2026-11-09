using static Rotadesk.Domain.Tests.Fixtures;

namespace Rotadesk.Domain.Tests;

public class WeeklyLimitTests
{
    private static readonly DateOnly Sunday = Monday.AddDays(-1);

    [Fact]
    public void One_minute_over_the_limit_is_refused()
    {
        var amira = Person(limitHours: 16);
        var existing = ShiftFor(amira, Monday, 9, 17);
        var candidate = ShiftFor(amira, Monday.AddDays(1), 9, 17, toMinute: 1);

        var check = Check(amira, candidate, [existing]);

        var violation = Assert.Single(check.Violations);
        Assert.Equal("shift.weekly-limit", violation.Code);
        Assert.Contains("16h 1m", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Exactly_at_the_limit_is_allowed()
    {
        var amira = Person(limitHours: 16);
        var check = Check(amira, ShiftFor(amira, Monday.AddDays(1), 9, 17), [ShiftFor(amira, Monday, 9, 17)]);

        Assert.True(check.IsAllowed);
    }

    [Fact]
    public void A_shift_across_sunday_midnight_counts_only_its_sunday_hours_towards_that_week()
    {
        var amira = Person(limitHours: 8);
        var sundayDay = ShiftFor(amira, Sunday, 10, 16);
        var overnight = new Shift(Tenant, RotaWeekId, At(Sunday, 22), At(Monday, 4), amira.Id);

        // 6h on Sunday plus the 2h before midnight is exactly 8h in that week.
        Assert.True(Check(amira, overnight, [sundayDay]).IsAllowed);
    }

    [Fact]
    public void The_hours_after_sunday_midnight_count_towards_the_next_week()
    {
        var amira = Person(limitHours: 8);
        var mondayDay = ShiftFor(amira, Monday, 9, 15);
        var overnight = new Shift(Tenant, RotaWeekId, At(Sunday, 22), At(Monday, 4), amira.Id);

        // 6h on Monday plus the 4h after midnight is 10h in the week of 16 November.
        var violation = Assert.Single(Check(amira, overnight, [mondayDay]).Violations);
        Assert.Equal("shift.weekly-limit", violation.Code);
        Assert.Contains("16 Nov", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Someone_elses_hours_do_not_count()
    {
        var amira = Person(limitHours: 8);
        var check = Check(amira, ShiftFor(amira, Monday, 9, 17), [ShiftFor(Person("Ben"), Monday.AddDays(1), 9, 17)]);

        Assert.True(check.IsAllowed);
    }
}
