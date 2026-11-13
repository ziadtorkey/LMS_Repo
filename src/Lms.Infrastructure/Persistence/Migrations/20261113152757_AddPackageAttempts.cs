using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PackageAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    PackageId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ModuleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    OpenMarker = table.Column<int>(type: "int", nullable: true),
                    Format = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    LastSeq = table.Column<long>(type: "bigint", nullable: false),
                    DataJson = table.Column<string>(type: "longtext", nullable: false),
                    ScoreRaw = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    ScoreMin = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    ScoreMax = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    LastCommitAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
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
                    table.PrimaryKey("PK_PackageAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageAttempts_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackageAttempts_PackageUploads_PackageId",
                        column: x => x.PackageId,
                        principalTable: "PackageUploads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PackageAttempts_ModuleId_StudentId",
                table: "PackageAttempts",
                columns: new[] { "ModuleId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PackageAttempts_OrganizationId_OrgUnitPath",
                table: "PackageAttempts",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_PackageAttempts_OrgUnitId",
                table: "PackageAttempts",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageAttempts_PackageId_StudentId_OpenMarker",
                table: "PackageAttempts",
                columns: new[] { "PackageId", "StudentId", "OpenMarker" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackageAttempts");
        }
    }
}
