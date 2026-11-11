using System.Net;

using static Rotadesk.Api.Tests.Http;

namespace Rotadesk.Api.Tests;

public class RoleTests(ApiFixture fixture)
{
    [Fact]
    public async Task Staff_cant_add_shifts()
    {
        var business = await fixture.CreateBusinessAsync();
        var staff = await fixture.SignInAsync(business.Staff);

        var response = await staff.PostJsonAsync("/api/shifts", new { start = At(Monday, 9), end = At(Monday, 17) });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Staff_cant_publish_a_week()
    {
        var business = await fixture.CreateBusinessAsync();
        var staff = await fixture.SignInAsync(business.Staff);

        var response = await staff.PostJsonAsync($"/api/weeks/{Monday:yyyy-MM-dd}/publish");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Managers_cant_add_or_change_staff()
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);

        var add = await manager.PostJsonAsync("/api/staff", new { name = "Cara", email = $"cara.{Guid.NewGuid():N}@example.test", role = "Staff" });
        var promote = await manager.PatchJsonAsync($"/api/staff/{business.Manager.Id}", new { role = "Owner" });

        Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, promote.StatusCode);
    }

    [Fact]
    public async Task A_demoted_manager_loses_manager_endpoints_on_their_very_next_request()
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);
        var owner = await fixture.SignInAsync(business.Owner);
        Assert.Equal(HttpStatusCode.Created, (await manager.PostJsonAsync("/api/shifts", new { start = At(Monday, 9), end = At(Monday, 12) })).StatusCode);

        var demote = await owner.PatchJsonAsync($"/api/staff/{business.Manager.Id}", new { role = "Staff" });
        var after = await manager.PostJsonAsync("/api/shifts", new { start = At(Monday, 13), end = At(Monday, 16) });

        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }
}
