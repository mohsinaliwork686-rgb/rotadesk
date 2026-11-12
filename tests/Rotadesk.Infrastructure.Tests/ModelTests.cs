using Microsoft.EntityFrameworkCore;

using Rotadesk.Domain;

namespace Rotadesk.Infrastructure.Tests;

/// <summary>Checks on the model itself, so a new table can't quietly skip the tenant rules.</summary>
public class ModelTests(PostgresFixture fixture)
{
    [Fact]
    public void Every_tenant_owned_entity_has_a_query_filter()
    {
        using var db = fixture.NewContext(new FixedTenant(Guid.NewGuid()));

        var unfiltered = db.Model.GetEntityTypes()
            .Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType) && e.GetDeclaredQueryFilters().Count == 0)
            .Select(e => e.ClrType.Name)
            .ToList();

        Assert.Empty(unfiltered);
    }

    [Fact]
    public void Every_entity_with_a_tenant_id_is_tenant_owned()
    {
        using var db = fixture.NewContext(new FixedTenant(Guid.NewGuid()));

        var notMarked = db.Model.GetEntityTypes()
            .Where(e => e.FindProperty("TenantId") is not null && !typeof(ITenantOwned).IsAssignableFrom(e.ClrType))
            .Select(e => e.ClrType.Name)
            .ToList();

        Assert.Empty(notMarked);
    }

    [Fact]
    public void Every_entity_except_the_business_is_tenant_owned()
    {
        using var db = fixture.NewContext(new FixedTenant(Guid.NewGuid()));

        var shared = db.Model.GetEntityTypes()
            .Where(e => e.ClrType != typeof(Business) && !typeof(ITenantOwned).IsAssignableFrom(e.ClrType))
            .Select(e => e.ClrType.Name)
            .ToList();

        Assert.Empty(shared);
    }

    [Fact]
    public void Every_link_between_business_owned_rows_includes_the_business()
    {
        using var db = fixture.NewContext(new FixedTenant(Guid.NewGuid()));

        var unscoped = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetForeignKeys())
            .Where(fk => typeof(ITenantOwned).IsAssignableFrom(fk.DeclaringEntityType.ClrType)
                && typeof(ITenantOwned).IsAssignableFrom(fk.PrincipalEntityType.ClrType))
            .Where(fk => fk.Properties.All(p => p.Name != nameof(ITenantOwned.TenantId))
                || fk.PrincipalKey.Properties.All(p => p.Name != nameof(ITenantOwned.TenantId)))
            .Select(fk => $"{fk.DeclaringEntityType.ClrType.Name}.{string.Join("+", fk.Properties.Select(p => p.Name))}")
            .ToList();

        Assert.Empty(unscoped);
    }
}
