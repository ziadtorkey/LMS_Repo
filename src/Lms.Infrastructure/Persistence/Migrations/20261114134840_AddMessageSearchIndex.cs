using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageSearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // L21: a FULLTEXT index with the ngram parser (two-character tokens) so Arabic and English words are found without word boundaries. The provider has no model
            // support for it, so it is raw SQL and the model snapshot does not know it.
            migrationBuilder.Sql("ALTER TABLE `Messages` ADD FULLTEXT INDEX `FT_Messages_Body` (`Body`) WITH PARSER ngram;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `Messages` DROP INDEX `FT_Messages_Body`;");
        }
    }
}
