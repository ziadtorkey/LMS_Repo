using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGradeSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GradeSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    GradebookId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudentProfileId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    BatchId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    ValuesJson = table.Column<string>(type: "json", nullable: false),
                    ByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    AtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
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
                    table.PrimaryKey("PK_GradeSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeSnapshots_Gradebooks_GradebookId",
                        column: x => x.GradebookId,
                        principalTable: "Gradebooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradeSnapshots_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradeSnapshots_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GradeSnapshots_BatchId",
                table: "GradeSnapshots",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeSnapshots_GradebookId_StudentProfileId_Kind",
                table: "GradeSnapshots",
                columns: new[] { "GradebookId", "StudentProfileId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeSnapshots_OrganizationId_OrgUnitPath",
                table: "GradeSnapshots",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeSnapshots_OrgUnitId",
                table: "GradeSnapshots",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeSnapshots_StudentProfileId",
                table: "GradeSnapshots",
                column: "StudentProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GradeSnapshots");
        }
    }
}
