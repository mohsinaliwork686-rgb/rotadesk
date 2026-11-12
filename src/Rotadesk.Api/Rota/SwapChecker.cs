using Microsoft.EntityFrameworkCore;

using Rotadesk.Domain;
using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Api.Rota;

/// <summary>Loads both people's shifts and availability around the swap, and asks <see cref="SwapApproval"/>.</summary>
internal sealed class SwapChecker(RotadeskDbContext db)
{
    public async Task<ScheduleCheck> CheckAsync(
        SwapRequest swap,
        Shift offered,
        Shift? returned,
        StaffMember requester,
        StaffMember colleague,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        // Every week either shift touches, so both weekly limits see all the hours that count.
        DateTimeOffset[] moments = returned is null
            ? [offered.Start, offered.End]
            : [offered.Start, offered.End, returned.Start, returned.End];
        var from = moments.Min(m => Week.Containing(m, zone).StartUtc);
        var to = moments.Max(m => Week.Containing(m, zone).EndUtc);
        Guid[] people = [requester.Id, colleague.Id];

        var shifts = await db.Shifts
            .Where(s => s.StaffMemberId != null && people.Contains(s.StaffMemberId.Value) && s.Start < to && s.End > from)
            .ToListAsync(cancellationToken);
        var availability = await db.Availability.Where(a => people.Contains(a.StaffMemberId)).ToListAsync(cancellationToken);

        return SwapApproval.Check(swap, offered, returned, requester, colleague, shifts, availability, zone);
    }
}
