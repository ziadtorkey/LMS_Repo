using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PackageUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LessonKey = table.Column<Guid>(type: "char(36)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    FileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    ArchiveFileId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    RejectReason = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true),
                    Title = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    MainLibrary = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    StorePrefix = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    ModuleId = table.Column<Guid>(type: "char(36)", nullable: true),
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
                    table.PrimaryKey("PK_PackageUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageUploads_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackageUploads_LessonModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "LessonModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackageUploads_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackageUploads_StoredFiles_ArchiveFileId",
                        column: x => x.ArchiveFileId,
                        principalTable: "StoredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PackageUploads_ArchiveFileId",
                table: "PackageUploads",
                column: "ArchiveFileId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageUploads_ClassCourseId_LessonKey",
                table: "PackageUploads",
                columns: new[] { "ClassCourseId", "LessonKey" });

            migrationBuilder.CreateIndex(
                name: "IX_PackageUploads_ModuleId",
                table: "PackageUploads",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageUploads_OrganizationId_OrgUnitPath",
                table: "PackageUploads",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_PackageUploads_OrganizationId_Status",
                table: "PackageUploads",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PackageUploads_OrgUnitId",
                table: "PackageUploads",
                column: "OrgUnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackageUploads");
        }
    }
}
