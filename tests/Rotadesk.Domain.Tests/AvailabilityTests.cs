using Rotadesk.Domain;

using static Rotadesk.Domain.Tests.Fixtures;

namespace Rotadesk.Domain.Tests;

public class AvailabilityTests
{
    private static readonly DateOnly Friday = Monday.AddDays(4);

    private static Availability Window(StaffMember person, DayOfWeek day, int from, int to) =>
        new(Tenant, person.Id, day, new TimeOnly(from, 0), to == 24 ? TimeOnly.MaxValue : new TimeOnly(to, 0));

    [Fact]
    public void Someone_with_no_availability_set_gets_no_warning()
    {
        var amira = Person();
        Assert.Empty(Check(amira, ShiftFor(amira, Monday, 3, 9), []).Warnings);
    }

    [Fact]
    public void A_shift_inside_a_window_gets_no_warning()
    {
        var amira = Person();
        var check = Check(amira, ShiftFor(amira, Monday, 9, 17), [], [Window(amira, DayOfWeek.Monday, 8, 18)]);

        Assert.Empty(check.Warnings);
    }

    [Fact]
    public void A_shift_outside_the_windows_is_allowed_with_a_warning()
    {
        var amira = Person();
        var check = Check(amira, ShiftFor(amira, Monday, 9, 19), [], [Window(amira, DayOfWeek.Monday, 8, 18)]);

        Assert.True(check.IsAllowed);
        Assert.Equal("Outside Amira's availability on Monday.", Assert.Single(check.Warnings));
    }

    [Fact]
    public void A_window_on_another_day_does_not_count()
    {
        var amira = Person();
        var check = Check(amira, ShiftFor(amira, Monday, 9, 17), [], [Window(amira, DayOfWeek.Tuesday, 8, 18)]);

        Assert.Single(check.Warnings);
    }

    [Fact]
    public void Windows_are_in_the_business_time_zone()
    {
        var amira = Person();
        var windows = new[] { Window(amira, DayOfWeek.Monday, 14, 22) };
        var inside = new Shift(Tenant, RotaWeekId, At(Monday, 14, zone: Karachi), At(Monday, 22, zone: Karachi), amira.Id);
        var outside = new Shift(Tenant, RotaWeekId, At(Monday, 9, zone: Karachi), At(Monday, 17, zone: Karachi), amira.Id);

        Assert.Empty(Check(amira, inside, [], windows, Karachi).Warnings);
        Assert.Single(Check(amira, outside, [], windows, Karachi).Warnings);
    }

    [Fact]
    public void An_overnight_shift_needs_a_window_on_each_day()
    {
        var amira = Person();
        var overnight = new Shift(Tenant, RotaWeekId, At(Friday, 20), At(Friday.AddDays(1), 2), amira.Id);
        var untilMidnight = Window(amira, DayOfWeek.Friday, 18, 24);
        var afterMidnight = Window(amira, DayOfWeek.Saturday, 0, 3);

        Assert.Empty(Check(amira, overnight, [], [untilMidnight, afterMidnight]).Warnings);
        Assert.Single(Check(amira, overnight, [], [untilMidnight]).Warnings);
    }

    [Fact]
    public void A_shift_ending_at_midnight_fits_a_window_until_midnight()
    {
        var amira = Person();
        var evening = new Shift(Tenant, RotaWeekId, At(Friday, 18), At(Friday.AddDays(1), 0), amira.Id);

        Assert.Empty(Check(amira, evening, [], [Window(amira, DayOfWeek.Friday, 17, 24)]).Warnings);
    }

    [Fact]
    public void A_window_has_to_end_after_it_starts()
    {
        var error = Assert.Throws<DomainException>(() => Window(Person(), DayOfWeek.Monday, 18, 9));
        Assert.Equal("availability.invalid-times", error.Code);
    }
}
