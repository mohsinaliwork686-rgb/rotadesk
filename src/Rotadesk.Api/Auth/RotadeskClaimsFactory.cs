using System.Security.Claims;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Rotadesk.Infrastructure.Accounts;

namespace Rotadesk.Api.Auth;

/// <summary>Puts who you are in the cookie: your business and your staff member. Your role is read fresh on each request.</summary>
internal sealed class RotadeskClaimsFactory(UserManager<RotadeskUser> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<RotadeskUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(RotadeskUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(RotadeskClaims.TenantId, user.TenantId.ToString()));
        identity.AddClaim(new Claim(RotadeskClaims.StaffMemberId, user.StaffMemberId.ToString()));
        return identity;
    }
}
