using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Rotadesk.Api.Auth;
using Rotadesk.Api.Errors;
using Rotadesk.Domain;
using Rotadesk.Infrastructure.Accounts;
using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Api.Staff;

internal static class StaffEndpoints
{
    public static void MapStaffEndpoints(this IEndpointRouteBuilder api)
    {
        var staff = api.MapGroup("/staff");

        staff.MapGet("", ListAsync).RequireAuthorization(Policies.Member);
        staff.MapPost("", CreateAsync).RequireAuthorization(Policies.Owner);
        staff.MapPatch("/{id:guid}", UpdateAsync).RequireAuthorization(Policies.Owner);
    }

    private static async Task<IResult> ListAsync(RotadeskDbContext db, CancellationToken cancellationToken)
    {
        var staff = await db.StaffMembers.AsNoTracking().OrderBy(s => s.Name).ToListAsync(cancellationToken);
        return Results.Ok(staff.Select(StaffResponse.From));
    }

    private static async Task<IResult> CreateAsync(
        CreateStaffRequest request,
        CurrentMember current,
        RotadeskDbContext db,
        UserManager<RotadeskUser> users,
        CancellationToken cancellationToken)
    {
        var owner = await current.GetAsync(cancellationToken);
        var member = new StaffMember(owner.TenantId, request.Name ?? "", request.Email ?? "", request.Role, Hours(request.WeeklyLimitHours));
        if (await users.FindByEmailAsync(member.Email) is not null)
        {
            return Problems.Conflict("staff.email-taken", $"{member.Email} already has an account.");
        }

        db.StaffMembers.Add(member);
        await db.SaveChangesAsync(cancellationToken);

        var password = TemporaryPassword.Create();
        var created = await users.CreateAsync(new RotadeskUser(member.TenantId, member.Id, member.Email), password);
        if (!created.Succeeded)
        {
            // The two contexts can't share a transaction, so undo the staff member rather than leave one nobody can
            // sign in as. A taken email is a conflict; anything else Identity refuses is bad input.
            db.StaffMembers.Remove(member);
            await db.SaveChangesAsync(cancellationToken);
            var reason = string.Join(" ", created.Errors.Select(e => e.Description));
            return created.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName")
                ? Problems.Conflict("staff.email-taken", reason)
                : Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: reason, extensions: new Dictionary<string, object?> { ["code"] = "staff.invalid-email" });
        }

        return Results.Created($"/api/staff/{member.Id}", new CreatedStaffResponse(StaffResponse.From(member), password));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateStaffRequest request, RotadeskDbContext db, CancellationToken cancellationToken)
    {
        var member = await db.StaffMembers.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (member is null)
        {
            return Problems.NotFound("Staff member");
        }

        if (request.Role is { } role && role != member.Role)
        {
            if (member.Role == StaffRole.Owner
                && !await db.StaffMembers.AnyAsync(s => s.Id != id && s.Role == StaffRole.Owner, cancellationToken))
            {
                return Problems.Conflict("staff.last-owner", "A business needs at least one owner. Make someone else an owner first.");
            }

            member.ChangeRole(role);
        }

        if (Hours(request.WeeklyLimitHours) is { } limit)
        {
            member.SetWeeklyLimit(limit);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(StaffResponse.From(member));
    }

    /// <summary>Hours to a span. The domain checks the range; this only keeps a huge number from overflowing first.</summary>
    private static TimeSpan? Hours(double? hours) => hours switch
    {
        null => null,
        { } h when double.IsFinite(h) && Math.Abs(h) <= 10_000 => TimeSpan.FromHours(h),
        _ => throw new DomainException("staff.invalid-weekly-limit", "The weekly limit must be more than zero and at most a whole week."),
    };
}
