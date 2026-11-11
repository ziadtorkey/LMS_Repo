using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    FriendlyName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Xml = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "IdentityProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Slug = table.Column<string>(type: "varchar(40)", nullable: false),
                    DisplayNameAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    DisplayNameEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Authority = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    AzureTenantId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    ClientId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    ProtectedClientSecret = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    ClientSecretUpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Scopes = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    MatchBy = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    NationalIdClaim = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    EmailDomains = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityProviders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdentityProviders_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityProviders_OrganizationId",
                table: "IdentityProviders",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityProviders_Slug",
                table: "IdentityProviders",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "IdentityProviders");
        }
    }
}
