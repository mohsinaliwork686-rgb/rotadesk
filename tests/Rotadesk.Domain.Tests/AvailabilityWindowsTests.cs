namespace Rotadesk.Domain.Tests;

public class AvailabilityWindowsTests
{
    private static DayWindow W(DayOfWeek day, int from, int to) =>
        new(day, new TimeOnly(from, 0), to == 24 ? Availability.EndOfDay : new TimeOnly(to, 0));

    [Fact]
    public void Overlapping_windows_on_a_day_become_one()
    {
        var merged = AvailabilityWindows.Merge([W(DayOfWeek.Monday, 9, 14), W(DayOfWeek.Monday, 12, 17)]);

        Assert.Equal([W(DayOfWeek.Monday, 9, 17)], merged);
    }

    [Fact]
    public void Touching_windows_become_one()
    {
        var merged = AvailabilityWindows.Merge([W(DayOfWeek.Monday, 13, 17), W(DayOfWeek.Monday, 9, 13)]);

        Assert.Equal([W(DayOfWeek.Monday, 9, 17)], merged);
    }

    [Fact]
    public void A_window_inside_another_disappears_into_it()
    {
        var merged = AvailabilityWindows.Merge([W(DayOfWeek.Tuesday, 8, 20), W(DayOfWeek.Tuesday, 10, 12)]);

        Assert.Equal([W(DayOfWeek.Tuesday, 8, 20)], merged);
    }

    [Fact]
    public void Separate_windows_and_other_days_stay_apart_monday_first()
    {
        var merged = AvailabilityWindows.Merge([W(DayOfWeek.Sunday, 10, 14), W(DayOfWeek.Monday, 18, 24), W(DayOfWeek.Monday, 9, 12)]);

        Assert.Equal([W(DayOfWeek.Monday, 9, 12), W(DayOfWeek.Monday, 18, 24), W(DayOfWeek.Sunday, 10, 14)], merged);
    }

    [Fact]
    public void A_window_that_ends_before_it_starts_is_refused()
    {
        var error = Assert.Throws<DomainException>(() => AvailabilityWindows.Merge([W(DayOfWeek.Monday, 17, 9)]));

        Assert.Equal("availability.invalid-times", error.Code);
    }
}
