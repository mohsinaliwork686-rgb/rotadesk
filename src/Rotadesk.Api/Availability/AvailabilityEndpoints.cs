using Microsoft.EntityFrameworkCore;

using Rotadesk.Api.Auth;
using Rotadesk.Api.Errors;
using Rotadesk.Domain;
using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Api.Availability;

internal static class AvailabilityEndpoints
{
    public static void MapAvailabilityEndpoints(this IEndpointRouteBuilder api)
    {
        var mine = api.MapGroup("/availability/mine").RequireAuthorization(Policies.Member);
        mine.MapGet("", MineAsync);
        mine.MapPut("", ReplaceMineAsync);

        api.MapGet("/staff/{id:guid}/availability", ForMemberAsync).RequireAuthorization(Policies.Member);
    }

    private static async Task<IResult> MineAsync(CurrentMember current, RotadeskDbContext db, CancellationToken cancellationToken)
    {
        var member = await current.GetAsync(cancellationToken);
        return Results.Ok(await ReadAsync(db, member.Id, cancellationToken));
    }

    /// <summary>Replaces all of the signed-in person's windows with these, merged where they overlap or touch.</summary>
    private static async Task<IResult> ReplaceMineAsync(
        IReadOnlyList<AvailabilityWindow> windows,
        CurrentMember current,
        RotadeskDbContext db,
        CancellationToken cancellationToken)
    {
        var member = await current.GetAsync(cancellationToken);
        var merged = AvailabilityWindows.Merge(windows.Select(w => new DayWindow(w.Day, TimeText.ParseFrom(w.From), TimeText.ParseTo(w.To))));

        db.Availability.RemoveRange(await db.Availability.Where(a => a.StaffMemberId == member.Id).ToListAsync(cancellationToken));
        db.Availability.AddRange(merged.Select(w => new Domain.Availability(member.TenantId, member.Id, w.Day, w.From, w.To)));
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(await ReadAsync(db, member.Id, cancellationToken));
    }

    /// <summary>Managers see anyone's windows when they build the rota. Staff see only their own.</summary>
    private static async Task<IResult> ForMemberAsync(Guid id, CurrentMember current, RotadeskDbContext db, CancellationToken cancellationToken)
    {
        var viewer = await current.GetAsync(cancellationToken);
        if (!await db.StaffMembers.AnyAsync(s => s.Id == id, cancellationToken))
        {
            return Problems.NotFound("Staff member");
        }

        if (!viewer.IsManager && viewer.Id != id)
        {
            return Problems.Forbidden("Only managers can see other people's availability.");
        }

        return Results.Ok(await ReadAsync(db, id, cancellationToken));
    }

    private static async Task<List<AvailabilityWindow>> ReadAsync(RotadeskDbContext db, Guid staffMemberId, CancellationToken cancellationToken)
    {
        var rows = await db.Availability.AsNoTracking().Where(a => a.StaffMemberId == staffMemberId).ToListAsync(cancellationToken);
        return [.. rows
            .OrderBy(a => ((int)a.Day + 6) % 7)
            .ThenBy(a => a.From)
            .Select(a => new AvailabilityWindow(a.Day, TimeText.Format(a.From), TimeText.Format(a.To)))];
    }
}
