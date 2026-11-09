namespace Rotadesk.Domain;

/// <summary>
/// One person asks a colleague to take a shift, optionally taking one of the colleague's in return.
/// The colleague accepts or declines, then a manager approves or rejects. Every other move throws.
/// </summary>
public sealed class SwapRequest : ITenantOwned
{
    private SwapRequest()
    {
    }

    private SwapRequest(Guid tenantId, Guid requesterId, Guid colleagueId, Guid offeredShiftId, Guid? returnShiftId, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        RequesterId = requesterId;
        ColleagueId = colleagueId;
        OfferedShiftId = offeredShiftId;
        ReturnShiftId = returnShiftId;
        Status = SwapStatus.Requested;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid RequesterId { get; private set; }

    public Guid ColleagueId { get; private set; }

    public Guid OfferedShiftId { get; private set; }

    public Guid? ReturnShiftId { get; private set; }

    public SwapStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RespondedAt { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public Guid? DecidedById { get; private set; }

    public bool IsFinal => Status is SwapStatus.Declined or SwapStatus.Approved or SwapStatus.Rejected or SwapStatus.Cancelled;

    public static SwapRequest Create(Guid requesterId, Guid colleagueId, Shift offered, Shift? returned, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(offered);

        if (colleagueId == requesterId)
        {
            throw new DomainException("swap.same-person", "You can't swap a shift with yourself.");
        }

        if (offered.StaffMemberId != requesterId)
        {
            throw new DomainException("swap.not-your-shift", "You can only offer one of your own shifts.");
        }

        if (returned is not null && returned.StaffMemberId != colleagueId)
        {
            throw new DomainException("swap.not-colleagues-shift", "The shift in return has to be one of your colleague's.");
        }

        if (returned is not null && returned.TenantId != offered.TenantId)
        {
            throw new DomainException("swap.not-colleagues-shift", "The shift in return has to be one of your colleague's.");
        }

        if (offered.Start <= now || (returned is not null && returned.Start <= now))
        {
            throw new DomainException("swap.shift-started", "A shift that has already started can't be swapped.");
        }

        return new SwapRequest(offered.TenantId, requesterId, colleagueId, offered.Id, returned?.Id, now);
    }

    public void Accept(Guid actorId, DateTimeOffset now)
    {
        RequireStatus(SwapStatus.Requested, "accept");
        RequireActor(actorId, ColleagueId, "Only the colleague asked can accept this swap.");
        Status = SwapStatus.Accepted;
        RespondedAt = now;
    }

    public void Decline(Guid actorId, DateTimeOffset now)
    {
        RequireStatus(SwapStatus.Requested, "decline");
        RequireActor(actorId, ColleagueId, "Only the colleague asked can decline this swap.");
        Status = SwapStatus.Declined;
        RespondedAt = now;
    }

    public void Cancel(Guid actorId, DateTimeOffset now)
    {
        if (Status is not (SwapStatus.Requested or SwapStatus.Accepted))
        {
            throw Illegal("cancel");
        }

        RequireActor(actorId, RequesterId, "Only the person who asked can cancel this swap.");
        Status = SwapStatus.Cancelled;
        DecidedAt = now;
        DecidedById = actorId;
    }

    public void Reject(StaffMember manager, DateTimeOffset now)
    {
        RequireStatus(SwapStatus.Accepted, "reject");
        RequireDecider(manager);
        Status = SwapStatus.Rejected;
        DecidedAt = now;
        DecidedById = manager.Id;
    }

    /// <summary>
    /// Approves the swap and moves the shifts. Check the rota rules first with <see cref="SwapApproval.Check"/>:
    /// this only checks that the shifts still belong to the people they belonged to when the swap was asked for.
    /// </summary>
    public void Approve(StaffMember manager, Shift offered, Shift? returned, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(offered);
        RequireStatus(SwapStatus.Accepted, "approve");
        RequireDecider(manager);
        RequireSameShifts(offered, returned);

        offered.AssignTo(ColleagueId);
        returned?.AssignTo(RequesterId);
        Status = SwapStatus.Approved;
        DecidedAt = now;
        DecidedById = manager.Id;
    }

    internal void RequireSameShifts(Shift offered, Shift? returned)
    {
        var sameOffered = offered.Id == OfferedShiftId && offered.StaffMemberId == RequesterId;
        var sameReturn = ReturnShiftId is null
            ? returned is null
            : returned is not null && returned.Id == ReturnShiftId && returned.StaffMemberId == ColleagueId;

        if (!sameOffered || !sameReturn)
        {
            throw new DomainException("swap.shifts-changed", "The shifts in this swap have changed since it was asked for.");
        }
    }

    private void RequireStatus(SwapStatus expected, string action)
    {
        if (Status != expected)
        {
            throw Illegal(action);
        }
    }

    private void RequireDecider(StaffMember manager)
    {
        ArgumentNullException.ThrowIfNull(manager);

        if (!manager.IsManager || manager.TenantId != TenantId)
        {
            throw new DomainException("swap.not-a-manager", "Only a manager can approve or reject a swap.");
        }

        if (manager.Id == RequesterId || manager.Id == ColleagueId)
        {
            throw new DomainException("swap.own-swap", "A manager can't approve or reject a swap they're part of.");
        }
    }

    private static void RequireActor(Guid actorId, Guid expected, string message)
    {
        if (actorId != expected)
        {
            throw new DomainException("swap.wrong-person", message);
        }
    }

    private DomainException Illegal(string action) =>
        new("swap.illegal-transition", $"A swap that is {Status.ToString().ToLowerInvariant()} can't be {Past(action)}.");

    private static string Past(string action) => action switch
    {
        "accept" => "accepted",
        "decline" => "declined",
        "cancel" => "cancelled",
        "approve" => "approved",
        "reject" => "rejected",
        _ => action,
    };
}
