using Microsoft.EntityFrameworkCore;

using Rotadesk.Api.Auth;
using Rotadesk.Api.Errors;
using Rotadesk.Domain;
using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Api.Rota;

/// <summary>
/// Swaps, moved only through the domain's state machine. Staff see and act on the swaps they're part of; anyone
/// else's is a 404. Managers see every swap in the business and decide the accepted ones.
/// </summary>
internal static class SwapEndpoints
{
    private static readonly SwapStatus[] Open = [SwapStatus.Requested, SwapStatus.Accepted];

    public static void MapSwapEndpoints(this IEndpointRouteBuilder api)
    {
        var swaps = api.MapGroup("/swaps");

        swaps.MapGet("", ListAsync).RequireAuthorization(Policies.Member);
        swaps.MapGet("/{id:guid}", GetAsync).RequireAuthorization(Policies.Member);
        swaps.MapPost("", CreateAsync).RequireAuthorization(Policies.Member);
        swaps.MapPost("/{id:guid}/accept", (Guid id, SwapContext context, CancellationToken ct) =>
            context.ActAsync(id, (swap, me, now) => swap.Accept(me.Id, now), ct)).RequireAuthorization(Policies.Member);
        swaps.MapPost("/{id:guid}/decline", (Guid id, SwapContext context, CancellationToken ct) =>
            context.ActAsync(id, (swap, me, now) => swap.Decline(me.Id, now), ct)).RequireAuthorization(Policies.Member);
        swaps.MapPost("/{id:guid}/cancel", (Guid id, SwapContext context, CancellationToken ct) =>
            context.ActAsync(id, (swap, me, now) => swap.Cancel(me.Id, now), ct)).RequireAuthorization(Policies.Member);
        swaps.MapPost("/{id:guid}/reject", (Guid id, SwapContext context, CancellationToken ct) =>
            context.ActAsync(id, (swap, me, now) => swap.Reject(me, now), ct)).RequireAuthorization(Policies.Manager);
        swaps.MapPost("/{id:guid}/approve", ApproveAsync).RequireAuthorization(Policies.Manager);
    }

    private static async Task<IResult> ListAsync(SwapStatus? status, SwapContext context, CancellationToken cancellationToken)
    {
        var visible = await context.VisibleAsync(cancellationToken);
        if (status is { } wanted)
        {
            visible = visible.Where(s => s.Status == wanted);
        }

        var swaps = await visible.AsNoTracking().OrderByDescending(s => s.CreatedAt).ToListAsync(cancellationToken);
        return Results.Ok(await context.ToResponsesAsync(swaps, cancellationToken));
    }

    private static async Task<IResult> GetAsync(Guid id, SwapContext context, CancellationToken cancellationToken)
    {
        var swap = await (await context.VisibleAsync(cancellationToken)).AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return swap is null ? Problems.NotFound("Swap") : Results.Ok(await context.ToResponseAsync(swap, [], cancellationToken));
    }

    /// <summary>
    /// The signed-in person offers one of their shifts to a colleague. The rules are checked now, so a swap that
    /// can't work is refused straight away, and again at approval, because the rota may change in between.
    /// </summary>
    private static async Task<IResult> CreateAsync(
        CreateSwapRequest request,
        CurrentMember current,
        RotadeskDbContext db,
        SwapChecker checker,
        SwapContext context,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var requester = await current.GetAsync(cancellationToken);
        var zone = (await current.GetBusinessAsync(cancellationToken)).TimeZone;

        var offered = await db.Shifts.SingleOrDefaultAsync(s => s.Id == request.OfferedShiftId, cancellationToken);
        var colleague = await db.StaffMembers.SingleOrDefaultAsync(s => s.Id == request.ColleagueId, cancellationToken);
        var returned = request.ReturnShiftId is { } returnId
            ? await db.Shifts.SingleOrDefaultAsync(s => s.Id == returnId, cancellationToken)
            : null;
        if (offered is null || colleague is null || (request.ReturnShiftId is not null && returned is null))
        {
            return Problems.NotFound(offered is null || (returned is null && request.ReturnShiftId is not null) ? "Shift" : "Colleague");
        }

        // Staff only see shifts on a published rota, so those are the only ones they can offer or ask for.
        Guid[] weekIds = returned is null ? [offered.RotaWeekId] : [offered.RotaWeekId, returned.RotaWeekId];
        if (await db.RotaWeeks.AnyAsync(r => weekIds.Contains(r.Id) && r.PublishedAt == null, cancellationToken))
        {
            return Problems.Conflict("swap.not-published", "Only shifts on a published rota can be swapped.");
        }

        Guid[] shiftIds = returned is null ? [offered.Id] : [offered.Id, returned.Id];
        if (await db.SwapRequests.AnyAsync(
                s => Open.Contains(s.Status)
                    && (shiftIds.Contains(s.OfferedShiftId) || (s.ReturnShiftId != null && shiftIds.Contains(s.ReturnShiftId.Value))),
                cancellationToken))
        {
            return Problems.Conflict("swap.already-open", "One of these shifts is already in a swap that's waiting for an answer.");
        }

        var swap = SwapRequest.Create(requester.Id, colleague.Id, offered, returned, clock.GetUtcNow());
        var check = await checker.CheckAsync(swap, offered, returned, requester, colleague, zone, cancellationToken);
        if (!check.IsAllowed)
        {
            return Problems.RuleBroken(check);
        }

        db.SwapRequests.Add(swap);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/swaps/{swap.Id}", await context.ToResponseAsync(swap, check.Warnings, cancellationToken));
    }

    /// <summary>
    /// Checks the rules again for both people as the rota is now, then moves the shifts. The swap and both shifts
    /// carry xmin, so if another manager decided it, or someone edited a shift, since this request read them, the
    /// save fails and the answer is a 409, not a second approval.
    /// </summary>
    private static async Task<IResult> ApproveAsync(
        Guid id,
        CurrentMember current,
        RotadeskDbContext db,
        SwapChecker checker,
        SwapContext context,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var manager = await current.GetAsync(cancellationToken);
        var zone = (await current.GetBusinessAsync(cancellationToken)).TimeZone;
        var swap = await db.SwapRequests.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (swap is null)
        {
            return Problems.NotFound("Swap");
        }

        swap.EnsureCanApprove(manager);
        var offered = await db.Shifts.SingleAsync(s => s.Id == swap.OfferedShiftId, cancellationToken);
        var returned = swap.ReturnShiftId is { } returnId ? await db.Shifts.SingleAsync(s => s.Id == returnId, cancellationToken) : null;
        var requester = await db.StaffMembers.SingleAsync(s => s.Id == swap.RequesterId, cancellationToken);
        var colleague = await db.StaffMembers.SingleAsync(s => s.Id == swap.ColleagueId, cancellationToken);

        var check = await checker.CheckAsync(swap, offered, returned, requester, colleague, zone, cancellationToken);
        if (!check.IsAllowed)
        {
            return Problems.RuleBroken(check);
        }

        swap.Approve(manager, offered, returned, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(await context.ToResponseAsync(swap, check.Warnings, cancellationToken));
    }
}

/// <summary>What the swap endpoints share: who can see which swaps, acting on one, and the answers.</summary>
internal sealed class SwapContext(CurrentMember current, RotadeskDbContext db, TimeProvider clock)
{
    /// <summary>A manager sees every swap in the business; anyone else, the swaps they're part of.</summary>
    public async Task<IQueryable<SwapRequest>> VisibleAsync(CancellationToken cancellationToken)
    {
        var viewer = await current.GetAsync(cancellationToken);
        return viewer.IsManager
            ? db.SwapRequests
            : db.SwapRequests.Where(s => s.RequesterId == viewer.Id || s.ColleagueId == viewer.Id);
    }

    public async Task<IResult> ActAsync(Guid id, Action<SwapRequest, StaffMember, DateTimeOffset> act, CancellationToken cancellationToken)
    {
        var me = await current.GetAsync(cancellationToken);
        var swap = await (await VisibleAsync(cancellationToken)).SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (swap is null)
        {
            return Problems.NotFound("Swap");
        }

        act(swap, me, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(await ToResponseAsync(swap, [], cancellationToken));
    }

    public async Task<SwapResponse> ToResponseAsync(SwapRequest swap, IReadOnlyList<string> warnings, CancellationToken cancellationToken) =>
        (await ToResponsesAsync([swap], cancellationToken, warnings))[0];

    public async Task<List<SwapResponse>> ToResponsesAsync(
        IReadOnlyList<SwapRequest> swaps,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? warnings = null)
    {
        var shiftIds = swaps.SelectMany(s => s.ReturnShiftId is { } r ? new[] { s.OfferedShiftId, r } : [s.OfferedShiftId]).ToList();
        var shifts = await db.Shifts.AsNoTracking().Where(s => shiftIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        var names = await db.StaffMembers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        SwapShift ShiftOf(Guid id) => new(id, shifts[id].Start, shifts[id].End);

        return [.. swaps.Select(s => new SwapResponse(
            s.Id,
            s.Status,
            new SwapPerson(s.RequesterId, names[s.RequesterId]),
            new SwapPerson(s.ColleagueId, names[s.ColleagueId]),
            ShiftOf(s.OfferedShiftId),
            s.ReturnShiftId is { } r ? ShiftOf(r) : null,
            s.CreatedAt,
            s.RespondedAt,
            s.DecidedAt,
            s.DecidedById is { } d ? names[d] : null,
            warnings ?? []))];
    }
}
