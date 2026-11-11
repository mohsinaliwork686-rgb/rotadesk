using System.Net;

using Rotadesk.Domain;

using static Rotadesk.Api.Tests.Http;

namespace Rotadesk.Api.Tests;

public class AvailabilityTests(ApiFixture fixture)
{
    [Fact]
    public async Task Windows_that_overlap_or_touch_are_merged_and_midnight_is_24_00()
    {
        var business = await fixture.CreateBusinessAsync();
        var staff = await fixture.SignInAsync(business.Staff);

        var response = await staff.PutJsonAsync("/api/availability/mine", new[]
        {
            new { day = "Monday", from = "13:00", to = "17:00" },
            new { day = "Friday", from = "18:00", to = "24:00" },
            new { day = "Monday", from = "09:00", to = "13:00" },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [new AvailabilityWindow(DayOfWeek.Monday, "09:00", "17:00"), new AvailabilityWindow(DayOfWeek.Friday, "18:00", "24:00")],
            await staff.GetOkAsync<List<AvailabilityWindow>>("/api/availability/mine"));
    }

    [Fact]
    public async Task A_shift_until_midnight_fits_a_window_until_24_00()
    {
        var business = await fixture.CreateBusinessAsync();
        await (await fixture.SignInAsync(business.Staff)).PutJsonAsync("/api/availability/mine", new[] { new { day = "Friday", from = "18:00", to = "24:00" } });
        var manager = await fixture.SignInAsync(business.Manager);

        var shift = await manager.AddShiftAsync(business.Staff, At(Monday.AddDays(4), 18), At(Monday.AddDays(5), 0));

        Assert.Empty(shift.Warnings);
    }

    [Fact]
    public async Task A_time_that_isnt_a_time_is_bad_input()
    {
        var business = await fixture.CreateBusinessAsync();
        var staff = await fixture.SignInAsync(business.Staff);

        var response = await staff.PutJsonAsync("/api/availability/mine", new[] { new { day = "Monday", from = "9am", to = "17:00" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("availability.invalid-time", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Managers_can_see_anyones_availability()
    {
        var business = await fixture.CreateBusinessAsync();
        await (await fixture.SignInAsync(business.Staff)).PutJsonAsync("/api/availability/mine", new[] { new { day = "Tuesday", from = "10:00", to = "14:00" } });
        var manager = await fixture.SignInAsync(business.Manager);

        var windows = await manager.GetOkAsync<List<AvailabilityWindow>>($"/api/staff/{business.Staff.Id}/availability");

        Assert.Equal([new AvailabilityWindow(DayOfWeek.Tuesday, "10:00", "14:00")], windows);
    }

    [Fact]
    public async Task Staff_cant_see_a_colleagues_availability()
    {
        var business = await fixture.CreateBusinessAsync();
        var staff = await fixture.SignInAsync(business.Staff);

        var response = await staff.GetAsync($"/api/staff/{business.Colleague.Id}/availability", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
