using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    SlotId = table.Column<Guid>(type: "char(36)", nullable: true),
                    PeriodId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Source = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    State = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    TakenByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    TakenAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LiveSessionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    SessionKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_AttendanceSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceSessions_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceSessions_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AttendanceRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    AttendanceSessionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudentProfileId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    Note = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    RecordKey = table.Column<string>(type: "varchar(70)", maxLength: 70, nullable: true),
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
                    table.PrimaryKey("PK_AttendanceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_AttendanceSessions_AttendanceSessionId",
                        column: x => x.AttendanceSessionId,
                        principalTable: "AttendanceSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AttendanceRecordChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    AttendanceRecordId = table.Column<Guid>(type: "char(36)", nullable: false),
                    FromStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    Kind = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    ByUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    AtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
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
                    table.PrimaryKey("PK_AttendanceRecordChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceRecordChanges_AttendanceRecords_AttendanceRecordId",
                        column: x => x.AttendanceRecordId,
                        principalTable: "AttendanceRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceRecordChanges_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecordChanges_AttendanceRecordId_AtUtc",
                table: "AttendanceRecordChanges",
                columns: new[] { "AttendanceRecordId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecordChanges_OrganizationId_OrgUnitPath",
                table: "AttendanceRecordChanges",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecordChanges_OrgUnitId",
                table: "AttendanceRecordChanges",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_AttendanceSessionId",
                table: "AttendanceRecords",
                column: "AttendanceSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_OrganizationId_OrgUnitPath",
                table: "AttendanceRecords",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_OrgUnitId",
                table: "AttendanceRecords",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_RecordKey",
                table: "AttendanceRecords",
                column: "RecordKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_StudentProfileId_Status",
                table: "AttendanceRecords",
                columns: new[] { "StudentProfileId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_ClassCourseId_Date",
                table: "AttendanceSessions",
                columns: new[] { "ClassCourseId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_ClassId_Date",
                table: "AttendanceSessions",
                columns: new[] { "ClassId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_LiveSessionId",
                table: "AttendanceSessions",
                column: "LiveSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_OrganizationId_OrgUnitPath",
                table: "AttendanceSessions",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_OrgUnitId",
                table: "AttendanceSessions",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_SessionKey",
                table: "AttendanceSessions",
                column: "SessionKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceRecordChanges");

            migrationBuilder.DropTable(
                name: "AttendanceRecords");

            migrationBuilder.DropTable(
                name: "AttendanceSessions");
        }
    }
}
