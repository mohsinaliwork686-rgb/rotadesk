using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Rotadesk.Domain;
using Rotadesk.Infrastructure;
using Rotadesk.Infrastructure.Accounts;
using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Api.Tests;

public sealed record TestPerson(Guid Id, string Name, string Email, string Password, StaffRole Role);

/// <summary>A new business with an Owner, a Manager and two staff, each with an account and a known password.</summary>
public sealed class TestBusiness
{
    public const string Password = "Rota-desk-1!";

    private TestBusiness(Business business, IReadOnlyList<TestPerson> people)
    {
        Id = business.Id;
        Name = business.Name;
        Owner = people[0];
        Manager = people[1];
        Staff = people[2];
        Colleague = people[3];
    }

    public Guid Id { get; }

    public string Name { get; }

    public TestPerson Owner { get; }

    public TestPerson Manager { get; }

    public TestPerson Staff { get; }

    public TestPerson Colleague { get; }

    public static async Task<TestBusiness> CreateAsync(ApiFixture fixture, string timeZone)
    {
        var business = new Business($"Test business {Guid.NewGuid():N}", timeZone);
        var members = new[] { ("Olga", StaffRole.Owner), ("Max", StaffRole.Manager), ("Amira", StaffRole.Staff), ("Ben", StaffRole.Staff) }
            .Select(p => new StaffMember(business.Id, p.Item1, $"{p.Item1.ToLowerInvariant()}.{business.Id:N}@example.test", p.Item2))
            .ToList();

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<RotadeskDbContext>>();
        await using (var db = new RotadeskDbContext(options, new FixedTenant(business.Id)))
        {
            db.Businesses.Add(business);
            db.StaffMembers.AddRange(members);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<RotadeskUser>>();
        foreach (var member in members)
        {
            var created = await users.CreateAsync(new RotadeskUser(business.Id, member.Id, member.Email), Password);
            Assert.True(created.Succeeded, string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        return new TestBusiness(business, [.. members.Select(m => new TestPerson(m.Id, m.Name, m.Email, Password, m.Role))]);
    }
}
