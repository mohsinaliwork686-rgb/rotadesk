using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

using Rotadesk.Domain;
using Rotadesk.Infrastructure.Accounts;

namespace Rotadesk.Api.Auth;

internal static class SignInSetup
{
    public static IServiceCollection AddRotadeskSignIn(this IServiceCollection services)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(cookie =>
        {
            cookie.Cookie.Name = "rotadesk";
            cookie.Cookie.HttpOnly = true;

            // The front end is on the same origin. Lax keeps the cookie off cross-site POSTs, and every change is a
            // JSON request, which a plain HTML form on another site can't send.
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
            cookie.SlidingExpiration = true;

            // An API answers with status codes, not redirects to a login page.
            cookie.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            cookie.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services.AddIdentityCore<RotadeskUser>(identity =>
            {
                identity.User.RequireUniqueEmail = true;
                identity.Lockout.MaxFailedAccessAttempts = 5;
                identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddEntityFrameworkStores<AccountsDbContext>()
            .AddSignInManager()
            .AddClaimsPrincipalFactory<RotadeskClaimsFactory>();

        services.AddScoped<IAuthorizationHandler, RoleAtLeastHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Member, p => p.RequireAuthenticatedUser().AddRequirements(new RoleAtLeast(StaffRole.Staff)))
            .AddPolicy(Policies.Manager, p => p.RequireAuthenticatedUser().AddRequirements(new RoleAtLeast(StaffRole.Manager)))
            .AddPolicy(Policies.Owner, p => p.RequireAuthenticatedUser().AddRequirements(new RoleAtLeast(StaffRole.Owner)));

        return services;
    }
}
