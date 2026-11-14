using Lms.Infrastructure.Announcements;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveCourseBoardToAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data only (M6 Task 17, L15): the board rows become announcements with the same ids. The old table stays one more release.
            foreach (var sql in CourseBoardMigration.Up)
            {
                migrationBuilder.Sql(sql);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in CourseBoardMigration.Down)
            {
                migrationBuilder.Sql(sql);
            }
        }
    }
}
