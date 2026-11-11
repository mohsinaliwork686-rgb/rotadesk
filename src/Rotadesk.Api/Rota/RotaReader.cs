using Microsoft.EntityFrameworkCore;

using Rotadesk.Domain;
using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Api.Rota;

internal sealed class RotaReader(RotadeskDbContext db)
{
    /// <summary>The week's row, added if this is the first time anyone has touched that week.</summary>
    public async Task<RotaWeek> FindOrAddWeekAsync(Guid tenantId, DateOnly weekStart, CancellationToken cancellationToken) =>
        await db.RotaWeeks.SingleOrDefaultAsync(r => r.WeekStart == weekStart, cancellationToken)
        ?? db.RotaWeeks.Add(new RotaWeek(tenantId, weekStart)).Entity;

    /// <summary>
    /// The week as the viewer may see it. Staff see shifts only once the week is published. Managers also get each
    /// shift's warnings, worked out again now, so a warning stays on the rota and a rule broken later shows up.
    /// </summary>
    public async Task<WeekResponse> ReadAsync(Week week, StaffMember viewer, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var rotaWeek = await db.RotaWeeks.AsNoTracking().SingleOrDefaultAsync(r => r.WeekStart == week.Start, cancellationToken);
        if (rotaWeek is null || (!rotaWeek.IsPublished && !viewer.IsManager))
        {
            return new WeekResponse(week.Start, rotaWeek?.IsPublished ?? false, rotaWeek?.PublishedAt, []);
        }

        var shifts = await db.Shifts.AsNoTracking()
            .Where(s => s.RotaWeekId == rotaWeek.Id)
            .OrderBy(s => s.Start)
            .ToListAsync(cancellationToken);
        var staff = await db.StaffMembers.AsNoTracking().ToDictionaryAsync(s => s.Id, cancellationToken);

        Func<Shift, IReadOnlyList<string>> warningsFor = _ => [];
        if (viewer.IsManager)
        {
            // A shift late on Sunday can run into next week, so its hours there count too.
            var nextWeekEnd = week.EndUtc.AddDays(7);
            var nearby = await db.Shifts.AsNoTracking()
                .Where(s => s.StaffMemberId != null && s.Start < nextWeekEnd && s.End > week.StartUtc)
                .ToListAsync(cancellationToken);
            var availability = await db.Availability.AsNoTracking().ToListAsync(cancellationToken);
            warningsFor = shift => shift.StaffMemberId is { } id && staff.TryGetValue(id, out var person)
                ? Warnings(SchedulingRules.Check(person, shift.Id, shift.Start, shift.End, nearby, availability, zone))
                : [];
        }

        return new WeekResponse(
            week.Start,
            rotaWeek.IsPublished,
            rotaWeek.PublishedAt,
            [.. shifts.Select(s => ToResponse(s, staff, warningsFor(s)))]);
    }

    public static ShiftResponse ToResponse(Shift shift, IReadOnlyDictionary<Guid, StaffMember> staff, IReadOnlyList<string> warnings) =>
        new(
            shift.Id,
            shift.Start,
            shift.End,
            shift.StaffMemberId,
            shift.StaffMemberId is { } id && staff.TryGetValue(id, out var person) ? person.Name : null,
            warnings);

    /// <summary>On the rota, a rule that's broken now is shown as a warning: it was allowed when the shift was made.</summary>
    private static IReadOnlyList<string> Warnings(ScheduleCheck check) =>
        [.. check.Violations.Select(v => v.Message), .. check.Warnings];
}
