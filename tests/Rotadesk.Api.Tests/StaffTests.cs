using System.Net;

using Rotadesk.Domain;

namespace Rotadesk.Api.Tests;

public class StaffTests(ApiFixture fixture)
{
    [Fact]
    public async Task Staff_can_see_their_colleagues()
    {
        var business = await fixture.CreateBusinessAsync();
        var staff = await fixture.SignInAsync(business.Staff);

        var colleagues = await staff.GetOkAsync<List<StaffResponse>>("/api/staff");

        Assert.Equal(["Amira", "Ben", "Max", "Olga"], colleagues.Select(c => c.Name));
    }

    [Fact]
    public async Task The_owner_adds_someone_who_can_then_sign_in_with_the_temporary_password()
    {
        var business = await fixture.CreateBusinessAsync();
        var owner = await fixture.SignInAsync(business.Owner);
        var email = $"cara.{Guid.NewGuid():N}@example.test";

        var response = await owner.PostJsonAsync("/api/staff", new { name = "Cara", email, role = "Staff", weeklyLimitHours = 20 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadAsync<CreatedStaffResponse>();
        Assert.Equal(20, created.Staff.WeeklyLimitHours);
        var cara = await fixture.SignInAsync(new TestPerson(created.Staff.Id, "Cara", email, created.TemporaryPassword, StaffRole.Staff));
        Assert.Equal(business.Name, (await cara.GetOkAsync<MeResponse>("/api/auth/me")).Business.Name);
    }

    [Fact]
    public async Task An_email_that_already_has_an_account_is_refused()
    {
        var business = await fixture.CreateBusinessAsync();
        var other = await fixture.CreateBusinessAsync();
        var owner = await fixture.SignInAsync(business.Owner);

        var response = await owner.PostJsonAsync("/api/staff", new { name = "Ben again", email = other.Colleague.Email, role = "Staff" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("staff.email-taken", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task The_last_owner_cant_be_demoted()
    {
        var business = await fixture.CreateBusinessAsync();
        var owner = await fixture.SignInAsync(business.Owner);

        var response = await owner.PatchJsonAsync($"/api/staff/{business.Owner.Id}", new { role = "Manager" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("staff.last-owner", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task An_owner_can_step_down_once_someone_else_owns_the_business()
    {
        var business = await fixture.CreateBusinessAsync();
        var owner = await fixture.SignInAsync(business.Owner);
        await owner.PatchJsonAsync($"/api/staff/{business.Manager.Id}", new { role = "Owner" });

        var response = await owner.PatchJsonAsync($"/api/staff/{business.Owner.Id}", new { role = "Manager" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(StaffRole.Manager, (await response.ReadAsync<StaffResponse>()).Role);
    }

    [Fact]
    public async Task A_weekly_limit_longer_than_a_week_is_bad_input()
    {
        var business = await fixture.CreateBusinessAsync();
        var owner = await fixture.SignInAsync(business.Owner);

        var response = await owner.PatchJsonAsync($"/api/staff/{business.Staff.Id}", new { weeklyLimitHours = 200 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("staff.invalid-weekly-limit", await response.ProblemCodeAsync());
    }
}
