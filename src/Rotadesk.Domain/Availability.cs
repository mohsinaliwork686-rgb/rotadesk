namespace Rotadesk.Domain;

/// <summary>
/// A weekly window when someone can usually work, in the business's local time. A window stays within
/// one day, and <see cref="EndOfDay"/> as the end means until midnight. Someone who can work
/// overnight adds two windows: one until midnight, and one from midnight on the next day.
/// </summary>
public sealed class Availability : ITenantOwned
{
    /// <summary>
    /// The end of a window that runs until midnight. It is a microsecond before midnight, not
    /// <see cref="TimeOnly.MaxValue"/>, because PostgreSQL stores times to the microsecond.
    /// </summary>
    public static readonly TimeOnly EndOfDay = new(23, 59, 59, 999, 999);

    private Availability()
    {
    }

    public Availability(Guid tenantId, Guid staffMemberId, DayOfWeek day, TimeOnly from, TimeOnly to)
    {
        if (to <= from)
        {
            throw new DomainException("availability.invalid-times", "An availability window has to end after it starts, on the same day.");
        }

        Id = Guid.NewGuid();
        TenantId = tenantId;
        StaffMemberId = staffMemberId;
        Day = day;
        From = from;
        To = to > EndOfDay ? EndOfDay : to;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid StaffMemberId { get; private set; }

    public DayOfWeek Day { get; private set; }

    public TimeOnly From { get; private set; }

    public TimeOnly To { get; private set; }
}
