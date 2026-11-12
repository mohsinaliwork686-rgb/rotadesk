using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Rotadesk.Domain;

namespace Rotadesk.Infrastructure.Persistence.Configurations;

internal sealed class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    public void Configure(EntityTypeBuilder<Business> builder)
    {
        builder.Property(b => b.Name).HasMaxLength(200);
        builder.Property(b => b.TimeZoneId).HasMaxLength(64);
        builder.Ignore(b => b.TimeZone);
    }
}

internal sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.HasOne<Business>().WithMany().HasForeignKey(s => s.TenantId);
        builder.Property(s => s.Name).HasMaxLength(200);
        builder.Property(s => s.Email).HasMaxLength(320);
        builder.Property(s => s.Role).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(s => new { s.TenantId, s.Email }).IsUnique();

        // Rows that point at a staff member point at (business, id), so the database refuses a link to another
        // business's staff member. Sign-in accounts point at it the same way.
        builder.HasAlternateKey(s => new { s.TenantId, s.Id });
        builder.Ignore(s => s.IsManager);
    }
}

internal sealed class RotaWeekConfiguration : IEntityTypeConfiguration<RotaWeek>
{
    public void Configure(EntityTypeBuilder<RotaWeek> builder)
    {
        builder.HasOne<Business>().WithMany().HasForeignKey(r => r.TenantId);
        builder.HasIndex(r => new { r.TenantId, r.WeekStart }).IsUnique();
        builder.HasAlternateKey(r => new { r.TenantId, r.Id });
        builder.Ignore(r => r.IsPublished);
    }
}

internal sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        builder.HasOne<Business>().WithMany().HasForeignKey(s => s.TenantId);
        builder.HasAlternateKey(s => new { s.TenantId, s.Id });
        builder.HasOne<RotaWeek>().WithMany()
            .HasForeignKey(s => new { s.TenantId, s.RotaWeekId }).HasPrincipalKey(r => new { r.TenantId, r.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // An unassigned shift has no staff member, and PostgreSQL skips a composite key with a null in it.
        builder.HasOne<StaffMember>().WithMany()
            .HasForeignKey(s => new { s.TenantId, s.StaffMemberId }).HasPrincipalKey(m => new { m.TenantId, m.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.TenantId, s.StaffMemberId, s.Start });
        builder.ToTable(t => t.HasCheckConstraint("ck_shifts_end_after_start", "\"end\" > start"));
        builder.Ignore(s => s.Length);

        // PostgreSQL's xmin changes on every update, so a save based on a stale read fails instead of overwriting.
        builder.Property<uint>("Version").IsRowVersion();
    }
}

internal sealed class AvailabilityConfiguration : IEntityTypeConfiguration<Availability>
{
    public void Configure(EntityTypeBuilder<Availability> builder)
    {
        builder.ToTable("availability");
        builder.HasOne<Business>().WithMany().HasForeignKey(a => a.TenantId);
        builder.HasOne<StaffMember>().WithMany()
            .HasForeignKey(a => new { a.TenantId, a.StaffMemberId }).HasPrincipalKey(m => new { m.TenantId, m.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => new { a.TenantId, a.StaffMemberId });
        builder.ToTable(t => t.HasCheckConstraint("ck_availability_to_after_from", "\"to\" > \"from\""));
    }
}

internal sealed class SwapRequestConfiguration : IEntityTypeConfiguration<SwapRequest>
{
    public void Configure(EntityTypeBuilder<SwapRequest> builder)
    {
        builder.HasOne<Business>().WithMany().HasForeignKey(s => s.TenantId);
        builder.HasOne<StaffMember>().WithMany()
            .HasForeignKey(s => new { s.TenantId, s.RequesterId }).HasPrincipalKey(m => new { m.TenantId, m.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StaffMember>().WithMany()
            .HasForeignKey(s => new { s.TenantId, s.ColleagueId }).HasPrincipalKey(m => new { m.TenantId, m.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Shift>().WithMany()
            .HasForeignKey(s => new { s.TenantId, s.OfferedShiftId }).HasPrincipalKey(m => new { m.TenantId, m.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Shift>().WithMany()
            .HasForeignKey(s => new { s.TenantId, s.ReturnShiftId }).HasPrincipalKey(m => new { m.TenantId, m.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(s => new { s.TenantId, s.Status });
        builder.Ignore(s => s.IsFinal);
        builder.Property<uint>("Version").IsRowVersion();
    }
}
