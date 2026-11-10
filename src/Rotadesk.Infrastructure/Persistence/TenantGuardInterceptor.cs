using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Rotadesk.Domain;

namespace Rotadesk.Infrastructure.Persistence;

/// <summary>
/// Query filters only cover reads. This refuses any save that adds, changes or deletes another business's row,
/// or moves a row to another business.
/// </summary>
public sealed class TenantGuardInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Check(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Check(DbContext? context)
    {
        if (context is not RotadeskDbContext db || db.IsSystem)
        {
            return;
        }

        foreach (var entry in db.ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            var tenantId = entry.Property(e => e.TenantId);
            if (db.CurrentTenantId is not { } current || tenantId.CurrentValue != current)
            {
                throw new TenantViolationException($"A {entry.Metadata.ClrType.Name} of another business can't be {Describe(entry.State)} here.");
            }

            if (entry.State == EntityState.Modified && tenantId.OriginalValue != tenantId.CurrentValue)
            {
                throw new TenantViolationException($"A {entry.Metadata.ClrType.Name} can't be moved to another business.");
            }
        }
    }

    private static string Describe(EntityState state) => state switch
    {
        EntityState.Added => "added",
        EntityState.Deleted => "deleted",
        _ => "changed",
    };
}
