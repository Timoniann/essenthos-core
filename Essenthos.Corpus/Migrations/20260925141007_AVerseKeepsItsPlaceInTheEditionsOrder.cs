using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AVerseKeepsItsPlaceInTheEditionsOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "sequence",
                table: "verse",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // The corpus loader writes a chapter's verses in the order the edition gives them, so a
            // verse's row id already carries its place. The exception is a verse added after the
            // load — a slot holding only a note, which has no words and was appended to the end of
            // its table — and that one takes the place its number gives it instead.
            migrationBuilder.Sql(
                """
                WITH worded AS (
                    SELECT v.id, v.chapter_id, v.number, v.label,
                           EXISTS (SELECT 1 FROM word w WHERE w.verse_id = v.id) AS worded
                    FROM verse v),
                keyed AS (
                    SELECT id, chapter_id, number, label, NOT worded AS wordless,
                           CASE WHEN worded THEN id
                                ELSE coalesce(max(id) FILTER (WHERE worded) OVER (
                                         PARTITION BY chapter_id ORDER BY number, label
                                         ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0)
                           END AS place
                    FROM worded),
                ranked AS (
                    SELECT id, row_number() OVER (
                        PARTITION BY chapter_id ORDER BY place, wordless, number, label) AS sequence
                    FROM keyed)
                UPDATE verse v SET sequence = ranked.sequence FROM ranked WHERE v.id = ranked.id
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sequence",
                table: "verse");
        }
    }
}
