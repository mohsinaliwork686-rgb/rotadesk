using Rotadesk.Domain;

using static Rotadesk.Domain.Tests.Fixtures;

namespace Rotadesk.Domain.Tests;

public class ShiftTests
{
    [Theory]
    [InlineData(17, 9)]
    [InlineData(9, 9)]
    public void A_shift_has_to_end_after_it_starts(int fromHour, int toHour)
    {
        var error = Assert.Throws<DomainException>(() => ShiftFor(null, Monday, fromHour, toHour));
        Assert.Equal("shift.invalid-times", error.Code);
    }

    [Fact]
    public void A_shift_over_16_hours_is_refused()
    {
        var error = Assert.Throws<DomainException>(() => new Shift(Tenant, RotaWeekId, At(Monday, 6), At(Monday, 22, 1)));
        Assert.Equal("shift.too-long", error.Code);
    }

    [Fact]
    public void A_shift_of_exactly_16_hours_is_allowed()
    {
        var shift = new Shift(Tenant, RotaWeekId, At(Monday, 6), At(Monday, 22));
        Assert.Equal(TimeSpan.FromHours(16), shift.Length);
    }

    [Fact]
    public void Times_are_stored_in_utc()
    {
        var start = new DateTimeOffset(2026, 11, 16, 14, 0, 0, TimeSpan.FromHours(5));
        var shift = new Shift(Tenant, RotaWeekId, start, start.AddHours(8));

        Assert.Equal(TimeSpan.Zero, shift.Start.Offset);
        Assert.Equal(start, shift.Start);
    }
}
