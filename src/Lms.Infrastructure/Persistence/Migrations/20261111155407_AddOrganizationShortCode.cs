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
            // A client without a usable root code (none, or shorter than 2) falls back to 'c' + the last 12 hex digits of its id.
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
               CASE WHEN CHAR_LENGTH(x.Base0) >= 2 THEN x.Base0 ELSE CONCAT('c', RIGHT(REPLACE(o2.Id, '-', ''), 12)) END AS Base
        FROM `Organizations` o2
        LEFT JOIN (
            SELECT u.OrganizationId, LEFT(REPLACE(LOWER(u.Code), '_', '-'), 17) AS Base0
            FROM `OrgUnits` u WHERE u.ParentId IS NULL
        ) x ON x.OrganizationId = o2.Id
    ) b
) n ON n.Id = o.Id
SET o.ShortCode = n.Code;");

            // The numbering above is per base code, so a client whose own code is 'sch-2' can still meet the second 'sch'. Any code
            // that is still shared keeps its first owner; the others move to the id-based fallback, so the unique index always holds.
            migrationBuilder.Sql(@"
UPDATE `Organizations` o
JOIN (
    SELECT Id, ROW_NUMBER() OVER (PARTITION BY ShortCode ORDER BY CreatedAt, Id) AS Rn FROM `Organizations`
) d ON d.Id = o.Id
SET o.ShortCode = CONCAT('c', RIGHT(REPLACE(o.Id, '-', ''), 12))
WHERE d.Rn > 1;");

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
