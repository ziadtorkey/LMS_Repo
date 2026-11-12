using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAcademicStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Classes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    CodeKey = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    NameAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: true),
                    HomeroomTeacherUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Classes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Classes_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Classes_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Courses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    CodeKey = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    NameAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    DescriptionAr = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    DescriptionEn = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    Language = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    WeeklyPeriods = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Courses_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClassStudents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AcademicYearId = table.Column<Guid>(type: "char(36)", nullable: false),
                    From = table.Column<DateTime>(type: "date", nullable: false),
                    To = table.Column<DateTime>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ActiveYearKey = table.Column<Guid>(type: "char(36)", nullable: true),
                    EndReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassStudents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassStudents_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassStudents_Classes_ClassId",
                        column: x => x.ClassId,
                        principalTable: "Classes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassStudents_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassStudents_StudentProfiles_StudentId",
                        column: x => x.StudentId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StudentTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci", nullable: false),
                    StudentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    FromClassId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ToClassId = table.Column<Guid>(type: "char(36)", nullable: false),
                    FromOrgUnitPath = table.Column<string>(type: "varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci", nullable: false),
                    EffectiveOn = table.Column<DateTime>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentTransfers_Classes_FromClassId",
                        column: x => x.FromClassId,
                        principalTable: "Classes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentTransfers_Classes_ToClassId",
                        column: x => x.ToClassId,
                        principalTable: "Classes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentTransfers_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentTransfers_StudentProfiles_StudentId",
                        column: x => x.StudentId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClassCourses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CourseKey = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsOptional = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassCourses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassCourses_Classes_ClassId",
                        column: x => x.ClassId,
                        principalTable: "Classes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassCourses_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassCourses_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CourseAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    CourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CourseKey = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseAllocations_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseAllocations_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CourseRestrictions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    CourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Type = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    ValuesJson = table.Column<string>(type: "json", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseRestrictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseRestrictions_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseRestrictions_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CourseSupervisors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    CourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LiveKey = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseSupervisors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseSupervisors_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseSupervisors_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseSupervisors_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TeacherCourseEligibilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LiveKey = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherCourseEligibilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeacherCourseEligibilities_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeacherCourseEligibilities_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeacherCourseEligibilities_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClassCourseTeachers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Role = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    From = table.Column<DateTime>(type: "date", nullable: false),
                    To = table.Column<DateTime>(type: "date", nullable: true),
                    OpenPrimaryKey = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassCourseTeachers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassCourseTeachers_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassCourseTeachers_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassCourseTeachers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CourseEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassStudentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Source = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    From = table.Column<DateTime>(type: "date", nullable: false),
                    To = table.Column<DateTime>(type: "date", nullable: true),
                    DropReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CarriedFromEnrollmentId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ActiveKey = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseEnrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseEnrollments_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseEnrollments_ClassStudents_ClassStudentId",
                        column: x => x.ClassStudentId,
                        principalTable: "ClassStudents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseEnrollments_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseEnrollments_StudentProfiles_StudentId",
                        column: x => x.StudentId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StudentGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    NameAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    MatchType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    MatchValue = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentGroups_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentGroups_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GroupMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    GroupId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LiveKey = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupMembers_CourseEnrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "CourseEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupMembers_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupMembers_StudentGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "StudentGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupMembers_StudentProfiles_StudentId",
                        column: x => x.StudentId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClassCourses_ClassId_CourseKey",
                table: "ClassCourses",
                columns: new[] { "ClassId", "CourseKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassCourses_CourseId",
                table: "ClassCourses",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassCourses_OrgUnitId",
                table: "ClassCourses",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassCourseTeachers_ClassCourseId_OpenPrimaryKey",
                table: "ClassCourseTeachers",
                columns: new[] { "ClassCourseId", "OpenPrimaryKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassCourseTeachers_OrganizationId_UserId",
                table: "ClassCourseTeachers",
                columns: new[] { "OrganizationId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassCourseTeachers_OrgUnitId",
                table: "ClassCourseTeachers",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassCourseTeachers_UserId",
                table: "ClassCourseTeachers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Classes_AcademicYearId",
                table: "Classes",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_Classes_OrganizationId_AcademicYearId_CodeKey",
                table: "Classes",
                columns: new[] { "OrganizationId", "AcademicYearId", "CodeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Classes_OrganizationId_OrgUnitPath_AcademicYearId",
                table: "Classes",
                columns: new[] { "OrganizationId", "OrgUnitPath", "AcademicYearId" });

            migrationBuilder.CreateIndex(
                name: "IX_Classes_OrgUnitId",
                table: "Classes",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassStudents_AcademicYearId",
                table: "ClassStudents",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassStudents_ClassId_Status",
                table: "ClassStudents",
                columns: new[] { "ClassId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassStudents_OrganizationId_StudentId_ActiveYearKey",
                table: "ClassStudents",
                columns: new[] { "OrganizationId", "StudentId", "ActiveYearKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassStudents_OrgUnitId",
                table: "ClassStudents",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassStudents_StudentId_Status",
                table: "ClassStudents",
                columns: new[] { "StudentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseAllocations_CourseId",
                table: "CourseAllocations",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseAllocations_OrganizationId_OrgUnitPath",
                table: "CourseAllocations",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseAllocations_OrgUnitId_CourseKey",
                table: "CourseAllocations",
                columns: new[] { "OrgUnitId", "CourseKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseEnrollments_ClassCourseId_StudentId_ActiveKey",
                table: "CourseEnrollments",
                columns: new[] { "ClassCourseId", "StudentId", "ActiveKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseEnrollments_ClassStudentId",
                table: "CourseEnrollments",
                column: "ClassStudentId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseEnrollments_OrgUnitId",
                table: "CourseEnrollments",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseEnrollments_StudentId_Status",
                table: "CourseEnrollments",
                columns: new[] { "StudentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseRestrictions_CourseId_Type",
                table: "CourseRestrictions",
                columns: new[] { "CourseId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseRestrictions_OrganizationId_OrgUnitPath",
                table: "CourseRestrictions",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseRestrictions_OrgUnitId",
                table: "CourseRestrictions",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Courses_OrganizationId_CodeKey",
                table: "Courses",
                columns: new[] { "OrganizationId", "CodeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Courses_OrganizationId_OrgUnitPath",
                table: "Courses",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_Courses_OrgUnitId_Status",
                table: "Courses",
                columns: new[] { "OrgUnitId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseSupervisors_CourseId_UserId_LiveKey",
                table: "CourseSupervisors",
                columns: new[] { "CourseId", "UserId", "LiveKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseSupervisors_OrganizationId_UserId",
                table: "CourseSupervisors",
                columns: new[] { "OrganizationId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseSupervisors_OrgUnitId",
                table: "CourseSupervisors",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseSupervisors_UserId",
                table: "CourseSupervisors",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_ClassCourseId_StudentId_LiveKey",
                table: "GroupMembers",
                columns: new[] { "ClassCourseId", "StudentId", "LiveKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_EnrollmentId",
                table: "GroupMembers",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_GroupId",
                table: "GroupMembers",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_OrgUnitId",
                table: "GroupMembers",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_StudentId",
                table: "GroupMembers",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGroups_ClassCourseId",
                table: "StudentGroups",
                column: "ClassCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGroups_OrgUnitId",
                table: "StudentGroups",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentTransfers_FromClassId",
                table: "StudentTransfers",
                column: "FromClassId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentTransfers_OrganizationId_OrgUnitPath",
                table: "StudentTransfers",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentTransfers_OrgUnitId",
                table: "StudentTransfers",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentTransfers_StudentId_EffectiveOn",
                table: "StudentTransfers",
                columns: new[] { "StudentId", "EffectiveOn" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentTransfers_ToClassId",
                table: "StudentTransfers",
                column: "ToClassId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherCourseEligibilities_CourseId",
                table: "TeacherCourseEligibilities",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherCourseEligibilities_OrganizationId_UserId",
                table: "TeacherCourseEligibilities",
                columns: new[] { "OrganizationId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeacherCourseEligibilities_OrgUnitId_UserId_CourseId_LiveKey",
                table: "TeacherCourseEligibilities",
                columns: new[] { "OrgUnitId", "UserId", "CourseId", "LiveKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherCourseEligibilities_UserId",
                table: "TeacherCourseEligibilities",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassCourseTeachers");

            migrationBuilder.DropTable(
                name: "CourseAllocations");

            migrationBuilder.DropTable(
                name: "CourseRestrictions");

            migrationBuilder.DropTable(
                name: "CourseSupervisors");

            migrationBuilder.DropTable(
                name: "GroupMembers");

            migrationBuilder.DropTable(
                name: "StudentTransfers");

            migrationBuilder.DropTable(
                name: "TeacherCourseEligibilities");

            migrationBuilder.DropTable(
                name: "CourseEnrollments");

            migrationBuilder.DropTable(
                name: "StudentGroups");

            migrationBuilder.DropTable(
                name: "ClassStudents");

            migrationBuilder.DropTable(
                name: "ClassCourses");

            migrationBuilder.DropTable(
                name: "Classes");

            migrationBuilder.DropTable(
                name: "Courses");
        }
    }
}
