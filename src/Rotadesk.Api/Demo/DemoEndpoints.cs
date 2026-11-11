using Microsoft.AspNetCore.Identity;

using Rotadesk.Api.Errors;
using Rotadesk.Infrastructure.Accounts;

namespace Rotadesk.Api.Demo;

/// <summary>One-click sign-in for the live demo. Mapped only when <c>Demo:Enabled</c> is set.</summary>
internal static class DemoEndpoints
{
    public static void MapDemoEndpoints(this IEndpointRouteBuilder api)
    {
        var demo = api.MapGroup("/demo").AllowAnonymous();

        demo.MapGet("/accounts", () => Results.Ok(
            DemoData.Businesses.SelectMany(b => b.People.Select(p => new DemoAccount(p.Email, p.Name, p.Role, b.Name)))));

        demo.MapPost("/sign-in", async (DemoSignInRequest request, UserManager<RotadeskUser> users, SignInManager<RotadeskUser> signIn) =>
        {
            // Only the seeded demo people, so the switch can't be used to sign in as anyone else.
            if (!DemoData.IsDemoAccount(request.Email) || await users.FindByEmailAsync(request.Email!) is not { } user)
            {
                return Problems.NotFound("Demo account");
            }

            await signIn.SignInAsync(user, isPersistent: false);
            return Results.NoContent();
        });
    }
}
