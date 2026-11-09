namespace Rotadesk.Domain;

/// <summary>
/// Checks the rota rules for both people as the shifts will be after a swap. The rota may have changed since
/// the swap was asked for, so this runs again at approval.
/// </summary>
public static class SwapApproval
{
    public static ScheduleCheck Check(
        SwapRequest request,
        Shift offered,
        Shift? returned,
        StaffMember requester,
        StaffMember colleague,
        IReadOnlyCollection<Shift> shifts,
        IReadOnlyCollection<Availability> availability,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(offered);
        request.RequireSameShifts(offered, returned);

        // Each person gives one shift away, so it no longer counts against them.
        var colleagueShifts = shifts.Where(s => s.Id != returned?.Id).ToList();
        var requesterShifts = shifts.Where(s => s.Id != offered.Id).ToList();

        var forColleague = SchedulingRules.Check(colleague, offered.Id, offered.Start, offered.End, colleagueShifts, availability, zone);
        var forRequester = returned is null
            ? new ScheduleCheck([], [])
            : SchedulingRules.Check(requester, returned.Id, returned.Start, returned.End, requesterShifts, availability, zone);

        return ScheduleCheck.Combine(forColleague, forRequester);
    }
}
