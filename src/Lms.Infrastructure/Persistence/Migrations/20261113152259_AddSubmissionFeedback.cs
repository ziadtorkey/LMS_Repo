using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DueSoonNotifiedAtUtc",
                table: "CourseAssignments",
                type: "datetime(6)",
                precision: 6,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubmissionFeedbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SubmissionKey = table.Column<Guid>(type: "char(36)", nullable: true),
                    AssignmentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Points = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    PenaltyPercentApplied = table.Column<int>(type: "int", nullable: true),
                    FinalGrade = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    FeedbackHtml = table.Column<string>(type: "longtext", nullable: true),
                    AnnotatedFileAssetId = table.Column<Guid>(type: "char(36)", nullable: true),
                    GradedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    ReturnedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ReturnedFinalGrade = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    ReturnedRevision = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_SubmissionFeedbacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubmissionFeedbacks_Assets_AnnotatedFileAssetId",
                        column: x => x.AnnotatedFileAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubmissionFeedbacks_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubmissionFeedbacks_Submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "Submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionFeedbacks_AnnotatedFileAssetId",
                table: "SubmissionFeedbacks",
                column: "AnnotatedFileAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionFeedbacks_AssignmentId_StudentId",
                table: "SubmissionFeedbacks",
                columns: new[] { "AssignmentId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionFeedbacks_OrganizationId_OrgUnitPath",
                table: "SubmissionFeedbacks",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionFeedbacks_OrgUnitId",
                table: "SubmissionFeedbacks",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionFeedbacks_SubmissionId",
                table: "SubmissionFeedbacks",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionFeedbacks_SubmissionKey",
                table: "SubmissionFeedbacks",
                column: "SubmissionKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubmissionFeedbacks");

            migrationBuilder.DropColumn(
                name: "DueSoonNotifiedAtUtc",
                table: "CourseAssignments");
        }
    }
}
