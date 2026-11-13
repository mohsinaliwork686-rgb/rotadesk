using System.Net;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Rotadesk.Api.Demo;
using Rotadesk.Domain;
using Rotadesk.Infrastructure.Persistence;

using static Rotadesk.Api.Tests.Http;

namespace Rotadesk.Api.Tests;

/// <summary>Which changes queue which emails, to whom, and that they arrive.</summary>
public class OutboxTests(ApiFixture fixture)
{
    private sealed record Week(TestBusiness Business, HttpClient Manager, ShiftResponse AmirasShift, ShiftResponse BensShift);

    private async Task<Week> RotaAsync(bool publish)
    {
        var business = await fixture.CreateBusinessAsync();
        var manager = await fixture.SignInAsync(business.Manager);
        var amirasShift = await manager.AddShiftAsync(business.Staff, At(Monday, 9), At(Monday, 17));
        var bensShift = await manager.AddShiftAsync(business.Colleague, At(Monday.AddDays(1), 9), At(Monday.AddDays(1), 17));
        if (publish)
        {
            Assert.Equal(HttpStatusCode.OK, (await manager.PostJsonAsync($"/api/weeks/{Monday:yyyy-MM-dd}/publish")).StatusCode);
        }

        return new Week(business, manager, amirasShift, bensShift);
    }

    /// <summary>The ids queued so far. The test clock is fixed, so emails can't be told apart by time.</summary>
    private async Task<HashSet<Guid>> SeenAsync(Guid businessId) => [.. (await fixture.OutboxAsync(businessId)).Select(m => m.Id)];

    /// <summary>Emails queued since <paramref name="seen"/>, which then includes them too.</summary>
    private async Task<List<(string To, string Subject)>> NewSinceAsync(Guid businessId, HashSet<Guid> seen)
    {
        var fresh = (await fixture.OutboxAsync(businessId)).Where(m => !seen.Contains(m.Id)).ToList();
        seen.UnionWith(fresh.Select(m => m.Id));
        return [.. fresh.Select(m => (m.To, m.Subject)).Order()];
    }

    [Fact]
    public async Task Publishing_emails_each_person_on_the_rota_their_own_shifts()
    {
        var week = await RotaAsync(publish: true);

        var emails = await fixture.OutboxAsync(week.Business.Id);

        Assert.Equal(new[] { week.Business.Staff.Email, week.Business.Colleague.Email }.Order(), emails.Select(e => e.To).Order());
        var amiras = emails.Single(e => e.To == week.Business.Staff.Email);
        Assert.Equal("Your shifts for the week of 16 Nov", amiras.Subject);
        Assert.Contains("- Mon 16 Nov, 09:00 to 17:00", amiras.Body);
        Assert.DoesNotContain("Tue 17 Nov", amiras.Body);
    }

    [Fact]
    public async Task Changes_to_a_week_that_isnt_published_email_nobody()
    {
        var week = await RotaAsync(publish: false);

        await week.Manager.PutJsonAsync($"/api/shifts/{week.AmirasShift.Id}", new { start = At(Monday, 10), end = At(Monday, 18), staffMemberId = week.Business.Colleague.Id });

        Assert.Empty(await fixture.OutboxAsync(week.Business.Id));
    }

    [Fact]
    public async Task Each_swap_step_emails_the_people_the_plan_names()
    {
        var week = await RotaAsync(publish: true);
        var amira = await fixture.SignInAsync(week.Business.Staff);
        var ben = await fixture.SignInAsync(week.Business.Colleague);
        var seen = await SeenAsync(week.Business.Id);

        var swap = await (await amira.PostJsonAsync("/api/swaps", new { offeredShiftId = week.AmirasShift.Id, colleagueId = week.Business.Colleague.Id, returnShiftId = week.BensShift.Id })).ReadAsync<SwapResponse>();
        var requested = await NewSinceAsync(week.Business.Id, seen);
        await ben.PostJsonAsync($"/api/swaps/{swap.Id}/accept");
        var accepted = await NewSinceAsync(week.Business.Id, seen);
        await week.Manager.PostJsonAsync($"/api/swaps/{swap.Id}/approve");
        var approved = await NewSinceAsync(week.Business.Id, seen);

        Assert.Equal([(week.Business.Colleague.Email, "Amira asked you to swap a shift")], requested);
        Assert.Equal(new[] { week.Business.Manager.Email, week.Business.Owner.Email }.Order(), accepted.Select(e => e.To).Order());
        Assert.All(accepted, e => Assert.Equal("A swap is waiting for your approval", e.Subject));
        Assert.Equal(new[] { week.Business.Staff.Email, week.Business.Colleague.Email }.Order(), approved.Select(e => e.To).Order());
        Assert.All(approved, e => Assert.Equal("Your swap was approved", e.Subject));
    }

    [Fact]
    public async Task A_rejected_swap_emails_both_people()
    {
        var week = await RotaAsync(publish: true);
        var swap = await (await (await fixture.SignInAsync(week.Business.Staff)).PostJsonAsync("/api/swaps", new { offeredShiftId = week.AmirasShift.Id, colleagueId = week.Business.Colleague.Id })).ReadAsync<SwapResponse>();
        await (await fixture.SignInAsync(week.Business.Colleague)).PostJsonAsync($"/api/swaps/{swap.Id}/accept");
        var seen = await SeenAsync(week.Business.Id);

        await week.Manager.PostJsonAsync($"/api/swaps/{swap.Id}/reject");

        var rejected = await NewSinceAsync(week.Business.Id, seen);
        Assert.Equal(new[] { week.Business.Staff.Email, week.Business.Colleague.Email }.Order(), rejected.Select(e => e.To).Order());
        Assert.All(rejected, e => Assert.Equal("Your swap was rejected", e.Subject));
    }

    [Fact]
    public async Task Changing_a_published_shift_emails_whoever_it_affects()
    {
        var week = await RotaAsync(publish: true);
        var seen = await SeenAsync(week.Business.Id);

        await week.Manager.PutJsonAsync($"/api/shifts/{week.AmirasShift.Id}", new { start = At(Monday, 10), end = At(Monday, 18), staffMemberId = week.Business.Staff.Id });
        var moved = await NewSinceAsync(week.Business.Id, seen);
        await week.Manager.PutJsonAsync($"/api/shifts/{week.AmirasShift.Id}", new { start = At(Monday, 10), end = At(Monday, 18), staffMemberId = week.Business.Colleague.Id });
        var reassigned = await NewSinceAsync(week.Business.Id, seen);
        await week.Manager.DeleteAsync($"/api/shifts/{week.BensShift.Id}", TestContext.Current.CancellationToken);
        var deleted = await NewSinceAsync(week.Business.Id, seen);

        Assert.Equal([(week.Business.Staff.Email, "One of your shifts changed")], moved);
        Assert.Equal(
            new[] { (week.Business.Staff.Email, "A shift was taken off your rota"), (week.Business.Colleague.Email, "You have a new shift") }.Order(),
            reassigned.Order());
        Assert.Equal([(week.Business.Colleague.Email, "A shift was taken off your rota")], deleted);
    }

    /// <summary>The email is saved with the change, so when the change fails, there's no email either.</summary>
    [Fact]
    public async Task An_approval_that_fails_queues_no_email()
    {
        var week = await RotaAsync(publish: true);
        var swap = await (await (await fixture.SignInAsync(week.Business.Staff)).PostJsonAsync("/api/swaps", new { offeredShiftId = week.AmirasShift.Id, colleagueId = week.Business.Colleague.Id })).ReadAsync<SwapResponse>();
        await (await fixture.SignInAsync(week.Business.Colleague)).PostJsonAsync($"/api/swaps/{swap.Id}/accept");
        var seen = await SeenAsync(week.Business.Id);
        await using var racing = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.ConfigureDbContext<RotadeskDbContext>(options => options.AddInterceptors(new SomeoneElseSavesTheSwapFirst()))));

        var response = await (await fixture.SignInAsync(week.Business.Manager, racing)).PostJsonAsync($"/api/swaps/{swap.Id}/approve");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await NewSinceAsync(week.Business.Id, seen));
    }

    /// <summary>With the worker running for real, a published rota lands in each person's own demo inbox.</summary>
    [Fact]
    public async Task The_worker_delivers_to_each_persons_demo_inbox()
    {
        await using var live = fixture.Factory.WithWebHostBuilder(b => b
            .UseSetting("Outbox:Enabled", "true")
            .UseSetting("Outbox:PollSeconds", "0.2"));
        var week = await RotaAsync(publish: true);
        var amira = await fixture.SignInAsync(week.Business.Staff, live);
        var olga = await fixture.SignInAsync(week.Business.Owner, live);

        var inbox = new List<InboxEmail>();
        for (var tries = 0; tries < 100 && inbox.Count == 0; tries++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            inbox = await amira.GetOkAsync<List<InboxEmail>>("/api/inbox");
        }

        var email = Assert.Single(inbox);
        Assert.Equal("Your shifts for the week of 16 Nov", email.Subject);
        Assert.Equal((await fixture.OutboxAsync(week.Business.Id)).Single(m => m.To == week.Business.Staff.Email).Id, email.MessageId);
        Assert.Empty(await olga.GetOkAsync<List<InboxEmail>>("/api/inbox"));
    }

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
