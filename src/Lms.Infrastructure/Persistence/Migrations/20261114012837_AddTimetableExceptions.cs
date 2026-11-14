using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTimetableExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TimetableExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    SlotId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    SubstituteUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    NewPeriodId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    ExceptionKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
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
                    table.PrimaryKey("PK_TimetableExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimetableExceptions_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimetableExceptions_Periods_NewPeriodId",
                        column: x => x.NewPeriodId,
                        principalTable: "Periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimetableExceptions_TimetableSlots_SlotId",
                        column: x => x.SlotId,
                        principalTable: "TimetableSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TimetableExceptions_ExceptionKey",
                table: "TimetableExceptions",
                column: "ExceptionKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimetableExceptions_NewPeriodId",
                table: "TimetableExceptions",
                column: "NewPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_TimetableExceptions_OrganizationId_OrgUnitPath",
                table: "TimetableExceptions",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_TimetableExceptions_OrgUnitId",
                table: "TimetableExceptions",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_TimetableExceptions_SlotId_Date",
                table: "TimetableExceptions",
                columns: new[] { "SlotId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_TimetableExceptions_SubstituteUserId_Date",
                table: "TimetableExceptions",
                columns: new[] { "SubstituteUserId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimetableExceptions");
        }
    }
}
