using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRotadeskDatabase(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<RotadeskDbContext>(options => Configure(options, connectionString));
        return services;
    }

    /// <summary>The database settings, shared with tests and the design-time factory.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
}
