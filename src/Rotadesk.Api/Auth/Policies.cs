using Microsoft.AspNetCore.Authorization;

using Rotadesk.Domain;

namespace Rotadesk.Api.Auth;

internal static class Policies
{
    /// <summary>Anyone signed in to the business.</summary>
    public const string Member = nameof(Member);

    /// <summary>A Manager or the Owner.</summary>
    public const string Manager = nameof(Manager);

    public const string Owner = nameof(Owner);
}

/// <summary>Passes when the signed-in member's current role is at least this one: Staff, then Manager, then Owner.</summary>
internal sealed record RoleAtLeast(StaffRole Role) : IAuthorizationRequirement;

internal sealed class RoleAtLeastHandler(CurrentMember current) : AuthorizationHandler<RoleAtLeast>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, RoleAtLeast requirement)
    {
        var member = await current.FindAsync(CancellationToken.None);
        if (member is not null && Rank(member.Role) >= Rank(requirement.Role))
        {
            context.Succeed(requirement);
        }
    }

    private static int Rank(StaffRole role) => role switch
    {
        StaffRole.Owner => 2,
        StaffRole.Manager => 1,
        _ => 0,
    };
}
