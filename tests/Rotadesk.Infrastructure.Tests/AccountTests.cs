using Microsoft.EntityFrameworkCore;

using Npgsql;

using Rotadesk.Domain;
using Rotadesk.Infrastructure.Accounts;

namespace Rotadesk.Infrastructure.Tests;

/// <summary>The link from a sign-in account to its staff member, which only the database can enforce across the two contexts.</summary>
public class AccountTests(PostgresFixture fixture)
{
    [Fact]
    public async Task An_account_belongs_to_a_staff_member_of_its_own_business()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        var account = new RotadeskUser(data.A.Business.Id, data.A.Staff.Id, data.A.Staff.Email);

        await using (var accounts = fixture.NewAccounts())
        {
            accounts.Users.Add(account);
            await accounts.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reread = fixture.NewAccounts();
        var saved = await reread.Users.SingleAsync(u => u.Id == account.Id, TestContext.Current.CancellationToken);
        Assert.Equal(data.A.Staff.Id, saved.StaffMemberId);
        Assert.Equal(data.A.Business.Id, saved.TenantId);
    }

    [Fact]
    public async Task An_account_cant_claim_one_business_and_point_at_another_businesss_staff_member()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        await using var accounts = fixture.NewAccounts();
        accounts.Users.Add(new RotadeskUser(data.A.Business.Id, data.B.Staff.Id, $"mixed.{Guid.NewGuid():N}@example.test"));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => accounts.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task Deleting_a_staff_member_deletes_their_account()
    {
        var data = await TwoBusinesses.CreateAsync(fixture);
        var leaver = new StaffMember(data.A.Business.Id, "Cara", $"cara.{Guid.NewGuid():N}@example.test", StaffRole.Staff);
        await using var db = fixture.For(data.A.Business.Id);
        db.Add(leaver);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var account = new RotadeskUser(data.A.Business.Id, leaver.Id, leaver.Email);
        await using (var accounts = fixture.NewAccounts())
        {
            accounts.Users.Add(account);
            await accounts.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        db.Remove(leaver);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var reread = fixture.NewAccounts();
        Assert.False(await reread.Users.AnyAsync(u => u.Id == account.Id, TestContext.Current.CancellationToken));
    }
}
