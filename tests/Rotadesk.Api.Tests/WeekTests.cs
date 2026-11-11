using System.Net;

using static Rotadesk.Api.Tests.Http;

namespace Rotadesk.Api.Tests;

public class WeekTests(ApiFixture fixture)
{
    private static string Url(DateOnly monday, string? action = null) =>
        action is null ? $"/api/weeks/{monday:yyyy-MM-dd}" : $"/api/weeks/{monday:yyyy-MM-dd}/{action}";

    [Fact]
    public async Task Staff_see_no_shifts_until_the_week_is_published()
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);
        var staff = await fixture.SignInAsync(business.Staff);
        await manager.AddShiftAsync(business.Staff, At(Monday, 9), At(Monday, 17));

        var before = await staff.GetOkAsync<WeekResponse>(Url(Monday));
        Assert.Equal(HttpStatusCode.OK, (await manager.PostJsonAsync(Url(Monday, "publish"))).StatusCode);
        var after = await staff.GetOkAsync<WeekResponse>(Url(Monday));

        Assert.False(before.Published);
        Assert.Empty(before.Shifts);
        Assert.True(after.Published);
        Assert.Single(after.Shifts);
    }

    [Fact]
    public async Task A_week_can_be_published_only_once()
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);
        await manager.PostJsonAsync(Url(Monday, "publish"));

        var again = await manager.PostJsonAsync(Url(Monday, "publish"));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("rota.already-published", await again.ProblemCodeAsync());
    }

    [Fact]
    public async Task A_week_starts_on_a_monday()
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);

        var response = await manager.GetAsync(Url(Monday.AddDays(2)), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("week.not-monday", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Managers_see_each_shifts_warnings_on_the_rota()
    {
        var business = await fixture.CreateBusinessAsync();
        await (await fixture.SignInAsync(business.Staff)).PutJsonAsync("/api/availability/mine", new[] { new { day = "Monday", from = "12:00", to = "18:00" } });
        var manager = await fixture.SignInAsync(business.Manager);
        await manager.AddShiftAsync(business.Staff, At(Monday, 9), At(Monday, 17));

        var week = await manager.GetOkAsync<WeekResponse>(Url(Monday));

        Assert.Equal(["Outside Amira's availability on Monday."], Assert.Single(week.Shifts).Warnings);
    }

    [Fact]
    public async Task Copying_last_week_keeps_local_times_when_the_clocks_change()
    {
        // London's clocks went back on Sunday 25 October 2026. 09:00 is 08:00 UTC the week before and 09:00 UTC after.
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);
        var summer = new DateOnly(2026, 10, 19);
        var winter = new DateOnly(2026, 10, 26);
        await manager.AddShiftAsync(business.Staff, At(summer, 8), At(summer, 16));

        var response = await manager.PostJsonAsync(Url(winter, "copy-previous"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var copy = Assert.Single((await response.ReadAsync<WeekResponse>()).Shifts);
        Assert.Equal(At(winter, 9), copy.Start);
        Assert.Equal(At(winter, 17), copy.End);
        Assert.Equal(business.Staff.Id, copy.StaffMemberId);
    }

    [Fact]
    public async Task Copying_into_a_week_that_already_has_shifts_is_refused()
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);
        await manager.AddShiftAsync(business.Staff, At(Monday.AddDays(-7), 9), At(Monday.AddDays(-7), 17));
        await manager.AddShiftAsync(business.Colleague, At(Monday, 9), At(Monday, 17));

        var response = await manager.PostJsonAsync(Url(Monday, "copy-previous"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("week.not-empty", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task A_shift_its_person_cant_take_any_more_is_copied_without_them_and_says_why()
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);
        await manager.AddShiftAsync(business.Staff, At(Monday.AddDays(-7), 9), At(Monday.AddDays(-7), 17));
        await (await fixture.SignInAsync(business.Owner)).PatchJsonAsync($"/api/staff/{business.Staff.Id}", new { weeklyLimitHours = 4 });

        var week = await (await manager.PostJsonAsync(Url(Monday, "copy-previous"))).ReadAsync<WeekResponse>();

        var copy = Assert.Single(week.Shifts);
        Assert.Null(copy.StaffMemberId);
        Assert.StartsWith("Copied without Amira: Amira would work 8h", copy.Warnings[0]);
    }
}
