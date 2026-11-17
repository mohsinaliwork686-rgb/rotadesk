using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Rotadesk.Domain;
using Rotadesk.Infrastructure;
using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Api.Tests;

/// <summary>
/// The live demo's rota: filled around today each time the app starts, within the rules, and left alone once a
/// visitor changes it. The fixture's clock is Tuesday 10 November, so its week starts on Monday 9 November. Tests
/// that change the demo do it in a later week, through an app started with a later clock.
/// </summary>
public class DemoRotaTests(ApiFixture fixture)
{
    private static readonly DateOnly ThisWeek = new(2026, 11, 9);

    [Fact]
    public async Task Each_business_has_this_week_published_and_next_week_as_a_draft()
    {
        var marcus = await SignInAsync("marcus@northside.example");
        var bilal = await SignInAsync("bilal@harbourbooks.example");

        var cafe = await marcus.GetOkAsync<WeekResponse>(WeekUrl(ThisWeek));
        var cafeNext = await marcus.GetOkAsync<WeekResponse>(WeekUrl(ThisWeek.AddDays(7)));
        var books = await bilal.GetOkAsync<WeekResponse>(WeekUrl(ThisWeek));

        Assert.Equal((true, 13), (cafe.Published, cafe.Shifts.Count));
        Assert.Equal((false, 13), (cafeNext.Published, cafeNext.Shifts.Count));
        Assert.Equal((true, 12), (books.Published, books.Shifts.Count));
    }

    [Fact]
    public async Task Shifts_are_on_the_business_clock_and_warn_only_where_meant_to()
    {
        var cafe = await (await SignInAsync("marcus@northside.example")).GetOkAsync<WeekResponse>(WeekUrl(ThisWeek));
        var books = await (await SignInAsync("bilal@harbourbooks.example")).GetOkAsync<WeekResponse>(WeekUrl(ThisWeek));

        // London is on GMT in November; Karachi is five hours ahead of UTC.
        Assert.Contains(cafe.Shifts, s => s.StaffName == "Amira" && s.Start == new DateTimeOffset(2026, 11, 9, 7, 0, 0, TimeSpan.Zero));
        Assert.Contains(books.Shifts, s => s.StaffName == "Hira" && s.Start == new DateTimeOffset(2026, 11, 9, 5, 0, 0, TimeSpan.Zero));

        // Amira isn't free before 08:00 on Fridays, and that's the one warning in the week.
        var friday = Assert.Single(cafe.Shifts, s => s.StaffName == "Amira" && s.Start.Date == new DateTime(2026, 11, 13));
        Assert.Equal(["Outside Amira's availability on Friday."], friday.Warnings);
        Assert.All(cafe.Shifts.Where(s => s.Id != friday.Id), s => Assert.Empty(s.Warnings));
        Assert.Single(cafe.Shifts, s => s.StaffMemberId is null);
    }

    [Fact]
    public async Task Each_business_has_a_swap_waiting_on_someone()
    {
        var cafeSwaps = await (await SignInAsync("marcus@northside.example")).GetOkAsync<List<SwapResponse>>("/api/swaps");
        var bookSwaps = await (await SignInAsync("hira@harbourbooks.example")).GetOkAsync<List<SwapResponse>>("/api/swaps");

        var cafe = Assert.Single(cafeSwaps, s => InWeek(ThisWeek, s.Offered.Start));
        Assert.Equal((SwapStatus.Accepted, "Ben", "Amira", false), (cafe.Status, cafe.Requester.Name, cafe.Colleague.Name, cafe.Return is not null));
        var books = Assert.Single(bookSwaps, s => InWeek(ThisWeek, s.Offered.Start));
        Assert.Equal((SwapStatus.Requested, "Usman", "Hira", true), (books.Status, books.Requester.Name, books.Colleague.Name, books.Return is not null));
    }

    [Fact]
    public async Task The_seeded_shifts_break_no_scheduling_rule()
    {
        foreach (var email in new[] { "marcus@northside.example", "bilal@harbourbooks.example" })
        {
            var me = await (await SignInAsync(email)).GetOkAsync<MeResponse>("/api/auth/me");
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<RotadeskDbContext>>();
            await using var db = new RotadeskDbContext(options, new FixedTenant(me.Business.Id));
            var zone = TimeZoneInfo.FindSystemTimeZoneById(me.Business.TimeZone);
            var staff = await db.StaffMembers.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
            var shifts = await db.Shifts.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
            var availability = await db.Availability.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);

            Assert.NotEmpty(availability);
            foreach (var shift in shifts.Where(s => s.StaffMemberId is not null))
            {
                var member = staff.Single(s => s.Id == shift.StaffMemberId);
                var check = SchedulingRules.Check(member, shift.Id, shift.Start, shift.End, shifts, availability, zone);
                Assert.True(check.IsAllowed, $"{member.Name} at {shift.Start}: {string.Join(" ", check.Violations.Select(v => v.Message))}");
            }
        }
    }

    [Fact]
    public async Task The_seeded_swaps_can_be_approved()
    {
        var monday = new DateOnly(2027, 1, 11);
        await using var factory = new RotadeskFactory(fixture.ConnectionString, demo: true, now: new DateTimeOffset(2027, 1, 12, 9, 0, 0, TimeSpan.Zero));
        var marcus = await SignInAsync("marcus@northside.example", factory);
        var hira = await SignInAsync("hira@harbourbooks.example", factory);
        var bilal = await SignInAsync("bilal@harbourbooks.example", factory);

        // Approval checks the rules again for both people, so a seeded swap that broke one would be refused here.
        var cafe = Assert.Single(await marcus.GetOkAsync<List<SwapResponse>>("/api/swaps"), s => InWeek(monday, s.Offered.Start));
        await AssertOkAsync(await marcus.PostJsonAsync($"/api/swaps/{cafe.Id}/approve"));

        var books = Assert.Single(await hira.GetOkAsync<List<SwapResponse>>("/api/swaps"), s => InWeek(monday, s.Offered.Start));
        await AssertOkAsync(await hira.PostJsonAsync($"/api/swaps/{books.Id}/accept"));
        await AssertOkAsync(await bilal.PostJsonAsync($"/api/swaps/{books.Id}/approve"));
    }

    [Fact]
    public async Task A_start_in_a_new_week_fills_it_but_keeps_what_visitors_changed()
    {
        var now = new DateTimeOffset(2027, 2, 9, 9, 0, 0, TimeSpan.Zero);
        var week = WeekUrl(new DateOnly(2027, 2, 8));
        await using (var first = new RotadeskFactory(fixture.ConnectionString, demo: true, now: now))
        {
            var marcus = await SignInAsync("marcus@northside.example", first);
            var seeded = await marcus.GetOkAsync<WeekResponse>(week);
            Assert.Equal((true, 13), (seeded.Published, seeded.Shifts.Count));

            var open = seeded.Shifts.Single(s => s.StaffMemberId is null);
            var deleted = await marcus.DeleteAsync($"/api/shifts/{open.Id}", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        await using var second = new RotadeskFactory(fixture.ConnectionString, demo: true, now: now);
        var again = await (await SignInAsync("marcus@northside.example", second)).GetOkAsync<WeekResponse>(week);

        Assert.Equal(12, again.Shifts.Count);
    }

    private async Task<HttpClient> SignInAsync(string email, RotadeskFactory? factory = null)
    {
        var client = fixture.NewClient(factory);
        var response = await client.PostJsonAsync("/api/demo/sign-in", new { email });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return client;
    }

    private static async Task AssertOkAsync(HttpResponseMessage response) =>
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    private static string WeekUrl(DateOnly monday) => $"/api/weeks/{monday:yyyy-MM-dd}";

    private static bool InWeek(DateOnly monday, DateTimeOffset instant) =>
        instant >= new DateTimeOffset(monday.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(-6)
        && instant < new DateTimeOffset(monday.AddDays(7).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
