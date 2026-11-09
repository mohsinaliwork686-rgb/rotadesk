using Rotadesk.Domain;

using static Rotadesk.Domain.Tests.Fixtures;

namespace Rotadesk.Domain.Tests;

public class WeekTests
{
    [Fact]
    public void A_week_starts_on_a_monday()
    {
        var error = Assert.Throws<DomainException>(() => Domain.Week.Starting(Monday.AddDays(1), London));
        Assert.Equal("week.not-monday", error.Code);
    }

    [Fact]
    public void Sunday_evening_belongs_to_the_week_before()
    {
        var week = Domain.Week.Containing(At(Monday.AddDays(-1), 23, 59), London);
        Assert.Equal(Monday.AddDays(-7), week.Start);
    }

    [Fact]
    public void A_week_follows_local_time_across_the_clock_change()
    {
        // Clocks go back on Sunday 25 October 2026, so the week of 19 October starts at 23:00 UTC on Sunday
        // and is an hour longer than usual.
        var monday19 = new DateOnly(2026, 10, 19);
        var week = Domain.Week.Starting(monday19, London);

        Assert.Equal(new DateTimeOffset(2026, 10, 18, 23, 0, 0, TimeSpan.Zero), week.StartUtc);
        Assert.Equal(TimeSpan.FromHours((7 * 24) + 1), week.EndUtc - week.StartUtc);
        Assert.Equal(monday19, Domain.Week.Containing(new DateTimeOffset(2026, 10, 18, 23, 30, 0, TimeSpan.Zero), London).Start);
    }

    [Fact]
    public void A_rota_week_starts_on_a_monday_and_publishes_once()
    {
        Assert.Throws<DomainException>(() => new RotaWeek(Tenant, Monday.AddDays(2)));

        var rota = new RotaWeek(Tenant, Monday);
        rota.Publish(Now);

        Assert.True(rota.IsPublished);
        Assert.Equal("rota.already-published", Assert.Throws<DomainException>(() => rota.Publish(Now)).Code);
    }
}
