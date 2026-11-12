using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLessonModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LessonModules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassLessonId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LessonKey = table.Column<Guid>(type: "char(36)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    TitleAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    TitleEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Visibility = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    VisibleFromUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    VisibleToUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Target = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    GroupId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CompletionRule = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    CompletionMinScore = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    RefId = table.Column<Guid>(type: "char(36)", nullable: true),
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
                    table.PrimaryKey("PK_LessonModules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonModules_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonModules_ClassLessons_ClassLessonId",
                        column: x => x.ClassLessonId,
                        principalTable: "ClassLessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonModules_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonModules_StudentGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "StudentGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModuleCompletions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400)", nullable: false),
                    ModuleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    State = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Score = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModuleCompletions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModuleCompletions_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ModuleCompletions_LessonModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "LessonModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ModuleCompletions_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ModuleCompletions_StudentProfiles_StudentId",
                        column: x => x.StudentId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModuleContents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ModuleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ModuleKey = table.Column<Guid>(type: "char(36)", nullable: true),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AssetId = table.Column<Guid>(type: "char(36)", nullable: true),
                    BodyHtmlAr = table.Column<string>(type: "longtext", nullable: true),
                    BodyHtmlEn = table.Column<string>(type: "longtext", nullable: true),
                    Url = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_ModuleContents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModuleContents_LessonModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "LessonModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ModuleContents_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LessonModules_ClassCourseId_LessonKey",
                table: "LessonModules",
                columns: new[] { "ClassCourseId", "LessonKey" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonModules_ClassLessonId_Order",
                table: "LessonModules",
                columns: new[] { "ClassLessonId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonModules_GroupId",
                table: "LessonModules",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonModules_OrganizationId_OrgUnitPath",
                table: "LessonModules",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonModules_OrgUnitId",
                table: "LessonModules",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ModuleCompletions_ClassCourseId_StudentId",
                table: "ModuleCompletions",
                columns: new[] { "ClassCourseId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ModuleCompletions_ModuleId_StudentId",
                table: "ModuleCompletions",
                columns: new[] { "ModuleId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModuleCompletions_OrganizationId_OrgUnitPath",
                table: "ModuleCompletions",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_ModuleCompletions_OrgUnitId",
                table: "ModuleCompletions",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ModuleCompletions_StudentId",
                table: "ModuleCompletions",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_ModuleContents_ModuleId",
                table: "ModuleContents",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_ModuleContents_ModuleKey",
                table: "ModuleContents",
                column: "ModuleKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModuleContents_OrganizationId_OrgUnitPath",
                table: "ModuleContents",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_ModuleContents_OrgUnitId",
                table: "ModuleContents",
                column: "OrgUnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModuleCompletions");

            migrationBuilder.DropTable(
                name: "ModuleContents");

            migrationBuilder.DropTable(
                name: "LessonModules");
        }
    }
}
