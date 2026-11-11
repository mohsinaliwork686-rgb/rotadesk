using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rotadesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StaffMemberTenantKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_staff_members_tenant_id_id",
                table: "staff_members",
                columns: new[] { "tenant_id", "id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_staff_members_tenant_id_id",
                table: "staff_members");
        }
    }
}
