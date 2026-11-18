using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Rotadesk.Infrastructure.Accounts;
using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers both contexts. The caller registers the <see cref="ITenantContext"/> for its requests.</summary>
    public static IServiceCollection AddRotadeskDatabase(this IServiceCollection services, string connectionString)
    {
        connectionString = WithoutGssEncryption(connectionString);
        services.AddSingleton<TenantGuardInterceptor>();
        services.AddDbContext<RotadeskDbContext>((provider, options) =>
            Configure(options, connectionString).AddInterceptors(provider.GetRequiredService<TenantGuardInterceptor>()));
        services.AddDbContext<AccountsDbContext>(options => ConfigureAccounts(options, connectionString));
        return services;
    }

    /// <summary>
    /// Nothing here uses Kerberos. Left on, Npgsql tries GSS encryption first, and in the slim runtime image, which has no
    /// Kerberos library, it logs an error on every start.
    /// </summary>
    private static string WithoutGssEncryption(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { GssEncryptionMode = GssEncryptionMode.Disable }.ConnectionString;

    /// <summary>The database settings, shared with tests and the design-time factory.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

    /// <summary>The accounts schema keeps its own migration history, so the two contexts never see each other's.</summary>
    public static DbContextOptionsBuilder ConfigureAccounts(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, AccountsDbContext.Schema))
            .UseSnakeCaseNamingConvention();

    /// <summary>
    /// Applies both sets of migrations, business data first, because the accounts point at staff members.
    /// </summary>
    public static async Task MigrateRotadeskDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        await using (var data = new RotadeskDbContext(provider.GetRequiredService<DbContextOptions<RotadeskDbContext>>(), SystemTenant.Instance))
        {
            await data.Database.MigrateAsync(cancellationToken);
        }

        await provider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
