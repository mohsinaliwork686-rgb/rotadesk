using Microsoft.EntityFrameworkCore;

using Rotadesk.Api.Auth;
using Rotadesk.Infrastructure.Persistence;

namespace Rotadesk.Api.Demo;

public sealed record InboxEmail(Guid Id, Guid MessageId, string Subject, string Body, DateTimeOffset DeliveredAt);

/// <summary>The demo inbox: the emails "delivered" to the signed-in person. Mapped only with the demo.</summary>
internal static class InboxEndpoints
{
    public static void MapInboxEndpoints(this IEndpointRouteBuilder api) =>
        api.MapGet("/inbox", async (CurrentMember current, RotadeskDbContext db, CancellationToken cancellationToken) =>
        {
            var me = await current.GetAsync(cancellationToken);
            var emails = await db.DemoInbox.AsNoTracking()
                .Where(e => e.To == me.Email)
                .OrderByDescending(e => e.DeliveredAt)
                .Select(e => new InboxEmail(e.Id, e.MessageId, e.Subject, e.Body, e.DeliveredAt))
                .ToListAsync(cancellationToken);
            return Results.Ok(emails);
        }).RequireAuthorization(Policies.Member);
}
