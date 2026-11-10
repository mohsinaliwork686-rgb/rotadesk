namespace Rotadesk.Infrastructure;

/// <summary>The business the current work belongs to. The API reads it from the signed-in user.</summary>
public interface ITenantContext
{
    /// <summary>The current business, or null when there is none, which then sees and changes nothing.</summary>
    Guid? TenantId { get; }

    /// <summary>
    /// True only for background work that spans businesses, such as the outbox worker. It may read and write
    /// every business's rows, so the code that uses it has to filter by tenant itself.
    /// </summary>
    bool IsSystem { get; }
}

/// <summary>A fixed tenant, for one business's request or a test.</summary>
public sealed class FixedTenant(Guid tenantId) : ITenantContext
{
    public Guid? TenantId { get; } = tenantId;

    public bool IsSystem => false;
}

/// <summary>Cross-business background work. See <see cref="ITenantContext.IsSystem"/>.</summary>
public sealed class SystemTenant : ITenantContext
{
    public static readonly SystemTenant Instance = new();

    private SystemTenant()
    {
    }

    public Guid? TenantId => null;

    public bool IsSystem => true;
}

/// <summary>Thrown when a change would touch another business's data.</summary>
public sealed class TenantViolationException : Exception
{
    public TenantViolationException()
        : base("A change touched another business's data.")
    {
    }

    public TenantViolationException(string message)
        : base(message)
    {
    }

    public TenantViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
