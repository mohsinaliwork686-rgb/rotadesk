using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

using Rotadesk.Infrastructure.Accounts;
using Rotadesk.Infrastructure.Persistence;

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

    /// <summary>
    /// A database made before the tenant keys already has accounts pointing at (business, staff member). Upgrading it
    /// has to keep that link, which is why the migration converts the index in place rather than dropping it.
    /// </summary>
    [Fact]
    public async Task A_database_with_accounts_upgrades_and_keeps_the_accounts_link()
    {
        var token = TestContext.Current.CancellationToken;
        var name = $"upgrade_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(fixture.ConnectionString))
        {
            await admin.OpenAsync(token);
            await new NpgsqlCommand($"CREATE DATABASE {name}", admin).ExecuteNonQueryAsync(token);
        }

        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = name }.ConnectionString;
        var dataOptions = DependencyInjection.Configure(new DbContextOptionsBuilder<RotadeskDbContext>(), connectionString).Options;
        var accountOptions = DependencyInjection.ConfigureAccounts(new DbContextOptionsBuilder<AccountsDbContext>(), connectionString).Options;

        await using (var data = new RotadeskDbContext((DbContextOptions<RotadeskDbContext>)dataOptions, SystemTenant.Instance))
        {
            await data.GetService<IMigrator>().MigrateAsync("20261111105337_StaffMemberTenantKey", token);
            await using (var accounts = new AccountsDbContext((DbContextOptions<AccountsDbContext>)accountOptions))
            {
                await accounts.Database.MigrateAsync(token);
            }

            await data.Database.MigrateAsync(token);
            Assert.Empty(await data.Database.GetPendingMigrationsAsync(token));

            var constraints = await data.Database
                .SqlQueryRaw<string>("SELECT conname AS \"Value\" FROM pg_constraint WHERE conname IN ('ak_staff_members_tenant_id_id', 'fk_users_staff_members_tenant_id_staff_member_id')")
                .ToListAsync(token);
            Assert.Equal(["ak_staff_members_tenant_id_id", "fk_users_staff_members_tenant_id_staff_member_id"], constraints.Order());
        }

        NpgsqlConnection.ClearAllPools();
        await using var cleanup = new NpgsqlConnection(fixture.ConnectionString);
        await cleanup.OpenAsync(token);
        await new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", cleanup).ExecuteNonQueryAsync(token);
    }
}
