using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Infrastructure.Outbox;

/// <summary>
/// Sends the emails that are due. This is the only code that works across businesses, through the system context,
/// and a test checks that nothing else does.
/// </summary>
public sealed partial class OutboxProcessor(
    DbContextOptions<RotadeskDbContext> options,
    IEmailSender sender,
    TimeProvider clock,
    ILogger<OutboxProcessor> log)
{
    public const int MaxAttempts = 5;

    /// <summary>How long a claimed message is left alone. If the worker stops mid-send, it's sent again after this.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    /// <summary>30 seconds after the first failure, then 1, 2 and 4 minutes.</summary>
    public static TimeSpan Backoff(int attempts) => TimeSpan.FromSeconds(30 * Math.Pow(2, attempts - 1));

    /// <summary>Sends up to <paramref name="batchSize"/> due messages, oldest first, and returns how many were sent.</summary>
    public async Task<int> ProcessDueAsync(int batchSize, CancellationToken cancellationToken)
    {
        await using var db = new RotadeskDbContext(options, SystemTenant.Instance);
        var now = clock.GetUtcNow();
        var due = await db.OutboxMessages
            .Where(m => m.SentAt == null && m.FailedAt == null && m.NextAttemptAt <= now)
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var message in due)
        {
            message.Claim(clock.GetUtcNow(), Lease);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another worker claimed it between our read and our claim. It's theirs.
                db.Entry(message).State = EntityState.Detached;
                continue;
            }

            try
            {
                await sender.SendAsync(new OutboundEmail(message.Id, message.TenantId, message.To, message.Subject, message.Body), cancellationToken);
                message.MarkSent(clock.GetUtcNow());
                sent++;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                message.MarkFailed(clock.GetUtcNow(), error.Message, MaxAttempts, Backoff);
                LogFailure(log, message.Id, message.Attempts, error.Message);
            }

            // Not cancellable: once an email is out, record it, or a shutdown here would send it again later.
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return sent;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending email {MessageId} failed on attempt {Attempts}: {Error}")]
    private static partial void LogFailure(ILogger logger, Guid messageId, int attempts, string error);
}
