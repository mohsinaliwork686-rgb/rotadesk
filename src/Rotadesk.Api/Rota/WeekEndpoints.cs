using Microsoft.EntityFrameworkCore;

using Rotadesk.Api.Auth;
using Rotadesk.Api.Errors;
using Rotadesk.Domain;
using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Api.Rota;

internal static class WeekEndpoints
{
    public static void MapWeekEndpoints(this IEndpointRouteBuilder api)
    {
        var weeks = api.MapGroup("/weeks");

        weeks.MapGet("/{weekStart}", GetAsync).RequireAuthorization(Policies.Member);
        weeks.MapPost("/{weekStart}/publish", PublishAsync).RequireAuthorization(Policies.Manager);
        weeks.MapPost("/{weekStart}/copy-previous", CopyPreviousAsync).RequireAuthorization(Policies.Manager);
    }

    private static async Task<IResult> GetAsync(DateOnly weekStart, CurrentMember current, RotaReader rota, CancellationToken cancellationToken)
    {
        var viewer = await current.GetAsync(cancellationToken);
        var zone = (await current.GetBusinessAsync(cancellationToken)).TimeZone;
        return Results.Ok(await rota.ReadAsync(Week.Starting(weekStart, zone), viewer, zone, cancellationToken));
    }

    private static async Task<IResult> PublishAsync(
        DateOnly weekStart,
        CurrentMember current,
        RotadeskDbContext db,
        RotaReader rota,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var manager = await current.GetAsync(cancellationToken);
        var zone = (await current.GetBusinessAsync(cancellationToken)).TimeZone;
        var week = Week.Starting(weekStart, zone);

        var rotaWeek = await rota.FindOrAddWeekAsync(manager.TenantId, week.Start, cancellationToken);
        rotaWeek.Publish(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(await rota.ReadAsync(week, manager, zone, cancellationToken));
    }

    /// <summary>
    /// Copies last week's shifts into this empty week, at the same local times, so 09:00 stays 09:00 when the clocks
    /// change. A shift whose person can't take it any more, say because their weekly limit went down, is copied
    /// without them, and says why.
    /// </summary>
    private static async Task<IResult> CopyPreviousAsync(
        DateOnly weekStart,
        CurrentMember current,
        RotadeskDbContext db,
        RotaReader rota,
        Scheduler scheduler,
        CancellationToken cancellationToken)
    {
        var manager = await current.GetAsync(cancellationToken);
        var zone = (await current.GetBusinessAsync(cancellationToken)).TimeZone;
        var week = Week.Starting(weekStart, zone);
        var previous = Week.Starting(weekStart.AddDays(-7), zone);

        var target = await rota.FindOrAddWeekAsync(manager.TenantId, week.Start, cancellationToken);
        if (target.IsPublished)
        {
            return Problems.Conflict("rota.already-published", "This week is already published, so nothing can be copied into it.");
        }

        if (await db.Shifts.AnyAsync(s => s.RotaWeekId == target.Id, cancellationToken))
        {
            return Problems.Conflict("week.not-empty", "This week already has shifts. Copy into an empty week.");
        }

        var source = await db.RotaWeeks.AsNoTracking().SingleOrDefaultAsync(r => r.WeekStart == previous.Start, cancellationToken);
        var originals = source is null
            ? []
            : await db.Shifts.AsNoTracking().Where(s => s.RotaWeekId == source.Id).OrderBy(s => s.Start).ToListAsync(cancellationToken);
        if (originals.Count == 0)
        {
            return Problems.Conflict("week.nothing-to-copy", $"The week of {previous.Start:d MMM} has no shifts to copy.");
        }

        var staff = await db.StaffMembers.ToDictionaryAsync(s => s.Id, cancellationToken);
        var copies = new List<Shift>();
        var notes = new Dictionary<Guid, string>();
        foreach (var original in originals)
        {
            var copy = new Shift(manager.TenantId, target.Id, AWeekLater(original.Start, zone), AWeekLater(original.End, zone));
            if (original.StaffMemberId is { } staffId && staff.TryGetValue(staffId, out var person))
            {
                var check = await scheduler.CheckAsync(person, copy.Id, copy.Start, copy.End, zone, copies, cancellationToken);
                if (check.IsAllowed)
                {
                    copy.AssignTo(staffId);
                }
                else
                {
                    notes[copy.Id] = $"Copied without {person.Name}: {check.Violations[0].Message}";
                }
            }

            copies.Add(copy);
        }

        db.Shifts.AddRange(copies);
        await db.SaveChangesAsync(cancellationToken);

        var copied = await rota.ReadAsync(week, manager, zone, cancellationToken);
        return Results.Ok(copied with
        {
            Shifts = [.. copied.Shifts.Select(s => notes.TryGetValue(s.Id, out var note) ? s with { Warnings = [note, .. s.Warnings] } : s)],
        });
    }

    /// <summary>The same wall-clock time seven days later. A time the clocks skip moves to the first one that exists.</summary>
    private static DateTimeOffset AWeekLater(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(instant, zone).DateTime.AddDays(7), DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
}
