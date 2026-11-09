namespace Rotadesk.Domain;

/// <summary>Anything that belongs to one business. The database filters these by the current tenant.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
