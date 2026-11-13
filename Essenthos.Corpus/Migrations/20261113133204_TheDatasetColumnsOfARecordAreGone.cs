using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheDatasetColumnsOfARecordAreGone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A database the record files were loaded into lost these columns at the load, so each goes only where it is.
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_entity_source_id;
                ALTER TABLE entity DROP COLUMN IF EXISTS source, DROP COLUMN IF EXISTS source_id, DROP COLUMN IF EXISTS open_bible_id;
                ALTER TABLE merged_record DROP COLUMN IF EXISTS record_source, DROP COLUMN IF EXISTS record_source_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE entity ADD COLUMN IF NOT EXISTS source text NOT NULL DEFAULT '',
                                   ADD COLUMN IF NOT EXISTS source_id text NOT NULL DEFAULT '',
                                   ADD COLUMN IF NOT EXISTS open_bible_id text;
                ALTER TABLE merged_record ADD COLUMN IF NOT EXISTS record_source text NOT NULL DEFAULT '',
                                          ADD COLUMN IF NOT EXISTS record_source_id text NOT NULL DEFAULT '';
                CREATE INDEX IF NOT EXISTS ix_entity_source_id ON entity (source_id);
                """);
        }
    }
}
