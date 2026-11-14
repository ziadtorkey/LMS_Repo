using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MessageReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    MessageId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConversationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ReporterUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ReportedUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Reason = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ContextJson = table.Column<string>(type: "longtext", nullable: false),
                    ReviewDueUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Action = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReportKey = table.Column<string>(type: "varchar(70)", maxLength: 70, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageReports", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MessageReportUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ReportId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageReportUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageReportUnits_MessageReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "MessageReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MessageReportUnits_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_MessageReports_MessageId",
                table: "MessageReports",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageReports_OrganizationId_ReportedUserId",
                table: "MessageReports",
                columns: new[] { "OrganizationId", "ReportedUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageReports_OrganizationId_Status_CreatedAt",
                table: "MessageReports",
                columns: new[] { "OrganizationId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageReports_ReporterUserId_CreatedAt",
                table: "MessageReports",
                columns: new[] { "ReporterUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageReports_ReportKey",
                table: "MessageReports",
                column: "ReportKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessageReportUnits_OrganizationId_OrgUnitPath",
                table: "MessageReportUnits",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageReportUnits_OrgUnitId",
                table: "MessageReportUnits",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageReportUnits_ReportId",
                table: "MessageReportUnits",
                column: "ReportId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessageReportUnits");

            migrationBuilder.DropTable(
                name: "MessageReports");
        }
    }
}
