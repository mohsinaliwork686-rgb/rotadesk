using Microsoft.EntityFrameworkCore;

using Rotadesk.Domain;

namespace Rotadesk.Infrastructure.Tests;

public class TenantGuardTests(PostgresFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Adding_a_row_for_another_business_is_refused()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        await using var db = fixture.For(data.A.Business.Id);

        db.Add(new RotaWeek(data.B.Business.Id, TwoBusinesses.Monday.AddDays(7)));

        await Assert.ThrowsAsync<TenantViolationException>(() => db.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task Changing_another_business_row_is_refused()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        await using var db = fixture.For(data.A.Business.Id);

        // A row that came from somewhere other than this context's own queries.
        db.Attach(data.B.Shift);
        data.B.Shift.AssignTo(null);

        await Assert.ThrowsAsync<TenantViolationException>(() => db.SaveChangesAsync(Token));
        await AssertUnchangedAsync(data.B);
    }

    [Fact]
    public async Task Deleting_another_business_row_is_refused()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        await using var db = fixture.For(data.A.Business.Id);

        db.Remove(data.B.Window);

        await Assert.ThrowsAsync<TenantViolationException>(() => db.SaveChangesAsync(Token));
        await using var system = fixture.NewContext(SystemTenant.Instance);
        Assert.NotNull(await system.Availability.FindAsync([data.B.Window.Id], Token));
    }

    [Fact]
    public async Task Moving_a_row_to_another_business_is_refused()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        await using var db = fixture.For(data.A.Business.Id);

        var shift = await db.Shifts.SingleAsync(s => s.Id == data.A.Shift.Id, Token);
        db.Entry(shift).Property(s => s.TenantId).CurrentValue = data.B.Business.Id;

        await Assert.ThrowsAsync<TenantViolationException>(() => db.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task A_business_can_change_its_own_rows()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        await using var db = fixture.For(data.A.Business.Id);

        var shift = await db.Shifts.SingleAsync(s => s.Id == data.A.Shift.Id, Token);
        shift.AssignTo(null);
        db.Add(new RotaWeek(data.A.Business.Id, TwoBusinesses.Monday.AddDays(7)));

        Assert.Equal(2, await db.SaveChangesAsync(Token));
    }

    private async Task AssertUnchangedAsync(TwoBusinesses.Seeded b)
    {
        await using var system = fixture.NewContext(SystemTenant.Instance);
        var shift = await system.Shifts.AsNoTracking().SingleAsync(s => s.Id == b.Shift.Id, Token);
        Assert.Equal(b.Staff.Id, shift.StaffMemberId);
    }
}
