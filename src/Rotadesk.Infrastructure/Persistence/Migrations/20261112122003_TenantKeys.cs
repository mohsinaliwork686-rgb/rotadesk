using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rotadesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_availability_staff_members_staff_member_id",
                table: "availability");

            migrationBuilder.DropForeignKey(
                name: "fk_shifts_rota_weeks_rota_week_id",
                table: "shifts");

            migrationBuilder.DropForeignKey(
                name: "fk_shifts_staff_members_staff_member_id",
                table: "shifts");

            migrationBuilder.DropForeignKey(
                name: "fk_swap_requests_shifts_offered_shift_id",
                table: "swap_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_swap_requests_shifts_return_shift_id",
                table: "swap_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_swap_requests_staff_members_colleague_id",
                table: "swap_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_swap_requests_staff_members_requester_id",
                table: "swap_requests");

            migrationBuilder.DropIndex(
                name: "ix_swap_requests_colleague_id",
                table: "swap_requests");

            migrationBuilder.DropIndex(
                name: "ix_swap_requests_offered_shift_id",
                table: "swap_requests");

            migrationBuilder.DropIndex(
                name: "ix_swap_requests_requester_id",
                table: "swap_requests");

            migrationBuilder.DropIndex(
                name: "ix_swap_requests_return_shift_id",
                table: "swap_requests");

            migrationBuilder.DropIndex(
                name: "ix_shifts_rota_week_id",
                table: "shifts");

            migrationBuilder.DropIndex(
                name: "ix_shifts_staff_member_id",
                table: "shifts");

            migrationBuilder.DropIndex(
                name: "ix_availability_staff_member_id",
                table: "availability");

            // The accounts' foreign key already depends on this index, so it can't be dropped and made again. PostgreSQL
            // turns the unique index into the constraint in place instead (and renames it).
            migrationBuilder.Sql("ALTER TABLE staff_members ADD CONSTRAINT ak_staff_members_tenant_id_id UNIQUE USING INDEX ix_staff_members_tenant_id_id;");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_shifts_tenant_id_id",
                table: "shifts",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_rota_weeks_tenant_id_id",
                table: "rota_weeks",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_swap_requests_tenant_id_colleague_id",
                table: "swap_requests",
                columns: new[] { "tenant_id", "colleague_id" });

            migrationBuilder.CreateIndex(
                name: "ix_swap_requests_tenant_id_offered_shift_id",
                table: "swap_requests",
                columns: new[] { "tenant_id", "offered_shift_id" });

            migrationBuilder.CreateIndex(
                name: "ix_swap_requests_tenant_id_requester_id",
                table: "swap_requests",
                columns: new[] { "tenant_id", "requester_id" });

            migrationBuilder.CreateIndex(
                name: "ix_swap_requests_tenant_id_return_shift_id",
                table: "swap_requests",
                columns: new[] { "tenant_id", "return_shift_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shifts_tenant_id_rota_week_id",
                table: "shifts",
                columns: new[] { "tenant_id", "rota_week_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_availability_staff_members_tenant_id_staff_member_id",
                table: "availability",
                columns: new[] { "tenant_id", "staff_member_id" },
                principalTable: "staff_members",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_shifts_rota_weeks_tenant_id_rota_week_id",
                table: "shifts",
                columns: new[] { "tenant_id", "rota_week_id" },
                principalTable: "rota_weeks",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_shifts_staff_members_tenant_id_staff_member_id",
                table: "shifts",
                columns: new[] { "tenant_id", "staff_member_id" },
                principalTable: "staff_members",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_swap_requests_shifts_tenant_id_offered_shift_id",
                table: "swap_requests",
                columns: new[] { "tenant_id", "offered_shift_id" },
                principalTable: "shifts",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_swap_requests_shifts_tenant_id_return_shift_id",
                table: "swap_requests",
                columns: new[] { "tenant_id", "return_shift_id" },
                principalTable: "shifts",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_swap_requests_staff_members_tenant_id_colleague_id",
                table: "swap_requests",
                columns: new[] { "tenant_id", "colleague_id" },
                principalTable: "staff_members",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_swap_requests_staff_members_tenant_id_requester_id",
                table: "swap_requests",
                columns: new[] { "tenant_id", "requester_id" },
                principalTable: "staff_members",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Roll the accounts migration back first: its foreign key depends on ak_staff_members_tenant_id_id.
            migrationBuilder.DropForeignKey(
                name: "fk_availability_staff_members_tenant_id_staff_member_id",
                table: "availability");

            migrationBuilder.DropForeignKey(
                name: "fk_shifts_rota_weeks_tenant_id_rota_week_id",
                table: "shifts");

            migrationBuilder.DropForeignKey(
                name: "fk_shifts_staff_members_tenant_id_staff_member_id",
                table: "shifts");

            migrationBuilder.DropForeignKey(
                name: "fk_swap_requests_shifts_tenant_id_offered_shift_id",
                table: "swap_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_swap_requests_shifts_tenant_id_return_shift_id",
                table: "swap_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_swap_requests_staff_members_tenant_id_colleague_id",
                table: "swap_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_swap_requests_staff_members_tenant_id_requester_id",
                table: "swap_requests");

            migrationBuilder.DropIndex(
                name: "ix_swap_requests_tenant_id_colleague_id",
                table: "swap_requests");

            migrationBuilder.DropIndex(
                name: "ix_swap_requests_tenant_id_offered_shift_id",
                table: "swap_requests");

            migrationBuilder.DropIndex(
                name: "ix_swap_requests_tenant_id_requester_id",
                table: "swap_requests");

            migrationBuilder.DropIndex(
                name: "ix_swap_requests_tenant_id_return_shift_id",
                table: "swap_requests");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_staff_members_tenant_id_id",
                table: "staff_members");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_shifts_tenant_id_id",
                table: "shifts");

            migrationBuilder.DropIndex(
                name: "ix_shifts_tenant_id_rota_week_id",
                table: "shifts");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_rota_weeks_tenant_id_id",
                table: "rota_weeks");

            migrationBuilder.CreateIndex(
                name: "ix_swap_requests_colleague_id",
                table: "swap_requests",
                column: "colleague_id");

            migrationBuilder.CreateIndex(
                name: "ix_swap_requests_offered_shift_id",
                table: "swap_requests",
                column: "offered_shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_swap_requests_requester_id",
                table: "swap_requests",
                column: "requester_id");

            migrationBuilder.CreateIndex(
                name: "ix_swap_requests_return_shift_id",
                table: "swap_requests",
                column: "return_shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_members_tenant_id_id",
                table: "staff_members",
                columns: new[] { "tenant_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shifts_rota_week_id",
                table: "shifts",
                column: "rota_week_id");

            migrationBuilder.CreateIndex(
                name: "ix_shifts_staff_member_id",
                table: "shifts",
                column: "staff_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_availability_staff_member_id",
                table: "availability",
                column: "staff_member_id");

            migrationBuilder.AddForeignKey(
                name: "fk_availability_staff_members_staff_member_id",
                table: "availability",
                column: "staff_member_id",
                principalTable: "staff_members",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_shifts_rota_weeks_rota_week_id",
                table: "shifts",
                column: "rota_week_id",
                principalTable: "rota_weeks",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_shifts_staff_members_staff_member_id",
                table: "shifts",
                column: "staff_member_id",
                principalTable: "staff_members",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_swap_requests_shifts_offered_shift_id",
                table: "swap_requests",
                column: "offered_shift_id",
                principalTable: "shifts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_swap_requests_shifts_return_shift_id",
                table: "swap_requests",
                column: "return_shift_id",
                principalTable: "shifts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_swap_requests_staff_members_colleague_id",
                table: "swap_requests",
                column: "colleague_id",
                principalTable: "staff_members",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_swap_requests_staff_members_requester_id",
                table: "swap_requests",
                column: "requester_id",
                principalTable: "staff_members",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
