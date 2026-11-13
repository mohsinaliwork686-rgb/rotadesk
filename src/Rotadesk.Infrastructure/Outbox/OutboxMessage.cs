using Rotadesk.Domain;

namespace Rotadesk.Infrastructure.Outbox;

/// <summary>
/// An email waiting to be sent. It is saved in the same transaction as the change it's about, so the change can't
/// happen without its email, and the email can't be sent for a change that didn't happen.
/// </summary>
public sealed class OutboxMessage : ITenantOwned
{
    private OutboxMessage()
    {
    }

    public OutboxMessage(Guid tenantId, string to, string subject, string body, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        To = to;
        Subject = subject;
        Body = body;
        CreatedAt = now;
        NextAttemptAt = now;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string To { get; private set; } = "";

    public string Subject { get; private set; } = "";

    public string Body { get; private set; } = "";

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When it's next due: now for a new message, later after a failure, and a lease while being sent.</summary>
    public DateTimeOffset NextAttemptAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Set when it gave up after the last attempt.</summary>
    public DateTimeOffset? FailedAt { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>
    /// Takes the message for one attempt. The attempt is counted now, before sending, so one that crashes the worker
    /// still counts, and the lease keeps other workers off it until it runs out.
    /// </summary>
    public void Claim(DateTimeOffset now, TimeSpan lease)
    {
        Attempts++;
        NextAttemptAt = now + lease;
    }

    public void MarkSent(DateTimeOffset now)
    {
        SentAt = now;
        LastError = null;
    }

    public void MarkFailed(DateTimeOffset now, string error, int maxAttempts, Func<int, TimeSpan> backoff)
    {
        ArgumentNullException.ThrowIfNull(backoff);
        LastError = error.Length > 2000 ? error[..2000] : error;
        if (Attempts >= maxAttempts)
        {
            FailedAt = now;
        }
        else
        {
            NextAttemptAt = now + backoff(Attempts);
        }
    }
}
