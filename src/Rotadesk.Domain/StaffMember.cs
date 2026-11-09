namespace Rotadesk.Domain;

public sealed class StaffMember : ITenantOwned
{
    public static readonly TimeSpan DefaultWeeklyLimit = TimeSpan.FromHours(40);

    private StaffMember()
    {
    }

    public StaffMember(Guid tenantId, string name, string email, StaffRole role, TimeSpan? weeklyLimit = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("staff.name-required", "A staff member needs a name.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("staff.email-required", "A staff member needs an email address.");
        }

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Name = name.Trim();
        Email = email.Trim();
        Role = role;
        SetWeeklyLimit(weeklyLimit ?? DefaultWeeklyLimit);
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = "";

    public string Email { get; private set; } = "";

    public StaffRole Role { get; private set; }

    public TimeSpan WeeklyLimit { get; private set; }

    public bool IsManager => Role is StaffRole.Manager or StaffRole.Owner;

    public void ChangeRole(StaffRole role) => Role = role;

    public void SetWeeklyLimit(TimeSpan limit)
    {
        if (limit <= TimeSpan.Zero || limit > TimeSpan.FromHours(7 * 24))
        {
            throw new DomainException("staff.invalid-weekly-limit", "The weekly limit must be more than zero and at most a whole week.");
        }

        WeeklyLimit = limit;
    }
}
