using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rotadesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "businesses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_businesses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rota_weeks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    week_start = table.Column<DateOnly>(type: "date", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rota_weeks", x => x.id);
                    table.ForeignKey(
                        name: "fk_rota_weeks_businesses_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staff_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    weekly_limit = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_members_businesses_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "availability",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<int>(type: "integer", nullable: false),
                    from = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    to = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_availability", x => x.id);
                    table.CheckConstraint("ck_availability_to_after_from", "\"to\" > \"from\"");
                    table.ForeignKey(
                        name: "fk_availability_businesses_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_availability_staff_members_staff_member_id",
                        column: x => x.staff_member_id,
                        principalTable: "staff_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shifts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rota_week_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shifts", x => x.id);
                    table.CheckConstraint("ck_shifts_end_after_start", "\"end\" > start");
                    table.ForeignKey(
                        name: "fk_shifts_businesses_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shifts_rota_weeks_rota_week_id",
                        column: x => x.rota_week_id,
                        principalTable: "rota_weeks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shifts_staff_members_staff_member_id",
                        column: x => x.staff_member_id,
                        principalTable: "staff_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "swap_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_id = table.Column<Guid>(type: "uuid", nullable: false),
                    colleague_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offered_shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_shift_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_swap_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_swap_requests_businesses_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_swap_requests_shifts_offered_shift_id",
                        column: x => x.offered_shift_id,
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_swap_requests_shifts_return_shift_id",
                        column: x => x.return_shift_id,
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_swap_requests_staff_members_colleague_id",
                        column: x => x.colleague_id,
                        principalTable: "staff_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_swap_requests_staff_members_requester_id",
                        column: x => x.requester_id,
                        principalTable: "staff_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_availability_staff_member_id",
                table: "availability",
                column: "staff_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_availability_tenant_id_staff_member_id",
                table: "availability",
                columns: new[] { "tenant_id", "staff_member_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rota_weeks_tenant_id_week_start",
                table: "rota_weeks",
                columns: new[] { "tenant_id", "week_start" },
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
                name: "ix_shifts_tenant_id_staff_member_id_start",
                table: "shifts",
                columns: new[] { "tenant_id", "staff_member_id", "start" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_members_tenant_id_email",
                table: "staff_members",
                columns: new[] { "tenant_id", "email" },
                unique: true);

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
                name: "ix_swap_requests_tenant_id_status",
                table: "swap_requests",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "availability");

            migrationBuilder.DropTable(
                name: "swap_requests");

            migrationBuilder.DropTable(
                name: "shifts");

            migrationBuilder.DropTable(
                name: "rota_weeks");

            migrationBuilder.DropTable(
                name: "staff_members");

            migrationBuilder.DropTable(
                name: "businesses");
        }
    }
}
