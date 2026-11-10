using Microsoft.EntityFrameworkCore;

using Rotadesk.Domain;

namespace Rotadesk.Infrastructure.Tests;

public class RoundTripTests(PostgresFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Everything_reads_back_as_it_was_saved()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        await using var db = fixture.For(data.B.Business.Id);

        var business = await db.Businesses.SingleAsync(b => b.Id == data.B.Business.Id, Token);
        var staff = await db.StaffMembers.SingleAsync(s => s.Id == data.B.Staff.Id, Token);
        var shift = await db.Shifts.SingleAsync(s => s.Id == data.B.Shift.Id, Token);
        var window = await db.Availability.SingleAsync(a => a.Id == data.B.Window.Id, Token);
        var swap = await db.SwapRequests.SingleAsync(s => s.Id == data.B.Swap.Id, Token);

        Assert.Equal("Asia/Karachi", business.TimeZoneId);
        Assert.Equal(StaffRole.Staff, staff.Role);
        Assert.Equal(StaffMember.DefaultWeeklyLimit, staff.WeeklyLimit);
        Assert.Equal(data.B.Shift.Start, shift.Start);
        Assert.Equal(data.B.Shift.End, shift.End);
        Assert.Equal(DayOfWeek.Friday, window.Day);
        Assert.Equal(Availability.EndOfDay, window.To);
        Assert.Equal(SwapStatus.Requested, swap.Status);
        Assert.Equal(data.B.ColleaguesShift.Id, swap.ReturnShiftId);
    }
}
