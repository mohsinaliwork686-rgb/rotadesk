namespace Rotadesk.Domain;

/// <summary>The tenant. Weeks and availability are worked out in its time zone.</summary>
public sealed class Business
{
    private Business()
    {
    }

    public Business(string name, string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("business.name-required", "A business needs a name.");
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _))
        {
            throw new DomainException("business.unknown-time-zone", $"'{timeZoneId}' is not a known time zone.");
        }

        Id = Guid.NewGuid();
        Name = name.Trim();
        TimeZoneId = timeZoneId;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = "";

    public string TimeZoneId { get; private set; } = "";

    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
}
