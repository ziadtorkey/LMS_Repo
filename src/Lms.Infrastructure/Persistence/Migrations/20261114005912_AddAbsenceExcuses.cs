using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAbsenceExcuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OpenedNotifiedAtUtc",
                table: "Quizzes",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AbsenceExcuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudentProfileId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Reason = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ReasonText = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    DateFrom = table.Column<DateTime>(type: "date", nullable: true),
                    DateTo = table.Column<DateTime>(type: "date", nullable: true),
                    AttachmentFileId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    DecisionComment = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    ReviewerUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EscalatedToUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EscalatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    EscalationComment = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    EscalatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
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
                    table.PrimaryKey("PK_AbsenceExcuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbsenceExcuses_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AbsenceExcuses_StoredFiles_AttachmentFileId",
                        column: x => x.AttachmentFileId,
                        principalTable: "StoredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AbsenceExcuses_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CourseAnnouncements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    BodyHtml = table.Column<string>(type: "longtext", nullable: false),
                    IsPinned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "char(36)", nullable: false),
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
                    table.PrimaryKey("PK_CourseAnnouncements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseAnnouncements_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseAnnouncements_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ForumThreads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    IsPinned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsLocked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LastActivityAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
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
                    table.PrimaryKey("PK_ForumThreads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForumThreads_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ForumThreads_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

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

            migrationBuilder.CreateTable(
                name: "AbsenceExcuseSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    AbsenceExcuseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AttendanceSessionId = table.Column<Guid>(type: "char(36)", nullable: false),
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
                    table.PrimaryKey("PK_AbsenceExcuseSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbsenceExcuseSessions_AbsenceExcuses_AbsenceExcuseId",
                        column: x => x.AbsenceExcuseId,
                        principalTable: "AbsenceExcuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AbsenceExcuseSessions_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ForumPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ThreadId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    BodyHtml = table.Column<string>(type: "longtext", nullable: false),
                    IsOpeningPost = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EditedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
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
                    table.PrimaryKey("PK_ForumPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForumPosts_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ForumPosts_ForumThreads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "ForumThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ForumPosts_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ForumPostAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    PostId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AssetId = table.Column<Guid>(type: "char(36)", nullable: false),
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
                    table.PrimaryKey("PK_ForumPostAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForumPostAttachments_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ForumPostAttachments_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ForumPostAttachments_ForumPosts_PostId",
                        column: x => x.PostId,
                        principalTable: "ForumPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ForumPostAttachments_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceExcuses_AttachmentFileId",
                table: "AbsenceExcuses",
                column: "AttachmentFileId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceExcuses_OrganizationId_OrgUnitPath_Status",
                table: "AbsenceExcuses",
                columns: new[] { "OrganizationId", "OrgUnitPath", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceExcuses_OrgUnitId",
                table: "AbsenceExcuses",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceExcuses_StudentProfileId_Status",
                table: "AbsenceExcuses",
                columns: new[] { "StudentProfileId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceExcuses_SubmittedByUserId",
                table: "AbsenceExcuses",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceExcuseSessions_AbsenceExcuseId_AttendanceSessionId",
                table: "AbsenceExcuseSessions",
                columns: new[] { "AbsenceExcuseId", "AttendanceSessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceExcuseSessions_AttendanceSessionId",
                table: "AbsenceExcuseSessions",
                column: "AttendanceSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceExcuseSessions_OrgUnitId",
                table: "AbsenceExcuseSessions",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseAnnouncements_ClassCourseId_IsPinned_CreatedAt",
                table: "CourseAnnouncements",
                columns: new[] { "ClassCourseId", "IsPinned", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseAnnouncements_OrgUnitId",
                table: "CourseAnnouncements",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ForumPostAttachments_AssetId",
                table: "ForumPostAttachments",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ForumPostAttachments_ClassCourseId",
                table: "ForumPostAttachments",
                column: "ClassCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_ForumPostAttachments_OrgUnitId",
                table: "ForumPostAttachments",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ForumPostAttachments_PostId",
                table: "ForumPostAttachments",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_ForumPosts_ClassCourseId",
                table: "ForumPosts",
                column: "ClassCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_ForumPosts_OrgUnitId",
                table: "ForumPosts",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ForumPosts_ThreadId_CreatedAt",
                table: "ForumPosts",
                columns: new[] { "ThreadId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ForumThreads_ClassCourseId_IsPinned_LastActivityAtUtc",
                table: "ForumThreads",
                columns: new[] { "ClassCourseId", "IsPinned", "LastActivityAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ForumThreads_OrgUnitId",
                table: "ForumThreads",
                column: "OrgUnitId");

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
                name: "AbsenceExcuseSessions");

            migrationBuilder.DropTable(
                name: "CourseAnnouncements");

            migrationBuilder.DropTable(
                name: "ForumPostAttachments");

            migrationBuilder.DropTable(
                name: "PackageUploads");

            migrationBuilder.DropTable(
                name: "AbsenceExcuses");

            migrationBuilder.DropTable(
                name: "ForumPosts");

            migrationBuilder.DropTable(
                name: "ForumThreads");

            migrationBuilder.DropColumn(
                name: "OpenedNotifiedAtUtc",
                table: "Quizzes");
        }
    }
}
