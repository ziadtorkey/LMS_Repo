using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LibraryResources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TitleAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    TitleEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    DescriptionAr = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    DescriptionEn = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    Type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    CourseId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CoverAssetId = table.Column<Guid>(type: "char(36)", nullable: true),
                    FileAssetId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Url = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    DownloadAllowed = table.Column<bool>(type: "tinyint(1)", nullable: false),
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
                    table.PrimaryKey("PK_LibraryResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryResources_Assets_CoverAssetId",
                        column: x => x.CoverAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LibraryResources_Assets_FileAssetId",
                        column: x => x.FileAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LibraryResources_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LibraryResources_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LibraryResourceLevels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ResourceId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LevelTypeId = table.Column<Guid>(type: "char(36)", nullable: false),
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
                    table.PrimaryKey("PK_LibraryResourceLevels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryResourceLevels_LevelTypes_LevelTypeId",
                        column: x => x.LevelTypeId,
                        principalTable: "LevelTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LibraryResourceLevels_LibraryResources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "LibraryResources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LibraryResourceLevels_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LibraryTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ResourceId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Tag = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
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
                    table.PrimaryKey("PK_LibraryTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryTags_LibraryResources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "LibraryResources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LibraryTags_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryResourceLevels_LevelTypeId",
                table: "LibraryResourceLevels",
                column: "LevelTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryResourceLevels_OrgUnitId",
                table: "LibraryResourceLevels",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryResourceLevels_ResourceId",
                table: "LibraryResourceLevels",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryResources_CourseId",
                table: "LibraryResources",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryResources_CoverAssetId",
                table: "LibraryResources",
                column: "CoverAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryResources_FileAssetId",
                table: "LibraryResources",
                column: "FileAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryResources_OrganizationId_OrgUnitId_Type",
                table: "LibraryResources",
                columns: new[] { "OrganizationId", "OrgUnitId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryResources_OrgUnitId",
                table: "LibraryResources",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryTags_OrganizationId_Tag",
                table: "LibraryTags",
                columns: new[] { "OrganizationId", "Tag" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryTags_OrgUnitId",
                table: "LibraryTags",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryTags_ResourceId",
                table: "LibraryTags",
                column: "ResourceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibraryResourceLevels");

            migrationBuilder.DropTable(
                name: "LibraryTags");

            migrationBuilder.DropTable(
                name: "LibraryResources");
        }
    }
}
