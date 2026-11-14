using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGradebook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Gradebooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseKey = table.Column<Guid>(type: "char(36)", nullable: true),
                    TermId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ScaleId = table.Column<Guid>(type: "char(36)", nullable: true),
                    DecimalPlaces = table.Column<int>(type: "int", nullable: false),
                    CategoryMethod = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    MissingPolicy = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    RoundingMode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    State = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    LockedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
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
                    table.PrimaryKey("PK_Gradebooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Gradebooks_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Gradebooks_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Gradebooks_Terms_TermId",
                        column: x => x.TermId,
                        principalTable: "Terms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GradeCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    GradebookId = table.Column<Guid>(type: "char(36)", nullable: false),
                    NameAr = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    IsUncategorised = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UncategorisedKey = table.Column<Guid>(type: "char(36)", nullable: true),
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
                    table.PrimaryKey("PK_GradeCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeCategories_Gradebooks_GradebookId",
                        column: x => x.GradebookId,
                        principalTable: "Gradebooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradeCategories_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GradeItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    GradebookId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CategoryId = table.Column<Guid>(type: "char(36)", nullable: false),
                    TitleAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    TitleEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    SourceKind = table.Column<int>(type: "int", nullable: true),
                    SourceId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SourceKey = table.Column<Guid>(type: "char(36)", nullable: true),
                    MaxPoints = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    PublishState = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    PublishAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_GradeItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeItems_GradeCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "GradeCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradeItems_Gradebooks_GradebookId",
                        column: x => x.GradebookId,
                        principalTable: "Gradebooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradeItems_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GradeEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    GradebookId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GradeItemId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ItemKey = table.Column<Guid>(type: "char(36)", nullable: true),
                    StudentProfileId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Points = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    IsExcused = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsOverride = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    ByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    AtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_GradeEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeEntries_GradeItems_GradeItemId",
                        column: x => x.GradeItemId,
                        principalTable: "GradeItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradeEntries_Gradebooks_GradebookId",
                        column: x => x.GradebookId,
                        principalTable: "Gradebooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradeEntries_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GradeEntries_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Gradebooks_ClassCourseId",
                table: "Gradebooks",
                column: "ClassCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_Gradebooks_ClassCourseKey_TermId",
                table: "Gradebooks",
                columns: new[] { "ClassCourseKey", "TermId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Gradebooks_OrganizationId_OrgUnitPath",
                table: "Gradebooks",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_Gradebooks_OrgUnitId",
                table: "Gradebooks",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Gradebooks_TermId",
                table: "Gradebooks",
                column: "TermId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeCategories_GradebookId_Order",
                table: "GradeCategories",
                columns: new[] { "GradebookId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeCategories_OrganizationId_OrgUnitPath",
                table: "GradeCategories",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeCategories_OrgUnitId",
                table: "GradeCategories",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeCategories_UncategorisedKey",
                table: "GradeCategories",
                column: "UncategorisedKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GradeEntries_GradebookId_StudentProfileId",
                table: "GradeEntries",
                columns: new[] { "GradebookId", "StudentProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeEntries_GradeItemId",
                table: "GradeEntries",
                column: "GradeItemId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeEntries_ItemKey_StudentProfileId",
                table: "GradeEntries",
                columns: new[] { "ItemKey", "StudentProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GradeEntries_OrganizationId_OrgUnitPath",
                table: "GradeEntries",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeEntries_OrgUnitId",
                table: "GradeEntries",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeEntries_StudentProfileId",
                table: "GradeEntries",
                column: "StudentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeItems_CategoryId",
                table: "GradeItems",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeItems_GradebookId_PublishState",
                table: "GradeItems",
                columns: new[] { "GradebookId", "PublishState" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeItems_GradebookId_SourceKind_SourceKey",
                table: "GradeItems",
                columns: new[] { "GradebookId", "SourceKind", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GradeItems_OrganizationId_OrgUnitPath",
                table: "GradeItems",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeItems_OrgUnitId",
                table: "GradeItems",
                column: "OrgUnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GradeEntries");

            migrationBuilder.DropTable(
                name: "GradeItems");

            migrationBuilder.DropTable(
                name: "GradeCategories");

            migrationBuilder.DropTable(
                name: "Gradebooks");
        }
    }
}
