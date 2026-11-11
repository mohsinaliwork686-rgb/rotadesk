using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Rotadesk.Domain;
using Rotadesk.Infrastructure;
using Rotadesk.Infrastructure.Accounts;
using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Api.Demo;

internal static class DemoSeeder
{
    /// <summary>
    /// Adds the demo businesses and their people the first time. Each business is written through its own tenant, so
    /// the write guard checks the seed like any other change.
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var options = provider.GetRequiredService<DbContextOptions<RotadeskDbContext>>();
        var users = provider.GetRequiredService<UserManager<RotadeskUser>>();
        var existing = provider.GetRequiredService<RotadeskDbContext>();

        foreach (var demo in DemoData.Businesses)
        {
            if (await existing.Businesses.AnyAsync(b => b.Name == demo.Name, cancellationToken))
            {
                continue;
            }

            var business = new Business(demo.Name, demo.TimeZoneId);
            var staff = demo.People.Select(p => new StaffMember(business.Id, p.Name, p.Email, p.Role)).ToList();
            await using (var db = new RotadeskDbContext(options, new FixedTenant(business.Id)))
            {
                db.Businesses.Add(business);
                db.StaffMembers.AddRange(staff);
                await db.SaveChangesAsync(cancellationToken);
            }

            foreach (var member in staff)
            {
                var created = await users.CreateAsync(new RotadeskUser(business.Id, member.Id, member.Email));
                if (!created.Succeeded)
                {
                    throw new InvalidOperationException($"Couldn't create the demo account {member.Email}: {string.Join(" ", created.Errors.Select(e => e.Description))}");
                }
            }
        }
    }
}
