using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAbsenceExcuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbsenceExcuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudentProfileId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Reason = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ReasonText = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    DateFrom = table.Column<DateTime>(type: "date", nullable: true),
                    DateTo = table.Column<DateTime>(type: "date", nullable: true),
                    AttachmentFileId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    DecisionComment = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    ReviewerUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EscalatedToUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EscalatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EscalationComment = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    EscalatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
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
                    table.PrimaryKey("PK_AbsenceExcuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbsenceExcuses_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AbsenceExcuses_StoredFiles_AttachmentFileId",
                        column: x => x.AttachmentFileId,
                        principalTable: "StoredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AbsenceExcuses_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AbsenceExcuseSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    AbsenceExcuseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AttendanceSessionId = table.Column<Guid>(type: "char(36)", nullable: false),
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
                    table.PrimaryKey("PK_AbsenceExcuseSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbsenceExcuseSessions_AbsenceExcuses_AbsenceExcuseId",
                        column: x => x.AbsenceExcuseId,
                        principalTable: "AbsenceExcuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AbsenceExcuseSessions_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceExcuses_AttachmentFileId",
                table: "AbsenceExcuses",
                column: "AttachmentFileId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceExcuses_OrganizationId_OrgUnitPath_Status",
                table: "AbsenceExcuses",
                columns: new[] { "OrganizationId", "OrgUnitPath", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceExcuses_OrgUnitId",
                table: "AbsenceExcuses",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceExcuses_StudentProfileId_Status",
                table: "AbsenceExcuses",
                columns: new[] { "StudentProfileId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceExcuses_SubmittedByUserId",
                table: "AbsenceExcuses",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceExcuseSessions_AbsenceExcuseId_AttendanceSessionId",
                table: "AbsenceExcuseSessions",
                columns: new[] { "AbsenceExcuseId", "AttendanceSessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceExcuseSessions_AttendanceSessionId",
                table: "AbsenceExcuseSessions",
                column: "AttendanceSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceExcuseSessions_OrgUnitId",
                table: "AbsenceExcuseSessions",
                column: "OrgUnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbsenceExcuseSessions");

            migrationBuilder.DropTable(
                name: "AbsenceExcuses");
        }
    }
}
