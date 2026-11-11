using Microsoft.EntityFrameworkCore;

using Rotadesk.Domain;
using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Api.Rota;

/// <summary>Loads what <see cref="SchedulingRules"/> needs for one person, and asks it.</summary>
internal sealed class Scheduler(RotadeskDbContext db)
{
    /// <param name="pending">Shifts added in this request but not saved yet, such as the rest of a copied week.</param>
    public async Task<ScheduleCheck> CheckAsync(
        StaffMember member,
        Guid shiftId,
        DateTimeOffset start,
        DateTimeOffset end,
        TimeZoneInfo zone,
        IReadOnlyCollection<Shift>? pending,
        CancellationToken cancellationToken)
    {
        // Every week the shift touches, so the weekly limit sees all the hours that count against it.
        var from = Week.Containing(start, zone).StartUtc;
        var to = Week.Containing(end, zone).EndUtc;
        var shifts = await db.Shifts
            .Where(s => s.StaffMemberId == member.Id && s.Start < to && s.End > from)
            .ToListAsync(cancellationToken);
        if (pending is not null)
        {
            shifts.AddRange(pending.Where(p => p.StaffMemberId == member.Id && shifts.All(s => s.Id != p.Id)));
        }

        var availability = await db.Availability.Where(a => a.StaffMemberId == member.Id).ToListAsync(cancellationToken);
        return SchedulingRules.Check(member, shiftId, start, end, shifts, availability, zone);
    }
}
