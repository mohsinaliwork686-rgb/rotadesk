using Rotadesk.Domain;

namespace Rotadesk.Infrastructure.Outbox;

/// <summary>
/// An email the demo "delivered". The demo sends no real mail: its sender writes here, and the inbox page reads it.
/// A message sent twice after a crash appears twice, with the same message id.
/// </summary>
public sealed class DemoInboxEntry : ITenantOwned
{
    private DemoInboxEntry()
    {
    }

    public DemoInboxEntry(Guid tenantId, Guid messageId, string to, string subject, string body, DateTimeOffset deliveredAt)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        MessageId = messageId;
        To = to;
        Subject = subject;
        Body = body;
        DeliveredAt = deliveredAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid MessageId { get; private set; }

    public string To { get; private set; } = "";

    public string Subject { get; private set; } = "";

    public string Body { get; private set; } = "";

    public DateTimeOffset DeliveredAt { get; private set; }
}
