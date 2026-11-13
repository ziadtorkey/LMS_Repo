using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuizzes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Quizzes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LessonKey = table.Column<Guid>(type: "char(36)", nullable: false),
                    ModuleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ModuleKey = table.Column<Guid>(type: "char(36)", nullable: true),
                    DescriptionAr = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    DescriptionEn = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    TimeLimitMinutes = table.Column<int>(type: "int", nullable: true),
                    MaxAttempts = table.Column<int>(type: "int", nullable: true),
                    GradingMethod = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ShuffleQuestions = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ShuffleOptions = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    OpensAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ClosesAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReviewPolicy = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    DrawCount = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_Quizzes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Quizzes_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Quizzes_LessonModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "LessonModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Quizzes_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QuizAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400)", nullable: false),
                    QuizId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Score = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    MaxScore = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    HasPendingEssays = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizAttempts_ClassCourses_ClassCourseId",
                        column: x => x.ClassCourseId,
                        principalTable: "ClassCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuizAttempts_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuizAttempts_Quizzes_QuizId",
                        column: x => x.QuizId,
                        principalTable: "Quizzes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuizAttempts_StudentProfiles_StudentId",
                        column: x => x.StudentId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QuizQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    QuizId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    Points = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_QuizQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizQuestions_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuizQuestions_Quizzes_QuizId",
                        column: x => x.QuizId,
                        principalTable: "Quizzes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QuizAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OrgUnitPath = table.Column<string>(type: "varchar(400)", nullable: false),
                    AttemptId = table.Column<Guid>(type: "char(36)", nullable: false),
                    QuizId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClassCourseId = table.Column<Guid>(type: "char(36)", nullable: false),
                    QuestionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    OptionOrder = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    SelectedOptionIds = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    Text = table.Column<string>(type: "longtext", nullable: true),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    AwardedPoints = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    Feedback = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizAnswers_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuizAnswers_QuizAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "QuizAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuizAnswers_QuizQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "QuizQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QuizOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    QuestionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    QuizId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    IsCorrect = table.Column<bool>(type: "tinyint(1)", nullable: false),
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
                    table.PrimaryKey("PK_QuizOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizOptions_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuizOptions_QuizQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "QuizQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAnswers_AttemptId_QuestionId",
                table: "QuizAnswers",
                columns: new[] { "AttemptId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuizAnswers_OrganizationId_OrgUnitPath",
                table: "QuizAnswers",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_QuizAnswers_OrgUnitId",
                table: "QuizAnswers",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAnswers_QuestionId",
                table: "QuizAnswers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAnswers_QuizId",
                table: "QuizAnswers",
                column: "QuizId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_ClassCourseId_StudentId",
                table: "QuizAttempts",
                columns: new[] { "ClassCourseId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_OrganizationId_OrgUnitPath",
                table: "QuizAttempts",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_OrgUnitId",
                table: "QuizAttempts",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_QuizId_StudentId_Number",
                table: "QuizAttempts",
                columns: new[] { "QuizId", "StudentId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_Status_EndsAtUtc",
                table: "QuizAttempts",
                columns: new[] { "Status", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_StudentId",
                table: "QuizAttempts",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizOptions_OrganizationId_OrgUnitPath",
                table: "QuizOptions",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_QuizOptions_OrgUnitId",
                table: "QuizOptions",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizOptions_QuestionId_Order",
                table: "QuizOptions",
                columns: new[] { "QuestionId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_QuizOptions_QuizId",
                table: "QuizOptions",
                column: "QuizId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizQuestions_OrganizationId_OrgUnitPath",
                table: "QuizQuestions",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_QuizQuestions_OrgUnitId",
                table: "QuizQuestions",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizQuestions_QuizId_Order",
                table: "QuizQuestions",
                columns: new[] { "QuizId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_Quizzes_ClassCourseId_LessonKey",
                table: "Quizzes",
                columns: new[] { "ClassCourseId", "LessonKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Quizzes_ModuleId",
                table: "Quizzes",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Quizzes_ModuleKey",
                table: "Quizzes",
                column: "ModuleKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quizzes_OrganizationId_OrgUnitPath",
                table: "Quizzes",
                columns: new[] { "OrganizationId", "OrgUnitPath" });

            migrationBuilder.CreateIndex(
                name: "IX_Quizzes_OrgUnitId",
                table: "Quizzes",
                column: "OrgUnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuizAnswers");

            migrationBuilder.DropTable(
                name: "QuizOptions");

            migrationBuilder.DropTable(
                name: "QuizAttempts");

            migrationBuilder.DropTable(
                name: "QuizQuestions");

            migrationBuilder.DropTable(
                name: "Quizzes");
        }
    }
}
