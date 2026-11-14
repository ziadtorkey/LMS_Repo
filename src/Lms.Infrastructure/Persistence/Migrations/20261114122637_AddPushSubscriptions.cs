using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPushSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Endpoint = table.Column<string>(type: "varchar(600)", maxLength: 600, nullable: false),
                    EndpointHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ProtectedP256dh = table.Column<string>(type: "varchar(600)", maxLength: 600, nullable: false),
                    ProtectedAuth = table.Column<string>(type: "varchar(600)", maxLength: 600, nullable: false),
                    DeviceLabel = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastSuccessUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushSubscriptions", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_OrganizationId_EndpointHash",
                table: "PushSubscriptions",
                columns: new[] { "OrganizationId", "EndpointHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_OrganizationId_UserId",
                table: "PushSubscriptions",
                columns: new[] { "OrganizationId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PushSubscriptions");
        }
    }
}
