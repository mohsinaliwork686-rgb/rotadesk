using System.Net;

using Rotadesk.Domain;

namespace Rotadesk.Api.Tests;

public class DemoTests(ApiFixture fixture)
{
    [Fact]
    public async Task The_demo_lists_who_a_visitor_can_be()
    {
        var accounts = await fixture.NewClient().GetOkAsync<List<DemoAccount>>("/api/demo/accounts");

        Assert.Contains(new DemoAccount("marcus@northside.example", "Marcus", StaffRole.Manager, "Northside Café"), accounts);
        Assert.Equal(8, accounts.Count);
    }

    [Fact]
    public async Task One_click_signs_a_visitor_in_as_a_manager_at_northside_cafe()
    {
        var client = fixture.NewClient();

        var response = await client.PostJsonAsync("/api/demo/sign-in", new { email = "marcus@northside.example" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var me = await client.GetOkAsync<MeResponse>("/api/auth/me");
        Assert.Equal(("Marcus", StaffRole.Manager, "Northside Café"), (me.Name, me.Role, me.Business.Name));
    }

    [Fact]
    public async Task Only_the_demo_people_can_be_signed_in_as()
    {
        var business = await fixture.CreateBusinessAsync();

        var response = await fixture.NewClient().PostJsonAsync("/api/demo/sign-in", new { email = business.Owner.Email });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task With_the_demo_switched_off_its_endpoints_dont_exist()
    {
        await using var factory = new RotadeskFactory(fixture.ConnectionString, demo: false);
        var client = factory.CreateClient();

        var accounts = await client.GetAsync("/api/demo/accounts", TestContext.Current.CancellationToken);
        var signIn = await client.PostJsonAsync("/api/demo/sign-in", new { email = "marcus@northside.example" });

        Assert.Equal(HttpStatusCode.NotFound, accounts.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, signIn.StatusCode);
    }
}
