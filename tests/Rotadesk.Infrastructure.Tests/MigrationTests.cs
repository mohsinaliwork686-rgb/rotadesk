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
}
