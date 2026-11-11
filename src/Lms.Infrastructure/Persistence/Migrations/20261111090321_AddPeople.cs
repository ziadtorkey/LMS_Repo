using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPeople : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GuardianProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    NameAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NameArKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NameEnKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Mobile = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    NationalId = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianProfiles", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PersonStatusChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ProfileId = table.Column<Guid>(type: "char(36)", nullable: false),
                    From = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    To = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    ChangedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonStatusChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonStatusChanges_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StaffProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    NameAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NameArKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NameEnKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Mobile = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    NationalId = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    JobTitleAr = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true),
                    JobTitleEn = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true),
                    PhotoFileId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ExpiresOn = table.Column<DateTime>(type: "date", nullable: true),
                    ExpiryNoticeSentOn = table.Column<DateTime>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffProfiles_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StudentProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    StudentCode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NameArKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NameEnKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    NicknameAr = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    NicknameEn = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "date", nullable: false),
                    Gender = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Religion = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Nationality = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    NationalId = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    Mobile = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    PhotoFileId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    FeatureFlagOverridesJson = table.Column<string>(type: "json", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentProfiles_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StudentGuardians",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400)", nullable: false),
                    StudentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GuardianId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Relation = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    IsPrimary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentGuardians", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentGuardians_GuardianProfiles_GuardianId",
                        column: x => x.GuardianId,
                        principalTable: "GuardianProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentGuardians_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentGuardians_StudentProfiles_StudentId",
                        column: x => x.StudentId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GuardianProfiles_OrganizationId_Mobile",
                table: "GuardianProfiles",
                columns: new[] { "OrganizationId", "Mobile" });

            migrationBuilder.CreateIndex(
                name: "IX_GuardianProfiles_OrganizationId_NationalId",
                table: "GuardianProfiles",
                columns: new[] { "OrganizationId", "NationalId" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonStatusChanges_OrganizationId_Kind_ProfileId_ChangedAt",
                table: "PersonStatusChanges",
                columns: new[] { "OrganizationId", "Kind", "ProfileId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonStatusChanges_OrganizationId_OrgUnitPath",
                table: "PersonStatusChanges",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonStatusChanges_OrgUnitId",
                table: "PersonStatusChanges",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffProfiles_OrganizationId_Email",
                table: "StaffProfiles",
                columns: new[] { "OrganizationId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffProfiles_OrganizationId_Mobile",
                table: "StaffProfiles",
                columns: new[] { "OrganizationId", "Mobile" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffProfiles_OrganizationId_NationalId",
                table: "StaffProfiles",
                columns: new[] { "OrganizationId", "NationalId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffProfiles_OrganizationId_OrgUnitPath_NameEnKey",
                table: "StaffProfiles",
                columns: new[] { "OrganizationId", "OrgUnitPath", "NameEnKey" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffProfiles_OrganizationId_Status_ExpiresOn",
                table: "StaffProfiles",
                columns: new[] { "OrganizationId", "Status", "ExpiresOn" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffProfiles_OrganizationId_UserId",
                table: "StaffProfiles",
                columns: new[] { "OrganizationId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffProfiles_OrgUnitId",
                table: "StaffProfiles",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffProfiles_UserId",
                table: "StaffProfiles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGuardians_GuardianId",
                table: "StudentGuardians",
                column: "GuardianId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGuardians_OrganizationId_OrgUnitPath",
                table: "StudentGuardians",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentGuardians_OrgUnitId",
                table: "StudentGuardians",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGuardians_StudentId",
                table: "StudentGuardians",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentProfiles_OrganizationId_NameArKey_DateOfBirth",
                table: "StudentProfiles",
                columns: new[] { "OrganizationId", "NameArKey", "DateOfBirth" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentProfiles_OrganizationId_NameEnKey_DateOfBirth",
                table: "StudentProfiles",
                columns: new[] { "OrganizationId", "NameEnKey", "DateOfBirth" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentProfiles_OrganizationId_NationalId",
                table: "StudentProfiles",
                columns: new[] { "OrganizationId", "NationalId" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentProfiles_OrganizationId_OrgUnitPath_NameEnKey",
                table: "StudentProfiles",
                columns: new[] { "OrganizationId", "OrgUnitPath", "NameEnKey" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentProfiles_OrganizationId_StudentCode",
                table: "StudentProfiles",
                columns: new[] { "OrganizationId", "StudentCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentProfiles_OrgUnitId",
                table: "StudentProfiles",
                column: "OrgUnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonStatusChanges");

            migrationBuilder.DropTable(
                name: "StaffProfiles");

            migrationBuilder.DropTable(
                name: "StudentGuardians");

            migrationBuilder.DropTable(
                name: "GuardianProfiles");

            migrationBuilder.DropTable(
                name: "StudentProfiles");
        }
    }
}
