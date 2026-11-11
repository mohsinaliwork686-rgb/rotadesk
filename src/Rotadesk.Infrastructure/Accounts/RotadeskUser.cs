using Microsoft.AspNetCore.Identity;

namespace Rotadesk.Infrastructure.Accounts;

/// <summary>
/// A sign-in account. Each one belongs to exactly one staff member, and so to one business. Its role is not
/// stored here: it is read from the staff member on every request, so a change takes effect straight away.
/// </summary>
public sealed class RotadeskUser : IdentityUser<Guid>
{
    private RotadeskUser()
    {
    }

    public RotadeskUser(Guid tenantId, Guid staffMemberId, string email)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        StaffMemberId = staffMemberId;
        UserName = email;
        Email = email;
    }

    public Guid TenantId { get; private set; }

    public Guid StaffMemberId { get; private set; }
}
