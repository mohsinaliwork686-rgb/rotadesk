using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Rotadesk.Infrastructure.Accounts;

/// <summary>Lets <c>dotnet ef</c> build the accounts model without starting the API. It never connects.</summary>
internal sealed class AccountsDesignTimeFactory : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>();
        DependencyInjection.ConfigureAccounts(options, "Host=localhost;Database=rotadesk_design");
        return new AccountsDbContext(options.Options);
    }
}
