using System.Security.Claims;

using Rotadesk.Infrastructure;

namespace Rotadesk.Api.Auth;

/// <summary>The business of the signed-in user, from their cookie. Never from the request itself.</summary>
internal sealed class ClaimsTenant(IHttpContextAccessor http) : ITenantContext
{
    public Guid? TenantId =>
        Guid.TryParse(http.HttpContext?.User.FindFirstValue(RotadeskClaims.TenantId), out var id) ? id : null;

    public bool IsSystem => false;
}

internal static class RotadeskClaims
{
    public const string TenantId = "tenant_id";

    public const string StaffMemberId = "staff_member_id";
}
