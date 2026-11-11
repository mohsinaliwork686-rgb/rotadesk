using System.Net;

using static Rotadesk.Api.Tests.Http;

namespace Rotadesk.Api.Tests;

/// <summary>Each business sees and changes only its own data. Another business's ids are a 404, never a 403.</summary>
public class TenancyTests(ApiFixture fixture)
{
    [Fact]
    public async Task The_staff_list_shows_only_your_business()
    {
        var a = await fixture.CreateBusinessAsync();
        await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(a.Manager);

        var staff = await manager.GetOkAsync<List<StaffResponse>>("/api/staff");

        Assert.Equal(new[] { a.Owner.Id, a.Manager.Id, a.Staff.Id, a.Colleague.Id }.Order(), staff.Select(s => s.Id).Order());
    }

    [Fact]
    public async Task Giving_a_shift_to_another_businesss_staff_member_is_a_404()
    {
        var a = await fixture.CreateBusinessAsync();
        var b = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(a.Manager);

        var response = await manager.PostJsonAsync("/api/shifts", new { start = At(Monday, 9), end = At(Monday, 17), staffMemberId = b.Staff.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Another_businesss_shift_cant_be_changed_or_deleted()
    {
        var a = await fixture.CreateBusinessAsync();
        var b = await fixture.CreateBusinessAsync();
        var theirs = await (await fixture.SignInAsync(b.Manager)).AddShiftAsync(b.Staff, At(Monday, 9), At(Monday, 17));
        var manager = await fixture.SignInAsync(a.Manager);

        var change = await manager.PutJsonAsync($"/api/shifts/{theirs.Id}", new { start = At(Monday, 10), end = At(Monday, 18) });
        var delete = await manager.DeleteAsync($"/api/shifts/{theirs.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, change.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task Another_businesss_staff_member_cant_be_changed()
    {
        var a = await fixture.CreateBusinessAsync();
        var b = await fixture.CreateBusinessAsync();
        var owner = await fixture.SignInAsync(a.Owner);

        var response = await owner.PatchJsonAsync($"/api/staff/{b.Staff.Id}", new { weeklyLimitHours = 10 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Another_businesss_availability_is_a_404()
    {
        var a = await fixture.CreateBusinessAsync();
        var b = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(a.Manager);

        var response = await manager.GetAsync($"/api/staff/{b.Staff.Id}/availability", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_same_week_shows_each_business_only_its_own_shifts()
    {
        var a = await fixture.CreateBusinessAsync();
        var b = await fixture.CreateBusinessAsync();
        var ours = await (await fixture.SignInAsync(a.Manager)).AddShiftAsync(a.Staff, At(Monday, 9), At(Monday, 17));
        await (await fixture.SignInAsync(b.Manager)).AddShiftAsync(b.Staff, At(Monday, 9), At(Monday, 17));

        var week = await (await fixture.SignInAsync(a.Manager)).GetOkAsync<WeekResponse>($"/api/weeks/{Monday:yyyy-MM-dd}");

        Assert.Equal([ours.Id], week.Shifts.Select(s => s.Id));
    }
}
