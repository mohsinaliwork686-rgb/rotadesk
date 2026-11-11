using Microsoft.EntityFrameworkCore;

using Rotadesk.Infrastructure;
using Rotadesk.Infrastructure.Accounts;
using Rotadesk.Infrastructure.Persistence;

using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Rotadesk.Infrastructure.Tests.PostgresFixture))]

namespace Rotadesk.Infrastructure.Tests;

/// <summary>One real PostgreSQL for the whole test run, migrated from empty. Tests keep apart by using new businesses.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using (var db = NewContext(SystemTenant.Instance))
        {
            await db.Database.MigrateAsync();
        }

        // After the business data, because the accounts point at staff members.
        await using var accounts = NewAccounts();
        await accounts.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public RotadeskDbContext NewContext(ITenantContext tenant)
    {
        var options = new DbContextOptionsBuilder<RotadeskDbContext>();
        DependencyInjection.Configure(options, ConnectionString).AddInterceptors(new TenantGuardInterceptor());
        return new RotadeskDbContext(options.Options, tenant);
    }

    public RotadeskDbContext For(Guid tenantId) => NewContext(new FixedTenant(tenantId));

    public AccountsDbContext NewAccounts()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>();
        DependencyInjection.ConfigureAccounts(options, ConnectionString);
        return new AccountsDbContext(options.Options);
    }
}
