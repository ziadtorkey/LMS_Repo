using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditOrgUnitPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrgUnitPath",
                table: "AuditEntries",
                type: "varchar(400)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OrganizationId_OrgUnitPath_TimestampUtc",
                table: "AuditEntries",
                columns: new[] { "OrganizationId", "OrgUnitPath", "TimestampUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_OrganizationId_OrgUnitPath_TimestampUtc",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "OrgUnitPath",
                table: "AuditEntries");
        }
    }
}
