using System.Net;

using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Rotadesk.Domain;
using Rotadesk.Infrastructure.Persistence;

using static Rotadesk.Api.Tests.Http;

namespace Rotadesk.Api.Tests;

public class SwapTests(ApiFixture fixture)
{
    /// <summary>Amira works Monday, Ben works Tuesday, and the week is published, so staff can see and swap it.</summary>
    private sealed record Week(TestBusiness Business, HttpClient Manager, HttpClient Amira, HttpClient Ben, ShiftResponse AmirasShift, ShiftResponse BensShift);

    private async Task<Week> PublishedWeekAsync()
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);
        var amirasShift = await manager.AddShiftAsync(business.Staff, At(Monday, 9), At(Monday, 17));
        var bensShift = await manager.AddShiftAsync(business.Colleague, At(Monday.AddDays(1), 9), At(Monday.AddDays(1), 17));
        Assert.Equal(HttpStatusCode.OK, (await manager.PostJsonAsync($"/api/weeks/{Monday:yyyy-MM-dd}/publish")).StatusCode);
        return new Week(business, manager, await fixture.SignInAsync(business.Staff), await fixture.SignInAsync(business.Colleague), amirasShift, bensShift);
    }

    private static async Task<SwapResponse> AskAsync(Week week, bool withReturn = true)
    {
        var response = await week.Amira.PostJsonAsync("/api/swaps", new
        {
            offeredShiftId = week.AmirasShift.Id,
            colleagueId = week.Business.Colleague.Id,
            returnShiftId = withReturn ? week.BensShift.Id : (Guid?)null,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<SwapResponse>();
    }

    private static async Task<SwapResponse> AskAndAcceptAsync(Week week, bool withReturn = true)
    {
        var swap = await AskAsync(week, withReturn);
        Assert.Equal(HttpStatusCode.OK, (await week.Ben.PostJsonAsync($"/api/swaps/{swap.Id}/accept")).StatusCode);
        return swap;
    }

    private static async Task<Dictionary<Guid, Guid?>> WhoWorksWhatAsync(HttpClient manager) =>
        (await manager.GetOkAsync<WeekResponse>($"/api/weeks/{Monday:yyyy-MM-dd}")).Shifts.ToDictionary(s => s.Id, s => s.StaffMemberId);

    [Fact]
    public async Task Once_the_colleague_accepts_and_a_manager_approves_both_shifts_move()
    {
        var week = await PublishedWeekAsync();
        var swap = await AskAndAcceptAsync(week);

        var approved = await week.Manager.PostJsonAsync($"/api/swaps/{swap.Id}/approve");

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var decided = await approved.ReadAsync<SwapResponse>();
        Assert.Equal((SwapStatus.Approved, "Max"), (decided.Status, decided.DecidedBy));
        var rota = await WhoWorksWhatAsync(week.Manager);
        Assert.Equal(week.Business.Colleague.Id, rota[week.AmirasShift.Id]);
        Assert.Equal(week.Business.Staff.Id, rota[week.BensShift.Id]);
    }

    [Fact]
    public async Task Without_a_shift_in_return_only_the_offered_shift_moves()
    {
        var week = await PublishedWeekAsync();
        var swap = await AskAndAcceptAsync(week, withReturn: false);

        await week.Manager.PostJsonAsync($"/api/swaps/{swap.Id}/approve");

        var rota = await WhoWorksWhatAsync(week.Manager);
        Assert.Equal(week.Business.Colleague.Id, rota[week.AmirasShift.Id]);
        Assert.Equal(week.Business.Colleague.Id, rota[week.BensShift.Id]);
    }

    [Fact]
    public async Task Only_the_colleague_asked_can_accept()
    {
        var week = await PublishedWeekAsync();
        var swap = await AskAsync(week);

        var response = await week.Amira.PostJsonAsync($"/api/swaps/{swap.Id}/accept");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("swap.wrong-person", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task The_colleague_can_decline()
    {
        var week = await PublishedWeekAsync();
        var swap = await AskAsync(week);

        var response = await week.Ben.PostJsonAsync($"/api/swaps/{swap.Id}/decline");

        Assert.Equal(SwapStatus.Declined, (await response.ReadAsync<SwapResponse>()).Status);
    }

    [Fact]
    public async Task Only_the_person_who_asked_can_cancel()
    {
        var week = await PublishedWeekAsync();
        var swap = await AskAndAcceptAsync(week);

        var byBen = await week.Ben.PostJsonAsync($"/api/swaps/{swap.Id}/cancel");
        var byAmira = await week.Amira.PostJsonAsync($"/api/swaps/{swap.Id}/cancel");

        Assert.Equal(HttpStatusCode.Forbidden, byBen.StatusCode);
        Assert.Equal(SwapStatus.Cancelled, (await byAmira.ReadAsync<SwapResponse>()).Status);
    }

    [Fact]
    public async Task A_manager_cant_approve_before_the_colleague_accepts()
    {
        var week = await PublishedWeekAsync();
        var swap = await AskAsync(week);

        var response = await week.Manager.PostJsonAsync($"/api/swaps/{swap.Id}/approve");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("swap.illegal-transition", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task A_manager_cant_decide_a_swap_they_are_part_of_but_another_manager_can()
    {
        var week = await PublishedWeekAsync();
        var maxsShift = await week.Manager.AddShiftAsync(week.Business.Manager, At(Monday.AddDays(2), 9), At(Monday.AddDays(2), 17));
        var ask = await week.Manager.PostJsonAsync("/api/swaps", new { offeredShiftId = maxsShift.Id, colleagueId = week.Business.Staff.Id });
        var swap = await ask.ReadAsync<SwapResponse>();
        await week.Amira.PostJsonAsync($"/api/swaps/{swap.Id}/accept");

        var byMax = await week.Manager.PostJsonAsync($"/api/swaps/{swap.Id}/approve");
        var byOlga = await (await fixture.SignInAsync(week.Business.Owner)).PostJsonAsync($"/api/swaps/{swap.Id}/approve");

        Assert.Equal(HttpStatusCode.Forbidden, byMax.StatusCode);
        Assert.Equal("swap.own-swap", await byMax.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.OK, byOlga.StatusCode);
    }

    [Fact]
    public async Task Rejecting_leaves_the_shifts_where_they_were()
    {
        var week = await PublishedWeekAsync();
        var swap = await AskAndAcceptAsync(week);

        var response = await week.Manager.PostJsonAsync($"/api/swaps/{swap.Id}/reject");

        Assert.Equal(SwapStatus.Rejected, (await response.ReadAsync<SwapResponse>()).Status);
        Assert.Equal(week.Business.Staff.Id, (await WhoWorksWhatAsync(week.Manager))[week.AmirasShift.Id]);
    }

    [Fact]
    public async Task Approval_checks_the_rules_again_as_the_rota_is_now()
    {
        var week = await PublishedWeekAsync();
        var swap = await AskAndAcceptAsync(week, withReturn: false);
        await week.Manager.AddShiftAsync(week.Business.Colleague, At(Monday, 12), At(Monday, 20));

        var response = await week.Manager.PostJsonAsync($"/api/swaps/{swap.Id}/approve");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("shift.overlap", await response.ProblemCodeAsync());
        Assert.Equal(SwapStatus.Accepted, (await week.Manager.GetOkAsync<SwapResponse>($"/api/swaps/{swap.Id}")).Status);
    }

    [Fact]
    public async Task A_swap_the_colleague_couldnt_take_is_refused_when_asked()
    {
        var week = await PublishedWeekAsync();
        await week.Manager.AddShiftAsync(week.Business.Colleague, At(Monday, 12), At(Monday, 20));

        var response = await week.Amira.PostJsonAsync("/api/swaps", new { offeredShiftId = week.AmirasShift.Id, colleagueId = week.Business.Colleague.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("shift.overlap", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Only_shifts_on_a_published_rota_can_be_swapped()
    {
        var business = await fixture.CreateBusinessAsync();
        var shift = await (await fixture.SignInAsync(business.Manager)).AddShiftAsync(business.Staff, At(Monday, 9), At(Monday, 17));

        var response = await (await fixture.SignInAsync(business.Staff)).PostJsonAsync("/api/swaps", new { offeredShiftId = shift.Id, colleagueId = business.Colleague.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("swap.not-published", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task A_shift_already_waiting_in_a_swap_cant_be_offered_again()
    {
        var week = await PublishedWeekAsync();
        await AskAsync(week);

        var again = await week.Amira.PostJsonAsync("/api/swaps", new { offeredShiftId = week.AmirasShift.Id, colleagueId = week.Business.Colleague.Id });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("swap.already-open", await again.ProblemCodeAsync());
    }

    [Fact]
    public async Task A_shift_that_has_already_started_cant_be_swapped()
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);
        var lastMonday = Monday.AddDays(-7);
        var shift = await manager.AddShiftAsync(business.Staff, At(lastMonday, 9), At(lastMonday, 17));
        await manager.PostJsonAsync($"/api/weeks/{lastMonday:yyyy-MM-dd}/publish");

        var response = await (await fixture.SignInAsync(business.Staff)).PostJsonAsync("/api/swaps", new { offeredShiftId = shift.Id, colleagueId = business.Colleague.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("swap.shift-started", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Staff_see_only_their_own_swaps_and_managers_see_them_all()
    {
        var week = await PublishedWeekAsync();
        var swap = await AskAsync(week);
        var owner = await fixture.SignInAsync(week.Business.Owner);
        var email = $"cara.{Guid.NewGuid():N}@example.test";
        var cara = (await (await owner.PostJsonAsync("/api/staff", new { name = "Cara", email, role = "Staff" })).ReadAsync<CreatedStaffResponse>());
        var caraClient = await fixture.SignInAsync(new TestPerson(cara.Staff.Id, "Cara", email, cara.TemporaryPassword, StaffRole.Staff));

        Assert.Empty(await caraClient.GetOkAsync<List<SwapResponse>>("/api/swaps"));
        Assert.Equal(HttpStatusCode.NotFound, (await caraClient.GetAsync($"/api/swaps/{swap.Id}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await caraClient.PostJsonAsync($"/api/swaps/{swap.Id}/accept")).StatusCode);
        Assert.Equal([swap.Id], (await week.Ben.GetOkAsync<List<SwapResponse>>("/api/swaps")).Select(s => s.Id));
        Assert.Equal([swap.Id], (await owner.GetOkAsync<List<SwapResponse>>("/api/swaps?status=Requested")).Select(s => s.Id));
        Assert.Empty(await owner.GetOkAsync<List<SwapResponse>>("/api/swaps?status=Approved"));
    }

    /// <summary>
    /// Another manager's decision lands between this approval's read and its write. The swap's xmin has moved, so
    /// the save matches no row: the answer is a 409 that says so, and nothing moves.
    /// </summary>
    [Fact]
    public async Task An_approval_based_on_a_stale_read_is_a_409_and_moves_nothing()
    {
        var week = await PublishedWeekAsync();
        var swap = await AskAndAcceptAsync(week);
        await using var racing = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.ConfigureDbContext<RotadeskDbContext>(options => options.AddInterceptors(new SomeoneElseSavesTheSwapFirst()))));
        var manager = await fixture.SignInAsync(week.Business.Manager, racing);

        var response = await manager.PostJsonAsync($"/api/swaps/{swap.Id}/approve");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("concurrency", await response.ProblemCodeAsync());
        Assert.Equal(SwapStatus.Accepted, (await week.Manager.GetOkAsync<SwapResponse>($"/api/swaps/{swap.Id}")).Status);
        Assert.Equal(week.Business.Staff.Id, (await WhoWorksWhatAsync(week.Manager))[week.AmirasShift.Id]);
    }

    [Fact]
    public async Task Of_many_simultaneous_approvals_exactly_one_wins_and_the_rest_are_409s()
    {
        var week = await PublishedWeekAsync();
        var swap = await AskAndAcceptAsync(week);
        var owner = await fixture.SignInAsync(week.Business.Owner);
        HttpClient[] deciders = [week.Manager, owner];

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => deciders[i % 2].PostJsonAsync($"/api/swaps/{swap.Id}/approve")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        var rota = await WhoWorksWhatAsync(week.Manager);
        Assert.Equal(week.Business.Colleague.Id, rota[week.AmirasShift.Id]);
        Assert.Equal(week.Business.Staff.Id, rota[week.BensShift.Id]);
    }

    /// <summary>Just before an approval is saved, changes the swap's row on another connection, as a second manager would.</summary>
    private sealed class SomeoneElseSavesTheSwapFirst : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is RotadeskDbContext db)
            {
                foreach (var entry in db.ChangeTracker.Entries<SwapRequest>().Where(e => e.State == EntityState.Modified && e.Entity.Status == SwapStatus.Approved))
                {
                    await using var other = new NpgsqlConnection(db.Database.GetConnectionString());
                    await other.OpenAsync(cancellationToken);
                    await using var update = new NpgsqlCommand("UPDATE swap_requests SET created_at = created_at WHERE id = @id", other);
                    update.Parameters.AddWithValue("id", entry.Entity.Id);
                    await update.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            return result;
        }
    }
}
