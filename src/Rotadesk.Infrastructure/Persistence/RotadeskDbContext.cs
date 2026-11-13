using System.Linq.Expressions;
using System.Reflection;

using Microsoft.EntityFrameworkCore;

using Rotadesk.Domain;
using Rotadesk.Infrastructure.Outbox;

namespace Rotadesk.Infrastructure.Persistence;

public sealed class RotadeskDbContext(DbContextOptions<RotadeskDbContext> options, ITenantContext tenant) : DbContext(options)
{
    public DbSet<Business> Businesses => Set<Business>();

    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();

    public DbSet<RotaWeek> RotaWeeks => Set<RotaWeek>();

    public DbSet<Shift> Shifts => Set<Shift>();

    public DbSet<Availability> Availability => Set<Availability>();

    public DbSet<SwapRequest> SwapRequests => Set<SwapRequest>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<DemoInboxEntry> DemoInbox => Set<DemoInboxEntry>();

    /// <summary>Read by the query filters on every query, so it always reflects this context's tenant.</summary>
    internal Guid? CurrentTenantId => tenant.TenantId;

    internal bool IsSystem => tenant.IsSystem;

    internal ITenantContext Tenant => tenant;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RotadeskDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                ApplyTenantFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            }
        }
    }

    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(RotadeskDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned
    {
        // EF Core turns the context's properties into query parameters, so one compiled model serves every tenant.
        Expression<Func<TEntity, bool>> filter = e => IsSystem || e.TenantId == CurrentTenantId;
        modelBuilder.Entity<TEntity>().HasQueryFilter(filter);
    }
}
