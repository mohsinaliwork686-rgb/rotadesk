using static Rotadesk.Domain.Tests.Fixtures;

namespace Rotadesk.Domain.Tests;

public class OverlapTests
{
    [Fact]
    public void Shifts_overlapping_by_one_minute_are_refused()
    {
        var amira = Person();
        var existing = ShiftFor(amira, Monday, 9, 17);
        var candidate = ShiftFor(amira, Monday, 16, 22, fromMinute: 59);

        var check = Check(amira, candidate, [existing]);

        Assert.Equal("shift.overlap", Assert.Single(check.Violations).Code);
    }

    [Fact]
    public void Back_to_back_shifts_are_allowed()
    {
        var amira = Person();
        var check = Check(amira, ShiftFor(amira, Monday, 17, 22), [ShiftFor(amira, Monday, 9, 17)]);

        Assert.True(check.IsAllowed);
    }

    [Fact]
    public void An_edited_shift_does_not_clash_with_itself()
    {
        var amira = Person();
        var shift = ShiftFor(amira, Monday, 9, 17);

        var check = SchedulingRules.Check(amira, shift.Id, At(Monday, 10), At(Monday, 18), [shift], [], London);

        Assert.True(check.IsAllowed);
    }

    [Fact]
    public void Someone_elses_shift_at_the_same_time_is_ignored()
    {
        var amira = Person();
        var check = Check(amira, ShiftFor(amira, Monday, 9, 17), [ShiftFor(Person("Ben"), Monday, 9, 17)]);

        Assert.True(check.IsAllowed);
    }
}
