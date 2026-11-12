using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBlueprints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Blueprints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    CourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PublishedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    ApplyMode = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    DraftKey = table.Column<Guid>(type: "char(36)", nullable: true),
                    VersionKey = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_Blueprints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Blueprints_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Blueprints_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BlueprintSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400)", nullable: false),
                    BlueprintId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Key = table.Column<Guid>(type: "char(36)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    TitleAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    TitleEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlueprintSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlueprintSections_Blueprints_BlueprintId",
                        column: x => x.BlueprintId,
                        principalTable: "Blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BlueprintSections_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClassLessonPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseKey = table.Column<Guid>(type: "char(36)", nullable: true),
                    BlueprintId = table.Column<Guid>(type: "char(36)", nullable: true),
                    BlueprintVersion = table.Column<int>(type: "int", nullable: true),
                    SyncedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
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
                    table.PrimaryKey("PK_ClassLessonPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassLessonPlans_Blueprints_BlueprintId",
                        column: x => x.BlueprintId,
                        principalTable: "Blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassLessonPlans_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassLessonPlans_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BlueprintLessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400)", nullable: false),
                    BlueprintId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SectionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Key = table.Column<Guid>(type: "char(36)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    TitleAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    TitleEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    ObjectivesAr = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                    ObjectivesEn = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                    PlannedOffsetDays = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlueprintLessons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlueprintLessons_BlueprintSections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "BlueprintSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BlueprintLessons_Blueprints_BlueprintId",
                        column: x => x.BlueprintId,
                        principalTable: "Blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BlueprintLessons_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClassSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400)", nullable: false),
                    PlanId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Key = table.Column<Guid>(type: "char(36)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    TitleAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    TitleEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    IsHidden = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassSections_ClassLessonPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "ClassLessonPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassSections_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClassLessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400)", nullable: false),
                    PlanId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SectionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Key = table.Column<Guid>(type: "char(36)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    TitleAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    TitleEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    ObjectivesAr = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                    ObjectivesEn = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                    PlannedOffsetDays = table.Column<int>(type: "int", nullable: true),
                    IsHidden = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassLessons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassLessons_ClassLessonPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "ClassLessonPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassLessons_ClassSections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "ClassSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassLessons_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_BlueprintLessons_BlueprintId_Key",
                table: "BlueprintLessons",
                columns: new[] { "BlueprintId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlueprintLessons_OrganizationId_OrgUnitPath",
                table: "BlueprintLessons",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_BlueprintLessons_OrgUnitId",
                table: "BlueprintLessons",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_BlueprintLessons_SectionId",
                table: "BlueprintLessons",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Blueprints_CourseId_DraftKey",
                table: "Blueprints",
                columns: new[] { "CourseId", "DraftKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Blueprints_CourseId_VersionKey",
                table: "Blueprints",
                columns: new[] { "CourseId", "VersionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Blueprints_OrganizationId_OrgUnitPath",
                table: "Blueprints",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_Blueprints_OrgUnitId",
                table: "Blueprints",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_BlueprintSections_BlueprintId_Key",
                table: "BlueprintSections",
                columns: new[] { "BlueprintId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlueprintSections_OrganizationId_OrgUnitPath",
                table: "BlueprintSections",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_BlueprintSections_OrgUnitId",
                table: "BlueprintSections",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassLessonPlans_BlueprintId",
                table: "ClassLessonPlans",
                column: "BlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassLessonPlans_ClassCourseId",
                table: "ClassLessonPlans",
                column: "ClassCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassLessonPlans_ClassCourseKey",
                table: "ClassLessonPlans",
                column: "ClassCourseKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassLessonPlans_OrganizationId_OrgUnitPath",
                table: "ClassLessonPlans",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassLessonPlans_OrgUnitId",
                table: "ClassLessonPlans",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassLessons_OrganizationId_OrgUnitPath",
                table: "ClassLessons",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassLessons_OrgUnitId",
                table: "ClassLessons",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassLessons_PlanId_Key",
                table: "ClassLessons",
                columns: new[] { "PlanId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassLessons_SectionId",
                table: "ClassLessons",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSections_OrganizationId_OrgUnitPath",
                table: "ClassSections",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassSections_OrgUnitId",
                table: "ClassSections",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSections_PlanId_Key",
                table: "ClassSections",
                columns: new[] { "PlanId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlueprintLessons");

            migrationBuilder.DropTable(
                name: "ClassLessons");

            migrationBuilder.DropTable(
                name: "BlueprintSections");

            migrationBuilder.DropTable(
                name: "ClassSections");

            migrationBuilder.DropTable(
                name: "ClassLessonPlans");

            migrationBuilder.DropTable(
                name: "Blueprints");
        }
    }
}
