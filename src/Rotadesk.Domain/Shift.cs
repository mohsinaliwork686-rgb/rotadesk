namespace Rotadesk.Domain;

public sealed class Shift : ITenantOwned
{
    public static readonly TimeSpan MaxLength = TimeSpan.FromHours(16);

    private Shift()
    {
    }

    public Shift(Guid tenantId, Guid rotaWeekId, DateTimeOffset start, DateTimeOffset end, Guid? staffMemberId = null)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        RotaWeekId = rotaWeekId;
        Reschedule(start, end);
        StaffMemberId = staffMemberId;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid RotaWeekId { get; private set; }

    /// <summary>Stored in UTC.</summary>
    public DateTimeOffset Start { get; private set; }

    /// <summary>Stored in UTC. The shift ends just before this moment.</summary>
    public DateTimeOffset End { get; private set; }

    public Guid? StaffMemberId { get; private set; }

    public TimeSpan Length => End - Start;

    public void Reschedule(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new DomainException("shift.invalid-times", "A shift has to end after it starts.");
        }

        if (end - start > MaxLength)
        {
            throw new DomainException("shift.too-long", $"A shift can be at most {MaxLength.TotalHours:0} hours long.");
        }

        Start = start.ToUniversalTime();
        End = end.ToUniversalTime();
    }

    public void AssignTo(Guid? staffMemberId) => StaffMemberId = staffMemberId;

    /// <summary>True if the two shifts share any moment. Back-to-back shifts don't overlap.</summary>
    public bool Overlaps(DateTimeOffset start, DateTimeOffset end) => Start < end && start < End;
}
