using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuizOpenedNotified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OpenedNotifiedAtUtc",
                table: "Quizzes",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpenedNotifiedAtUtc",
                table: "Quizzes");
        }
    }
}
