using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrollmentExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnrollmentExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    FailedTypesJson = table.Column<string>(type: "json", nullable: false),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    PendingKey = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                    RequestedBy = table.Column<Guid>(type: "char(36)", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DecisionComment = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_EnrollmentExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnrollmentExceptions_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnrollmentExceptions_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnrollmentExceptions_StudentProfiles_StudentId",
                        column: x => x.StudentId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentExceptions_ClassCourseId",
                table: "EnrollmentExceptions",
                column: "ClassCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentExceptions_OrganizationId_OrgUnitPath_Status",
                table: "EnrollmentExceptions",
                columns: new[] { "OrganizationId", "OrgUnitPath", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentExceptions_OrgUnitId",
                table: "EnrollmentExceptions",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentExceptions_PendingKey",
                table: "EnrollmentExceptions",
                column: "PendingKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentExceptions_StudentId",
                table: "EnrollmentExceptions",
                column: "StudentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnrollmentExceptions");
        }
    }
}
