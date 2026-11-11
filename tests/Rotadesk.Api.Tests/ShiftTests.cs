using System.Net;

using static Rotadesk.Api.Tests.Http;

namespace Rotadesk.Api.Tests;

public class ShiftTests(ApiFixture fixture)
{
    [Fact]
    public async Task A_shift_outside_someones_availability_is_saved_with_a_warning()
    {
        var business = await fixture.CreateBusinessAsync();
        var staff = await fixture.SignInAsync(business.Staff);
        await staff.PutJsonAsync("/api/availability/mine", new[] { new { day = "Monday", from = "12:00", to = "18:00" } });
        var manager = await fixture.SignInAsync(business.Manager);

        var shift = await manager.AddShiftAsync(business.Staff, At(Monday, 9), At(Monday, 17));

        Assert.Equal(business.Staff.Id, shift.StaffMemberId);
        Assert.Equal("Amira", shift.StaffName);
        Assert.Equal(["Outside Amira's availability on Monday."], shift.Warnings);
    }

    [Fact]
    public async Task An_overlapping_shift_is_refused_and_names_the_rule()
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);
        await manager.AddShiftAsync(business.Staff, At(Monday, 9), At(Monday, 17));

        var response = await manager.PostJsonAsync("/api/shifts", new { start = At(Monday, 16, 59), end = At(Monday, 20), staffMemberId = business.Staff.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("shift.overlap", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Back_to_back_shifts_are_allowed()
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);
        await manager.AddShiftAsync(business.Staff, At(Monday, 9), At(Monday, 17));

        var next = await manager.AddShiftAsync(business.Staff, At(Monday, 17), At(Monday, 21));

        Assert.Empty(next.Warnings);
    }

    [Fact]
    public async Task Going_over_the_weekly_limit_is_refused()
    {
        var business = await fixture.CreateBusinessAsync();
        await (await fixture.SignInAsync(business.Owner)).PatchJsonAsync($"/api/staff/{business.Staff.Id}", new { weeklyLimitHours = 8 });
        var manager = await fixture.SignInAsync(business.Manager);
        await manager.AddShiftAsync(business.Staff, At(Monday, 9), At(Monday, 16));

        var response = await manager.PostJsonAsync("/api/shifts", new { start = At(Monday.AddDays(1), 9), end = At(Monday.AddDays(1), 10, 1), staffMemberId = business.Staff.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("shift.weekly-limit", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task A_shift_that_ends_before_it_starts_is_bad_input()
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);

        var response = await manager.PostJsonAsync("/api/shifts", new { start = At(Monday, 17), end = At(Monday, 9) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("shift.invalid-times", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task A_shift_can_be_left_for_later_without_anyone_on_it()
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);

        var shift = await manager.AddShiftAsync(null, At(Monday, 9), At(Monday, 17));

        Assert.Null(shift.StaffMemberId);
    }

    [Fact]
    public async Task Moving_a_shift_is_checked_without_clashing_with_its_old_self()
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);
        var shift = await manager.AddShiftAsync(business.Staff, At(Monday, 9), At(Monday, 17));

        var response = await manager.PutJsonAsync($"/api/shifts/{shift.Id}", new { start = At(Monday, 10), end = At(Monday, 18), staffMemberId = business.Staff.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(At(Monday, 10), (await response.ReadAsync<ShiftResponse>()).Start);
    }

    [Fact]
    public async Task A_shift_cant_move_to_another_week()
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);
        var shift = await manager.AddShiftAsync(business.Staff, At(Monday, 9), At(Monday, 17));

        var response = await manager.PutJsonAsync($"/api/shifts/{shift.Id}", new { start = At(Monday.AddDays(7), 9), end = At(Monday.AddDays(7), 17) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("shift.other-week", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task A_deleted_shift_leaves_the_rota()
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);
        var shift = await manager.AddShiftAsync(business.Staff, At(Monday, 9), At(Monday, 17));

        var response = await manager.DeleteAsync($"/api/shifts/{shift.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty((await manager.GetOkAsync<WeekResponse>($"/api/weeks/{Monday:yyyy-MM-dd}")).Shifts);
    }
}
