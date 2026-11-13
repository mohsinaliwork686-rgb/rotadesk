using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Rotadesk.Domain;
using Rotadesk.Infrastructure.Outbox;
using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Api.Rota;

/// <summary>
/// Queues the emails a change should send. They're added to the request's context, so they're saved by the same
/// SaveChanges as the change, in one transaction: if the change fails, no email is queued.
/// </summary>
internal sealed class Notifications(RotadeskDbContext db, TimeProvider clock)
{
    /// <summary>"Mon 16 Nov, 09:00 to 17:00", in the business's time zone.</summary>
    public static string When(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        var from = TimeZoneInfo.ConvertTime(start, zone);
        var to = TimeZoneInfo.ConvertTime(end, zone);
        return string.Create(CultureInfo.InvariantCulture, $"{from:ddd d MMM, HH:mm} to {to:HH:mm}");
    }

    /// <summary>Each person with a shift in the week gets a list of their own shifts.</summary>
    public async Task RotaPublishedAsync(RotaWeek week, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var shifts = await db.Shifts
            .Where(s => s.RotaWeekId == week.Id && s.StaffMemberId != null)
            .OrderBy(s => s.Start)
            .ToListAsync(cancellationToken);
        var ids = shifts.Select(s => s.StaffMemberId!.Value).Distinct().ToList();
        var people = await db.StaffMembers.Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);

        foreach (var theirs in shifts.GroupBy(s => s.StaffMemberId!.Value))
        {
            var person = people[theirs.Key];
            var list = string.Join("\n", theirs.Select(s => $"- {When(s.Start, s.End, zone)}"));
            Queue(person, Format($"Your shifts for the week of {week.WeekStart:d MMM}"), Format($"Hi {person.Name},\n\nThe rota for the week of {week.WeekStart:d MMMM} is published. Your shifts:\n\n{list}"));
        }
    }

    public void SwapRequested(StaffMember requester, StaffMember colleague, Shift offered, Shift? returned, TimeZoneInfo zone) =>
        Queue(
            colleague,
            $"{requester.Name} asked you to swap a shift",
            $"Hi {colleague.Name},\n\n{requester.Name} asked you to take their shift on {When(offered.Start, offered.End, zone)}{InReturn(returned, "give them yours", zone)}.\n\nAccept or decline it in rotadesk.");

    /// <summary>The managers who can decide it: everyone at Manager or above who isn't part of the swap.</summary>
    public async Task SwapAcceptedAsync(StaffMember requester, StaffMember colleague, Shift offered, Shift? returned, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var managers = await db.StaffMembers
            .Where(s => (s.Role == StaffRole.Manager || s.Role == StaffRole.Owner) && s.Id != requester.Id && s.Id != colleague.Id)
            .ToListAsync(cancellationToken);
        foreach (var manager in managers)
        {
            Queue(
                manager,
                "A swap is waiting for your approval",
                $"Hi {manager.Name},\n\n{colleague.Name} agreed to take {requester.Name}'s shift on {When(offered.Start, offered.End, zone)}{InReturn(returned, $"give {requester.Name} theirs", zone)}. It needs a manager's approval.");
        }
    }

    public void SwapDecided(bool approved, StaffMember manager, StaffMember requester, StaffMember colleague, Shift offered, Shift? returned, TimeZoneInfo zone)
    {
        var verdict = approved ? "approved" : "rejected";
        var outcome = approved ? "The rota now shows the new shifts." : "Nothing on the rota has changed.";
        foreach (var person in new[] { requester, colleague })
        {
            Queue(
                person,
                $"Your swap was {verdict}",
                $"Hi {person.Name},\n\n{manager.Name} {verdict} the swap of {requester.Name}'s shift on {When(offered.Start, offered.End, zone)}{InReturn(returned, $"{colleague.Name}'s", zone, plain: true)}. {outcome}");
        }
    }

    /// <summary>
    /// A shift on a published rota was added, changed or removed. Whoever had it and whoever has it now hear about
    /// it; nobody does if it was and stays unassigned.
    /// </summary>
    public void PublishedShiftChanged(
        StaffMember? before,
        (DateTimeOffset Start, DateTimeOffset End)? was,
        StaffMember? after,
        (DateTimeOffset Start, DateTimeOffset End)? now,
        TimeZoneInfo zone)
    {
        var wasText = was is { } w ? When(w.Start, w.End, zone) : null;
        var nowText = now is { } n ? When(n.Start, n.End, zone) : null;

        if (before is not null && after is not null && before.Id == after.Id)
        {
            if (wasText != nowText)
            {
                Queue(after, "One of your shifts changed", $"Hi {after.Name},\n\nYour shift on {wasText} is now {nowText}.");
            }

            return;
        }

        if (before is not null)
        {
            Queue(before, "A shift was taken off your rota", $"Hi {before.Name},\n\nYou're no longer working {wasText}.");
        }

        if (after is not null)
        {
            Queue(after, "You have a new shift", $"Hi {after.Name},\n\nYou're now working {nowText}.");
        }
    }

    private static string InReturn(Shift? returned, string what, TimeZoneInfo zone, bool plain = false) =>
        returned is null ? "" : plain
            ? $" for {what} on {When(returned.Start, returned.End, zone)}"
            : $", and to {what} on {When(returned.Start, returned.End, zone)} in return";

    private static string Format(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private void Queue(StaffMember to, string subject, string body) =>
        db.OutboxMessages.Add(new OutboxMessage(to.TenantId, to.Email, subject, body, clock.GetUtcNow()));
}
