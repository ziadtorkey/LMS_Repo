using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudentChangeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400)", nullable: false),
                    StudentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    PendingStudentId = table.Column<Guid>(type: "char(36)", nullable: true),
                    RequestedBy = table.Column<Guid>(type: "char(36)", nullable: false),
                    ChangesJson = table.Column<string>(type: "json", nullable: false),
                    Note = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    DecidedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DecisionComment = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentChangeRequests_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentChangeRequests_StudentProfiles_StudentId",
                        column: x => x.StudentId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_StudentChangeRequests_OrganizationId_OrgUnitPath_Status",
                table: "StudentChangeRequests",
                columns: new[] { "OrganizationId", "OrgUnitPath", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentChangeRequests_OrgUnitId",
                table: "StudentChangeRequests",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentChangeRequests_PendingStudentId",
                table: "StudentChangeRequests",
                column: "PendingStudentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentChangeRequests_StudentId",
                table: "StudentChangeRequests",
                column: "StudentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentChangeRequests");
        }
    }
}
