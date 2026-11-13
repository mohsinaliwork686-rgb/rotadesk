using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Infrastructure.Outbox;

public sealed record OutboundEmail(Guid MessageId, Guid TenantId, string To, string Subject, string Body);

/// <summary>Sends one email. A real provider would be one more class.</summary>
public interface IEmailSender
{
    Task SendAsync(OutboundEmail email, CancellationToken cancellationToken);
}

/// <summary>The demo's sender: it delivers into the demo inbox, through the email's own business.</summary>
public sealed class DemoInboxSender(DbContextOptions<RotadeskDbContext> options, TimeProvider clock) : IEmailSender
{
    public async Task SendAsync(OutboundEmail email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        await using var db = new RotadeskDbContext(options, new FixedTenant(email.TenantId));
        db.Add(new DemoInboxEntry(email.TenantId, email.MessageId, email.To, email.Subject, email.Body, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Without the demo, and without a real provider, emails are written to the log.</summary>
public sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> log) : IEmailSender
{
    public Task SendAsync(OutboundEmail email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        Log(log, email.To, email.Subject, email.MessageId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email to {To}: {Subject} ({MessageId})")]
    private static partial void Log(ILogger logger, string to, string subject, Guid messageId);
}
