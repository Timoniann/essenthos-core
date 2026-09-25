using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AVerseIsListedOncePerEntityAndSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A verse listed twice for one entity by one source keeps one row, the undisputed one
            // where there is one.
            migrationBuilder.Sql(
                """
                DELETE FROM entity_verse v
                USING (
                    SELECT id, row_number() OVER (
                        PARTITION BY entity_id, canonical_book, canonical_chapter, canonical_verse, source
                        ORDER BY disputed, id) AS rank
                    FROM entity_verse) ranked
                WHERE ranked.id = v.id AND ranked.rank > 1
                """);

            migrationBuilder.CreateIndex(
                name: "ix_entity_verse_entity_id_canonical_book_canonical_chapter_can",
                table: "entity_verse",
                columns: new[] { "entity_id", "canonical_book", "canonical_chapter", "canonical_verse", "source" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_entity_verse_entity_id_canonical_book_canonical_chapter_can",
                table: "entity_verse");
        }
    }
}
