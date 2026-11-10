using Microsoft.EntityFrameworkCore;

namespace Rotadesk.Infrastructure.Tests;

public class TenantFilterTests(PostgresFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_business_sees_only_its_own_rows_in_every_table()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        await using var db = fixture.For(data.A.Business.Id);

        Assert.All(await db.StaffMembers.ToListAsync(Token), s => Assert.Equal(data.A.Business.Id, s.TenantId));
        Assert.All(await db.RotaWeeks.ToListAsync(Token), w => Assert.Equal(data.A.Business.Id, w.TenantId));
        Assert.All(await db.Shifts.ToListAsync(Token), s => Assert.Equal(data.A.Business.Id, s.TenantId));
        Assert.All(await db.Availability.ToListAsync(Token), a => Assert.Equal(data.A.Business.Id, a.TenantId));
        Assert.All(await db.SwapRequests.ToListAsync(Token), s => Assert.Equal(data.A.Business.Id, s.TenantId));
        Assert.Equal(2, await db.StaffMembers.CountAsync(Token));
    }

    [Fact]
    public async Task Another_business_row_is_not_found_by_its_id()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        await using var db = fixture.For(data.A.Business.Id);

        Assert.Null(await db.Shifts.FindAsync([data.B.Shift.Id], Token));
        Assert.Null(await db.StaffMembers.FirstOrDefaultAsync(s => s.Id == data.B.Staff.Id, Token));
        Assert.Null(await db.SwapRequests.FindAsync([data.B.Swap.Id], Token));
    }

    [Fact]
    public async Task A_context_without_a_business_sees_nothing()
    {
        await TwoBusinesses.CreateAsync(fixture);
        await using var db = fixture.NewContext(new NoTenant());

        Assert.Equal(0, await db.Shifts.CountAsync(Token));
        Assert.Equal(0, await db.StaffMembers.CountAsync(Token));
    }

    [Fact]
    public async Task The_system_context_sees_every_business()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        await using var db = fixture.NewContext(SystemTenant.Instance);

        var ids = new[] { data.A.Shift.Id, data.B.Shift.Id };
        Assert.Equal(2, await db.Shifts.CountAsync(s => ids.Contains(s.Id), Token));
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid? TenantId => null;

        public bool IsSystem => false;
    }
}
