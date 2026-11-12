using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGradeEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GradeEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    SourceKind = table.Column<int>(type: "int", nullable: false),
                    SourceId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudentProfileId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Points = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    MaxPoints = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    ScoredAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_GradeEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeEvents_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradeEvents_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradeEvents_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GradeEvents_ClassCourseId_StudentProfileId",
                table: "GradeEvents",
                columns: new[] { "ClassCourseId", "StudentProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeEvents_OrganizationId_OrgUnitPath",
                table: "GradeEvents",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeEvents_OrgUnitId",
                table: "GradeEvents",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeEvents_SourceKind_SourceId_StudentProfileId_Revision",
                table: "GradeEvents",
                columns: new[] { "SourceKind", "SourceId", "StudentProfileId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GradeEvents_StudentProfileId",
                table: "GradeEvents",
                column: "StudentProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GradeEvents");
        }
    }
}
