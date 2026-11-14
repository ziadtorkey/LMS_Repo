using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTimetable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TimetableSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    TermId = table.Column<Guid>(type: "char(36)", nullable: false),
                    TeacherUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Day = table.Column<int>(type: "int", nullable: false),
                    PeriodId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RoomName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    State = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    SlotKey = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
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
                    table.PrimaryKey("PK_TimetableSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimetableSlots_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimetableSlots_Classes_ClassId",
                        column: x => x.ClassId,
                        principalTable: "Classes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimetableSlots_Periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "Periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TimetableSlots_ClassCourseId",
                table: "TimetableSlots",
                column: "ClassCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_TimetableSlots_ClassId_TermId",
                table: "TimetableSlots",
                columns: new[] { "ClassId", "TermId" });

            migrationBuilder.CreateIndex(
                name: "IX_TimetableSlots_OrganizationId_OrgUnitPath",
                table: "TimetableSlots",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_TimetableSlots_PeriodId",
                table: "TimetableSlots",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_TimetableSlots_SlotKey",
                table: "TimetableSlots",
                column: "SlotKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimetableSlots_TeacherUserId_TermId_Day_PeriodId",
                table: "TimetableSlots",
                columns: new[] { "TeacherUserId", "TermId", "Day", "PeriodId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimetableSlots");
        }
    }
}
