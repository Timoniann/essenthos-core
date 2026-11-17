using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class StrongPagesReadPairCountsBookSumsAndPlacesTheBuildStored : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "strong_book_words",
                columns: table => new
                {
                    witness_id = table.Column<int>(type: "integer", nullable: false),
                    book = table.Column<int>(type: "integer", nullable: false),
                    words = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strong_book_words", x => new { x.witness_id, x.book });
                    table.ForeignKey(
                        name: "fk_strong_book_words_texts_witness_id",
                        column: x => x.witness_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "The words of one book of an edition carrying any Strong number, the ETCBC prefix morphemes (H9...) left out: the Strong map's denominator. Summed from strong_book by the build beside it.");

            migrationBuilder.CreateTable(
                name: "strong_occurrence",
                columns: table => new
                {
                    strong_number = table.Column<string>(type: "text", nullable: false),
                    corpus = table.Column<string>(type: "text", nullable: false),
                    book = table.Column<int>(type: "integer", nullable: false),
                    chapter = table.Column<int>(type: "integer", nullable: false),
                    verse = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    word_id = table.Column<long>(type: "bigint", nullable: false),
                    text_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strong_occurrence", x => new { x.strong_number, x.corpus, x.book, x.chapter, x.verse, x.position, x.word_id });
                },
                comment: "Every word carrying a Strong number, keyed in list order: the text's slug, the frame address of its verse's primary reference, its position, its id. Written by the build from word and verse_reference.");

            migrationBuilder.CreateTable(
                name: "text_book_linked",
                columns: table => new
                {
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    book = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_text_book_linked", x => new { x.text_id, x.book });
                    table.ForeignKey(
                        name: "fk_text_book_linked_text_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "The books in which some link of a text renders some numbered word of the edition it is counted over: the books of strong_book_reach for any number, written by the build beside it.");

            migrationBuilder.CreateTable(
                name: "text_pair_links",
                columns: table => new
                {
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    edition_id = table.Column<int>(type: "integer", nullable: false),
                    links = table.Column<int>(type: "integer", nullable: false),
                    numbered = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_text_pair_links", x => new { x.text_id, x.edition_id });
                    table.CheckConstraint("ck_text_pair_links_links", "links > 0");
                    table.ForeignKey(
                        name: "fk_text_pair_links_texts_edition_id",
                        column: x => x.edition_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_text_pair_links_texts_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "How many links join a text to an edition that is not a translation, both ways, and whether any word of the edition carries a Strong number: what a count of the text's renderings is counted over is chosen by. Written by the build from link.");

            migrationBuilder.CreateIndex(
                name: "ix_text_pair_links_edition_id",
                table: "text_pair_links",
                column: "edition_id");

            // Every row follows from what a built database already holds, written as the build writes it, so a database
            // migrated without a new build answers as one built after it.
            migrationBuilder.Sql("""
                INSERT INTO text_book_linked (text_id, book)
                SELECT DISTINCT text_id, book FROM strong_book_reach;

                INSERT INTO strong_book_words (witness_id, book, words)
                SELECT witness_id, book, sum(occurrences) FROM strong_book
                WHERE strong_number NOT LIKE 'H9%'
                GROUP BY witness_id, book;

                INSERT INTO text_pair_links (text_id, edition_id, links, numbered)
                SELECT t.id, e.id, c.links,
                       EXISTS (SELECT 1 FROM word w WHERE w.text_id = e.id AND w.strong_number IS NOT NULL)
                FROM text t
                JOIN text e ON e.id <> t.id AND e.kind <> 'translation'
                CROSS JOIN LATERAL (
                    SELECT (SELECT count(*) FROM link l WHERE l.from_text_id = t.id AND l.to_text_id = e.id)
                         + (SELECT count(*) FROM link l WHERE l.from_text_id = e.id AND l.to_text_id = t.id) AS links
                ) c
                WHERE c.links > 0;

                INSERT INTO strong_occurrence (strong_number, corpus, book, chapter, verse, position, word_id, text_id)
                SELECT w.strong_number, t.slug, r.canonical_book, r.canonical_chapter, r.canonical_verse, w.position, w.id, w.text_id
                FROM word w
                JOIN text t ON t.id = w.text_id
                JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
                WHERE w.strong_number IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strong_book_words");

            migrationBuilder.DropTable(
                name: "strong_occurrence");

            migrationBuilder.DropTable(
                name: "text_book_linked");

            migrationBuilder.DropTable(
                name: "text_pair_links");
        }
    }
}
