using Microsoft.EntityFrameworkCore;

using Rotadesk.Api.Auth;
using Rotadesk.Api.Errors;
using Rotadesk.Domain;
using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Api.Rota;

internal static class ShiftEndpoints
{
    public static void MapShiftEndpoints(this IEndpointRouteBuilder api)
    {
        var shifts = api.MapGroup("/shifts").RequireAuthorization(Policies.Manager);

        shifts.MapPost("", CreateAsync);
        shifts.MapPut("/{id:guid}", UpdateAsync);
        shifts.MapDelete("/{id:guid}", DeleteAsync);
    }

    private static async Task<IResult> CreateAsync(
        ShiftRequest request,
        CurrentMember current,
        RotadeskDbContext db,
        RotaReader rota,
        Scheduler scheduler,
        CancellationToken cancellationToken)
    {
        RequireTimes(request);
        var manager = await current.GetAsync(cancellationToken);
        var zone = (await current.GetBusinessAsync(cancellationToken)).TimeZone;

        // A shift belongs to the week it starts in, in the business's time zone.
        var rotaWeek = await rota.FindOrAddWeekAsync(manager.TenantId, Week.Containing(request.Start, zone).Start, cancellationToken);
        var shift = new Shift(manager.TenantId, rotaWeek.Id, request.Start, request.End);

        var (problem, warnings) = await AssignAsync(shift, request.StaffMemberId, db, scheduler, zone, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        db.Shifts.Add(shift);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/shifts/{shift.Id}", await ToResponseAsync(shift, warnings, db, cancellationToken));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        ShiftRequest request,
        CurrentMember current,
        RotadeskDbContext db,
        Scheduler scheduler,
        CancellationToken cancellationToken)
    {
        RequireTimes(request);
        var shift = await db.Shifts.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shift is null)
        {
            return Problems.NotFound("Shift");
        }

        var zone = (await current.GetBusinessAsync(cancellationToken)).TimeZone;
        var rotaWeek = await db.RotaWeeks.SingleAsync(r => r.Id == shift.RotaWeekId, cancellationToken);
        if (Week.Containing(request.Start, zone).Start != rotaWeek.WeekStart)
        {
            return Problems.Conflict("shift.other-week", "A shift stays in its week. Delete it and add it to the other week instead.");
        }

        shift.Reschedule(request.Start, request.End);
        var (problem, warnings) = await AssignAsync(shift, request.StaffMemberId, db, scheduler, zone, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(await ToResponseAsync(shift, warnings, db, cancellationToken));
    }

    private static async Task<IResult> DeleteAsync(Guid id, RotadeskDbContext db, CancellationToken cancellationToken)
    {
        var shift = await db.Shifts.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shift is null)
        {
            return Problems.NotFound("Shift");
        }

        if (await db.SwapRequests.AnyAsync(s => s.OfferedShiftId == id || s.ReturnShiftId == id, cancellationToken))
        {
            return Problems.Conflict("shift.in-swap", "This shift is part of a swap request, so it can't be deleted.");
        }

        db.Shifts.Remove(shift);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    /// <summary>
    /// Gives the shift to someone, if the rules allow it. Another business's staff member isn't found, so it's a 404
    /// rather than a sign that the id exists.
    /// </summary>
    private static async Task<(IResult? Problem, IReadOnlyList<string> Warnings)> AssignAsync(
        Shift shift,
        Guid? staffMemberId,
        RotadeskDbContext db,
        Scheduler scheduler,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        if (staffMemberId is not { } id)
        {
            shift.AssignTo(null);
            return (null, []);
        }

        var person = await db.StaffMembers.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (person is null)
        {
            return (Problems.NotFound("Staff member"), []);
        }

        var check = await scheduler.CheckAsync(person, shift.Id, shift.Start, shift.End, zone, null, cancellationToken);
        if (!check.IsAllowed)
        {
            return (Problems.RuleBroken(check), []);
        }

        shift.AssignTo(id);
        return (null, check.Warnings);
    }

    private static void RequireTimes(ShiftRequest request)
    {
        if (request.Start == default || request.End == default)
        {
            throw new DomainException("shift.invalid-times", "A shift needs a start and an end.");
        }
    }

    private static async Task<ShiftResponse> ToResponseAsync(Shift shift, IReadOnlyList<string> warnings, RotadeskDbContext db, CancellationToken cancellationToken)
    {
        var staff = shift.StaffMemberId is { } id
            ? await db.StaffMembers.AsNoTracking().Where(s => s.Id == id).ToDictionaryAsync(s => s.Id, cancellationToken)
            : new Dictionary<Guid, StaffMember>();
        return RotaReader.ToResponse(shift, staff, warnings);
    }
}
