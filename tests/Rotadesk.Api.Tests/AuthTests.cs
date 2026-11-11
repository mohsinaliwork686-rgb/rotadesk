using System.Net;

using Rotadesk.Domain;

namespace Rotadesk.Api.Tests;

public class AuthTests(ApiFixture fixture)
{
    [Fact]
    public async Task Signing_in_shows_who_you_are_and_where_you_work()
    {
        var business = await fixture.CreateBusinessAsync();
        var client = await fixture.SignInAsync(business.Manager);

        var me = await client.GetOkAsync<MeResponse>("/api/auth/me");

        Assert.Equal(business.Manager.Id, me.StaffMemberId);
        Assert.Equal(StaffRole.Manager, me.Role);
        Assert.Equal(business.Name, me.Business.Name);
        Assert.Equal("Europe/London", me.Business.TimeZone);
    }

    [Fact]
    public async Task A_wrong_password_is_a_401_and_signs_nobody_in()
    {
        var business = await fixture.CreateBusinessAsync();
        var client = fixture.NewClient();

        var response = await client.PostJsonAsync("/api/auth/sign-in", new { email = business.Staff.Email, password = "not-it" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Without_signing_in_the_api_answers_401_not_a_redirect()
    {
        var response = await fixture.NewClient().GetAsync("/api/staff", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var business = await fixture.CreateBusinessAsync();
        var client = await fixture.SignInAsync(business.Staff);

        await client.PostJsonAsync("/api/auth/sign-out");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_against_the_right_one()
    {
        var business = await fixture.CreateBusinessAsync();
        var client = fixture.NewClient();
        for (var i = 0; i < 5; i++)
        {
            await client.PostJsonAsync("/api/auth/sign-in", new { email = business.Staff.Email, password = $"wrong-{i}" });
        }

        var response = await client.PostJsonAsync("/api/auth/sign-in", new { email = business.Staff.Email, password = business.Staff.Password });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }
}
