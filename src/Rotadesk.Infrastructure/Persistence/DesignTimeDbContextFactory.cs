using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Rotadesk.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the model without starting the API. It never connects.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RotadeskDbContext>
{
    public RotadeskDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RotadeskDbContext>();
        DependencyInjection.Configure(options, "Host=localhost;Database=rotadesk_design");
        return new RotadeskDbContext(options.Options, SystemTenant.Instance);
    }
}
