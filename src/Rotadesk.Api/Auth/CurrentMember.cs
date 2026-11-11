using System.Security.Claims;

using Microsoft.EntityFrameworkCore;

using Rotadesk.Domain;
using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Api.Auth;

/// <summary>
/// The signed-in person's staff record, read once per request through the tenant filter. If the cookie names a staff
/// member of another business, or one that no longer exists, there is no current member and every policy fails.
/// </summary>
internal sealed class CurrentMember(IHttpContextAccessor http, RotadeskDbContext db)
{
    private StaffMember? _member;
    private Business? _business;
    private bool _looked;

    public async Task<StaffMember?> FindAsync(CancellationToken cancellationToken)
    {
        if (!_looked)
        {
            _looked = true;
            if (Guid.TryParse(http.HttpContext?.User.FindFirstValue(RotadeskClaims.StaffMemberId), out var id))
            {
                _member = await db.StaffMembers.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
            }
        }

        return _member;
    }

    /// <summary>For endpoints behind a policy, which has already found the member.</summary>
    public async Task<StaffMember> GetAsync(CancellationToken cancellationToken) =>
        await FindAsync(cancellationToken)
        ?? throw new InvalidOperationException("There is no signed-in member. The endpoint needs a policy.");

    public async Task<Business> GetBusinessAsync(CancellationToken cancellationToken)
    {
        var member = await GetAsync(cancellationToken);
        return _business ??= await db.Businesses.SingleAsync(b => b.Id == member.TenantId, cancellationToken);
    }
}
