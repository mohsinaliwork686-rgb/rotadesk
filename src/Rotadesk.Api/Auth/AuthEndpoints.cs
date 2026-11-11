using Microsoft.AspNetCore.Identity;

using Rotadesk.Infrastructure.Accounts;

namespace Rotadesk.Api.Auth;

internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth");

        auth.MapPost("/sign-in", SignInAsync).AllowAnonymous();
        auth.MapPost("/sign-out", async (SignInManager<RotadeskUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).AllowAnonymous();
        auth.MapGet("/me", MeAsync).RequireAuthorization(Policies.Member);
    }

    private static async Task<IResult> SignInAsync(SignInRequest request, SignInManager<RotadeskUser> signIn)
    {
        // Five wrong passwords lock the account for five minutes, so a password can't be guessed quickly.
        var result = await signIn.PasswordSignInAsync(request.Email ?? "", request.Password ?? "", isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            return Results.NoContent();
        }

        return result.IsLockedOut
            ? Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many wrong passwords. Try again in a few minutes.")
            : Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Wrong email or password.");
    }

    private static async Task<IResult> MeAsync(CurrentMember current, CancellationToken cancellationToken)
    {
        var member = await current.GetAsync(cancellationToken);
        var business = await current.GetBusinessAsync(cancellationToken);
        return Results.Ok(new MeResponse(
            member.Id,
            member.Name,
            member.Email,
            member.Role,
            new BusinessResponse(business.Id, business.Name, business.TimeZoneId)));
    }
}
