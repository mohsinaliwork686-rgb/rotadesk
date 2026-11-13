using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Rotadesk.Domain;
using Rotadesk.Infrastructure.Outbox;

namespace Rotadesk.Infrastructure.Tests;

/// <summary>
/// The worker's logic against real PostgreSQL, with a clock the test moves and a sender the test controls. The tests
/// in this class run one after another, and each starts by setting aside anything earlier ones left unsent.
/// </summary>
public class OutboxProcessorTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_due_message_is_sent_once_and_marked_sent()
    {
        var message = await QueueAsync();
        var sender = new Sender();

        Assert.Equal(1, await Processor(sender, Start).ProcessDueAsync(10, Token));
        Assert.Equal(0, await Processor(sender, Start.AddHours(1)).ProcessDueAsync(10, Token));

        Assert.Equal([message.Id], sender.Delivered);
        var saved = await ReloadAsync(message);
        Assert.Equal((Start, 1, (string?)null), (saved.SentAt, saved.Attempts, saved.LastError));
    }

    [Fact]
    public async Task A_failed_send_is_tried_again_only_after_the_backoff()
    {
        var message = await QueueAsync();
        var sender = new Sender { FailuresLeft = 1 };

        await Processor(sender, Start).ProcessDueAsync(10, Token);
        var failed = await ReloadAsync(message);
        await Processor(sender, Start.AddSeconds(29)).ProcessDueAsync(10, Token);
        await Processor(sender, Start.AddSeconds(30)).ProcessDueAsync(10, Token);

        Assert.Equal((1, "the mail server said no", Start.AddSeconds(30)), (failed.Attempts, failed.LastError, failed.NextAttemptAt));
        Assert.Equal([message.Id], sender.Delivered);
        var sent = await ReloadAsync(message);
        Assert.Equal((Start.AddSeconds(30), 2, (string?)null), (sent.SentAt, sent.Attempts, sent.LastError));
    }

    [Fact]
    public async Task It_gives_up_after_five_attempts_and_keeps_the_last_error()
    {
        var message = await QueueAsync();
        var sender = new Sender { FailuresLeft = int.MaxValue };

        // Due at 0s, then after 30s, 1m, 2m and 4m.
        foreach (var at in new[] { 0, 30, 90, 210, 450 })
        {
            await Processor(sender, Start.AddSeconds(at)).ProcessDueAsync(10, Token);
        }

        await Processor(sender, Start.AddDays(1)).ProcessDueAsync(10, Token);

        var gaveUp = await ReloadAsync(message);
        Assert.Equal(5, sender.Tries);
        Assert.Equal((5, Start.AddSeconds(450), "the mail server said no"), (gaveUp.Attempts, gaveUp.FailedAt, gaveUp.LastError));
        Assert.Null(gaveUp.SentAt);
    }

    [Fact]
    public async Task A_worker_stopped_mid_send_loses_nothing_and_sends_it_after_the_lease()
    {
        var message = await QueueAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor(new Sender { StopMidSend = true }, Start).ProcessDueAsync(10, Token));
        var stopped = await ReloadAsync(message);
        var sender = new Sender();
        await Processor(sender, Start + OutboxProcessor.Lease - TimeSpan.FromSeconds(1)).ProcessDueAsync(10, Token);
        await Processor(sender, Start + OutboxProcessor.Lease).ProcessDueAsync(10, Token);

        Assert.Equal((1, (DateTimeOffset?)null, (DateTimeOffset?)null), (stopped.Attempts, stopped.SentAt, stopped.FailedAt));
        Assert.Equal([message.Id], sender.Delivered);
        Assert.NotNull((await ReloadAsync(message)).SentAt);
    }

    [Fact]
    public async Task Two_workers_that_read_the_same_message_cant_both_claim_it()
    {
        var message = await QueueAsync();
        await using var first = fixture.NewContext(SystemTenant.Instance);
        await using var second = fixture.NewContext(SystemTenant.Instance);
        var mine = await first.OutboxMessages.SingleAsync(m => m.Id == message.Id, Token);
        var theirs = await second.OutboxMessages.SingleAsync(m => m.Id == message.Id, Token);

        mine.Claim(Start, OutboxProcessor.Lease);
        await first.SaveChangesAsync(Token);
        theirs.Claim(Start, OutboxProcessor.Lease);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));
    }

    private OutboxProcessor Processor(IEmailSender sender, DateTimeOffset now) =>
        new(fixture.Options, sender, new Clock(now), NullLogger<OutboxProcessor>.Instance);

    /// <summary>Sets aside what earlier tests left, then queues one message for a new business.</summary>
    private async Task<OutboxMessage> QueueAsync()
    {
        await using var db = fixture.NewContext(SystemTenant.Instance);
        await db.Database.ExecuteSqlRawAsync("UPDATE outbox_messages SET failed_at = now() WHERE sent_at IS NULL AND failed_at IS NULL", Token);

        var business = new Business($"Outbox {Guid.NewGuid():N}", "Europe/London");
        var message = new OutboxMessage(business.Id, "amira@example.test", "Your shifts", "Monday 09:00 to 17:00", Start);
        await using var write = fixture.For(business.Id);
        write.Add(business);
        write.Add(message);
        await write.SaveChangesAsync(Token);
        return message;
    }

    private async Task<OutboxMessage> ReloadAsync(OutboxMessage message)
    {
        await using var db = fixture.NewContext(SystemTenant.Instance);
        return await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id, Token);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Sender : IEmailSender
    {
        public List<Guid> Delivered { get; } = [];

        public int Tries { get; private set; }

        public int FailuresLeft { get; set; }

        /// <summary>Stops the way a shutting-down worker does, in the middle of sending.</summary>
        public bool StopMidSend { get; init; }

        public Task SendAsync(OutboundEmail email, CancellationToken cancellationToken)
        {
            Tries++;
            if (StopMidSend)
            {
                throw new OperationCanceledException("the worker is stopping");
            }

            if (FailuresLeft > 0)
            {
                FailuresLeft--;
                throw new InvalidOperationException("the mail server said no");
            }

            Delivered.Add(email.MessageId);
            return Task.CompletedTask;
        }
    }
}
