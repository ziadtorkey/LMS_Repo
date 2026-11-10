using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleAuditAndUniqueBuiltIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Roles_OrganizationId_BuiltInKey",
                table: "Roles");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_OrganizationId_BuiltInKey",
                table: "Roles",
                columns: new[] { "OrganizationId", "BuiltInKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Roles_OrganizationId_BuiltInKey",
                table: "Roles");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_OrganizationId_BuiltInKey",
                table: "Roles",
                columns: new[] { "OrganizationId", "BuiltInKey" });
        }
    }
}
