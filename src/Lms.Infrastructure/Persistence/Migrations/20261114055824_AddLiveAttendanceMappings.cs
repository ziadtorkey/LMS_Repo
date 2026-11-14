using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveAttendanceMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LiveAttendanceMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    AttendanceSessionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudentProfileId = table.Column<Guid>(type: "char(36)", nullable: true),
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
                    table.PrimaryKey("PK_LiveAttendanceMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveAttendanceMappings_AttendanceSessions_AttendanceSessionId",
                        column: x => x.AttendanceSessionId,
                        principalTable: "AttendanceSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiveAttendanceMappings_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiveAttendanceMappings_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LiveAttendanceMappings_AttendanceSessionId_ParticipantId",
                table: "LiveAttendanceMappings",
                columns: new[] { "AttendanceSessionId", "ParticipantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveAttendanceMappings_OrganizationId_OrgUnitPath",
                table: "LiveAttendanceMappings",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveAttendanceMappings_OrgUnitId",
                table: "LiveAttendanceMappings",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveAttendanceMappings_StudentProfileId",
                table: "LiveAttendanceMappings",
                column: "StudentProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LiveAttendanceMappings");
        }
    }
}
