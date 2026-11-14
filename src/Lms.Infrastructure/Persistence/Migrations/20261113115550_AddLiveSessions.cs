using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LiveSessionSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LessonKey = table.Column<Guid>(type: "char(36)", nullable: false),
                    IntervalWeeks = table.Column<int>(type: "int", nullable: false),
                    DaysOfWeek = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    UntilUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    Count = table.Column<int>(type: "int", nullable: true),
                    TimeZoneId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
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
                    table.PrimaryKey("PK_LiveSessionSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveSessionSeries_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiveSessionSeries_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LiveSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LessonKey = table.Column<Guid>(type: "char(36)", nullable: false),
                    SeriesId = table.Column<Guid>(type: "char(36)", nullable: true),
                    OccurrenceIndex = table.Column<int>(type: "int", nullable: true),
                    ModuleId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Provider = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ExternalMeetingId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    HostUserKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    JoinUrlProtected = table.Column<string>(type: "text", nullable: true),
                    StartUrlProtected = table.Column<string>(type: "text", nullable: true),
                    RecordingUrl = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true),
                    RecordingSource = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    ReminderSentAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
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
                    table.PrimaryKey("PK_LiveSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveSessions_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiveSessions_LiveSessionSeries_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "LiveSessionSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiveSessions_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LiveSessionParticipants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    SessionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudentId = table.Column<Guid>(type: "char(36)", nullable: true),
                    DisplayName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    MatchState = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    LeftAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    DurationSeconds = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_LiveSessionParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveSessionParticipants_LiveSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "LiveSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiveSessionParticipants_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LiveSessionParticipants_OrganizationId_OrgUnitPath",
                table: "LiveSessionParticipants",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveSessionParticipants_OrgUnitId",
                table: "LiveSessionParticipants",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveSessionParticipants_SessionId_JoinedAtUtc",
                table: "LiveSessionParticipants",
                columns: new[] { "SessionId", "JoinedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveSessionParticipants_StudentId",
                table: "LiveSessionParticipants",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveSessions_ClassCourseId_StartsAtUtc",
                table: "LiveSessions",
                columns: new[] { "ClassCourseId", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveSessions_ExternalMeetingId",
                table: "LiveSessions",
                column: "ExternalMeetingId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveSessions_ModuleId",
                table: "LiveSessions",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveSessions_OrganizationId_OrgUnitPath",
                table: "LiveSessions",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveSessions_OrgUnitId",
                table: "LiveSessions",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveSessions_SeriesId",
                table: "LiveSessions",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveSessionSeries_ClassCourseId_LessonKey",
                table: "LiveSessionSeries",
                columns: new[] { "ClassCourseId", "LessonKey" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveSessionSeries_OrganizationId_OrgUnitPath",
                table: "LiveSessionSeries",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveSessionSeries_OrgUnitId",
                table: "LiveSessionSeries",
                column: "OrgUnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LiveSessionParticipants");

            migrationBuilder.DropTable(
                name: "LiveSessions");

            migrationBuilder.DropTable(
                name: "LiveSessionSeries");
        }
    }
}
