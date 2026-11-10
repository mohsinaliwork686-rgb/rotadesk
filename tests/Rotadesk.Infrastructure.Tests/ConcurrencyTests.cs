using Microsoft.EntityFrameworkCore;

using Rotadesk.Domain;

namespace Rotadesk.Infrastructure.Tests;

public class ConcurrencyTests(PostgresFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_second_of_two_saves_from_the_same_read_fails()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        await using var first = fixture.For(data.A.Business.Id);
        await using var second = fixture.For(data.A.Business.Id);
        var mine = await first.Shifts.SingleAsync(s => s.Id == data.A.Shift.Id, Token);
        var theirs = await second.Shifts.SingleAsync(s => s.Id == data.A.Shift.Id, Token);

        mine.AssignTo(data.A.Colleague.Id);
        await first.SaveChangesAsync(Token);
        theirs.AssignTo(null);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task Two_managers_approving_the_same_swap_cannot_both_succeed()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        var manager1 = new StaffMember(data.A.Business.Id, "Maya", $"maya.{Guid.NewGuid():N}@example.test", StaffRole.Manager);
        var manager2 = new StaffMember(data.A.Business.Id, "Noor", $"noor.{Guid.NewGuid():N}@example.test", StaffRole.Manager);
        await using (var setup = fixture.For(data.A.Business.Id))
        {
            setup.AddRange(manager1, manager2);
            var swap = await setup.SwapRequests.SingleAsync(s => s.Id == data.A.Swap.Id, Token);
            swap.Accept(data.A.Colleague.Id, DateTimeOffset.UtcNow);
            await setup.SaveChangesAsync(Token);
        }

        await using var first = fixture.For(data.A.Business.Id);
        await using var second = fixture.For(data.A.Business.Id);
        var swap1 = await first.SwapRequests.SingleAsync(s => s.Id == data.A.Swap.Id, Token);
        var swap2 = await second.SwapRequests.SingleAsync(s => s.Id == data.A.Swap.Id, Token);

        swap1.Reject(manager1, DateTimeOffset.UtcNow);
        swap2.Reject(manager2, DateTimeOffset.UtcNow);
        await first.SaveChangesAsync(Token);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));
    }
}
