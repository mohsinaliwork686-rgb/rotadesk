using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Rotadesk.Infrastructure.Accounts;

/// <summary>
/// Sign-in accounts, in their own schema. They are looked up before anyone's business is known, so they can't sit
/// behind the tenant filter. Keeping them out of <see cref="Persistence.RotadeskDbContext"/> keeps its rule true:
/// every table in it belongs to a business.
/// </summary>
public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : IdentityUserContext<RotadeskUser, Guid>(options)
{
    public const string Schema = "accounts";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(Schema);
        builder.Entity<RotadeskUser>(user =>
        {
            user.ToTable("users");
            user.HasIndex(u => u.StaffMemberId).IsUnique();
        });
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
    }
}
