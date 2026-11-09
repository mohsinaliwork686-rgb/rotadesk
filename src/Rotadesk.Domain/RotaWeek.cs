namespace Rotadesk.Domain;

/// <summary>One week of one business's rota. Staff see its shifts once it's published.</summary>
public sealed class RotaWeek : ITenantOwned
{
    private RotaWeek()
    {
    }

    public RotaWeek(Guid tenantId, DateOnly weekStart)
    {
        if (weekStart.DayOfWeek != DayOfWeek.Monday)
        {
            throw new DomainException("week.not-monday", $"A week starts on a Monday, not on {weekStart.DayOfWeek}.");
        }

        Id = Guid.NewGuid();
        TenantId = tenantId;
        WeekStart = weekStart;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public DateOnly WeekStart { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public bool IsPublished => PublishedAt is not null;

    public void Publish(DateTimeOffset now)
    {
        if (IsPublished)
        {
            throw new DomainException("rota.already-published", "This week is already published.");
        }

        PublishedAt = now;
    }
}
