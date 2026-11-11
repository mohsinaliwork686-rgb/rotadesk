using Microsoft.EntityFrameworkCore;

namespace Rotadesk.Infrastructure.Tests;

public class MigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Migrations_apply_to_an_empty_database_and_match_the_model()
    {
        await using var db = fixture.NewContext(SystemTenant.Instance);

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Account_migrations_apply_and_match_the_model()
    {
        await using var accounts = fixture.NewAccounts();

        Assert.Empty(await accounts.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.False(accounts.Database.HasPendingModelChanges());
    }
}
