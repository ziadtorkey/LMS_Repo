using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationShortCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShortCode",
                table: "Organizations",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // D47 backfill: the lower-cased root unit code ('_' -> '-', cut to 17 so a '-n' suffix fits), '-2', '-3'... on a clash.
            // A client without a usable root code (none, or shorter than 2) falls back to 'c' + the first 12 hex digits of its id.
            migrationBuilder.Sql(@"
UPDATE `Organizations` o
JOIN (
    SELECT b.Id,
           CASE WHEN ROW_NUMBER() OVER (PARTITION BY b.Base ORDER BY b.CreatedAt, b.Id) = 1
                THEN b.Base
                ELSE CONCAT(b.Base, '-', ROW_NUMBER() OVER (PARTITION BY b.Base ORDER BY b.CreatedAt, b.Id))
           END AS Code
    FROM (
        SELECT o2.Id, o2.CreatedAt,
               CASE WHEN CHAR_LENGTH(x.Base0) >= 2 THEN x.Base0 ELSE CONCAT('c', LEFT(REPLACE(o2.Id, '-', ''), 12)) END AS Base
        FROM `Organizations` o2
        LEFT JOIN (
            SELECT u.OrganizationId, LEFT(REPLACE(LOWER(u.Code), '_', '-'), 17) AS Base0
            FROM `OrgUnits` u WHERE u.ParentId IS NULL
        ) x ON x.OrganizationId = o2.Id
    ) b
) n ON n.Id = o.Id
SET o.ShortCode = n.Code;");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_ShortCode",
                table: "Organizations",
                column: "ShortCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Organizations_ShortCode",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ShortCode",
                table: "Organizations");
        }
    }
}
