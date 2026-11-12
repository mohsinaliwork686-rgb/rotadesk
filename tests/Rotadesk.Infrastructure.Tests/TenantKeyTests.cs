using Microsoft.EntityFrameworkCore;

using Npgsql;

using Rotadesk.Domain;

namespace Rotadesk.Infrastructure.Tests;

/// <summary>
/// The database's own guard. These save through the system context, which skips the tenant filters and the write
/// guard, to show that even code that gets those wrong can't link one business's rows to another's.
/// </summary>
public class TenantKeyTests(PostgresFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_shift_cant_be_given_to_another_businesss_staff_member()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        var shift = new Shift(data.A.Business.Id, data.A.Week.Id, At(18, 9), At(18, 17), data.B.Staff.Id);

        await AssertRefusedAsync(shift);
    }

    [Fact]
    public async Task A_shift_cant_sit_in_another_businesss_week()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        var shift = new Shift(data.A.Business.Id, data.B.Week.Id, At(18, 9), At(18, 17));

        await AssertRefusedAsync(shift);
    }

    [Fact]
    public async Task Availability_cant_belong_to_another_businesss_staff_member()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        var window = new Availability(data.A.Business.Id, data.B.Staff.Id, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0));

        await AssertRefusedAsync(window);
    }

    [Fact]
    public async Task A_swap_cant_ask_another_businesss_colleague()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        var swap = SwapRequest.Create(data.A.Staff.Id, data.B.Colleague.Id, data.A.Shift, null, At(10, 9));

        await AssertRefusedAsync(swap);
    }

    [Fact]
    public async Task An_unassigned_shift_is_still_fine()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        await using var db = fixture.NewContext(SystemTenant.Instance);
        db.Add(new Shift(data.A.Business.Id, data.A.Week.Id, At(18, 9), At(18, 17)));

        Assert.Equal(1, await db.SaveChangesAsync(Token));
    }

    private static DateTimeOffset At(int day, int hour) => new(2026, 11, day, hour, 0, 0, TimeSpan.Zero);

    private async Task AssertRefusedAsync(object row)
    {
        await using var db = fixture.NewContext(SystemTenant.Instance);
        db.Add(row);

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Token));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }
}
