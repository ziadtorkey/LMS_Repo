using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContactSuppressions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Channel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    AddressHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactSuppressions", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DeliveryOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    NotificationId = table.Column<Guid>(type: "char(36)", nullable: true),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Channel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    TemplateKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    IsPriority = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LeaseUntilUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    HeldUntilUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Provider = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    LastErrorCode = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryOutbox", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ContactSuppressions_OrganizationId_Channel_AddressHash",
                table: "ContactSuppressions",
                columns: new[] { "OrganizationId", "Channel", "AddressHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOutbox_CreatedAtUtc",
                table: "DeliveryOutbox",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOutbox_NotificationId_Channel",
                table: "DeliveryOutbox",
                columns: new[] { "NotificationId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOutbox_OrganizationId_Channel_Status_NextAttemptAtUtc",
                table: "DeliveryOutbox",
                columns: new[] { "OrganizationId", "Channel", "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOutbox_OrganizationId_UserId_Channel_SentAtUtc",
                table: "DeliveryOutbox",
                columns: new[] { "OrganizationId", "UserId", "Channel", "SentAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContactSuppressions");

            migrationBuilder.DropTable(
                name: "DeliveryOutbox");
        }
    }
}
