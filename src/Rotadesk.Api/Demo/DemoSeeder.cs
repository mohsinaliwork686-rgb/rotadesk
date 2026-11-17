using System.Globalization;

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
    /// Adds the demo businesses and their people the first time, then makes sure each has a rota around today: this
    /// week published and next week as a draft. The free host sleeps when idle and starts again on the next visit, so
    /// a visitor always finds a current rota. Each business is written through its own tenant, so the write guard
    /// checks the seed like any other change.
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var options = provider.GetRequiredService<DbContextOptions<RotadeskDbContext>>();
        var users = provider.GetRequiredService<UserManager<RotadeskUser>>();
        var existing = provider.GetRequiredService<RotadeskDbContext>();
        var now = provider.GetRequiredService<TimeProvider>().GetUtcNow();

        foreach (var demo in DemoData.Businesses)
        {
            var businessId = await existing.Businesses.Where(b => b.Name == demo.Name).Select(b => (Guid?)b.Id).SingleOrDefaultAsync(cancellationToken)
                ?? await AddBusinessAsync(demo, options, users, cancellationToken);

            await using var db = new RotadeskDbContext(options, new FixedTenant(businessId));
            await FillWeeksAsync(db, demo, businessId, now, cancellationToken);
        }
    }

    private static async Task<Guid> AddBusinessAsync(
        DemoBusiness demo,
        DbContextOptions<RotadeskDbContext> options,
        UserManager<RotadeskUser> users,
        CancellationToken cancellationToken)
    {
        var business = new Business(demo.Name, demo.TimeZoneId);
        var people = demo.People.Select(p => (Person: p, Member: new StaffMember(business.Id, p.Name, p.Email, p.Role, TimeSpan.FromHours(p.WeeklyLimitHours)))).ToList();
        var availability = people.SelectMany(p => (p.Person.Availability ?? []).Select(w =>
            new Domain.Availability(business.Id, p.Member.Id, w.Day, ParseTime(w.From), ParseTime(w.To))));

        await using (var db = new RotadeskDbContext(options, new FixedTenant(business.Id)))
        {
            db.Businesses.Add(business);
            db.StaffMembers.AddRange(people.Select(p => p.Member));
            db.Availability.AddRange(availability);
            await db.SaveChangesAsync(cancellationToken);
        }

        foreach (var (_, member) in people)
        {
            var created = await users.CreateAsync(new RotadeskUser(business.Id, member.Id, member.Email));
            if (!created.Succeeded)
            {
                throw new InvalidOperationException($"Couldn't create the demo account {member.Email}: {string.Join(" ", created.Errors.Select(e => e.Description))}");
            }
        }

        return business.Id;
    }

    /// <summary>
    /// Fills this week, published with its swaps, and next week, as a draft, from the typical week. A week that already
    /// has shifts is left as it is, so what visitors change stays until the week is over.
    /// </summary>
    private static async Task FillWeeksAsync(RotadeskDbContext db, DemoBusiness demo, Guid businessId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var zone = (await db.Businesses.SingleAsync(b => b.Id == businessId, cancellationToken)).TimeZone;
        var staff = await db.StaffMembers.ToDictionaryAsync(s => s.Email, cancellationToken);
        var thisWeek = Week.Containing(now, zone);

        foreach (var (week, current) in new[] { (thisWeek, true), (Week.Starting(thisWeek.Start.AddDays(7), zone), false) })
        {
            var rotaWeek = await db.RotaWeeks.SingleOrDefaultAsync(r => r.WeekStart == week.Start, cancellationToken);
            if (rotaWeek is null)
            {
                rotaWeek = new RotaWeek(businessId, week.Start);
                db.RotaWeeks.Add(rotaWeek);
            }
            else if (await db.Shifts.AnyAsync(s => s.RotaWeekId == rotaWeek.Id, cancellationToken))
            {
                continue;
            }

            var shifts = demo.Week.ToDictionary(
                s => s,
                s => new Shift(businessId, rotaWeek.Id, At(week, s.Day, s.Start, zone), At(week, s.Day, s.End, zone), s.Email is null ? null : staff[s.Email].Id));
            db.Shifts.AddRange(shifts.Values);

            if (!current)
            {
                continue;
            }

            if (!rotaWeek.IsPublished)
            {
                rotaWeek.Publish(now);
            }

            foreach (var swap in demo.Swaps)
            {
                var colleague = staff[swap.To].Id;
                var request = SwapRequest.Create(staff[swap.From].Id, colleague, shifts[swap.Offered], swap.Return is null ? null : shifts[swap.Return], now);
                if (swap.Accepted)
                {
                    request.Accept(colleague, now);
                }

                db.SwapRequests.Add(request);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A day of the week at a wall-clock time, in UTC. A time the clocks skip moves to the first one that exists.</summary>
    private static DateTimeOffset At(Week week, int day, string time, TimeZoneInfo zone)
    {
        var local = week.Start.AddDays(day).ToDateTime(ParseTime(time), DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    private static TimeOnly ParseTime(string text) => TimeOnly.ParseExact(text, "HH:mm", CultureInfo.InvariantCulture);
}
