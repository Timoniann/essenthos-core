using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260924160000_AWitnessNumberIsReadAsTheWord")]
    public partial class AWitnessNumberIsReadAsTheWord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The readers of the critical editions now read these numbers on a cold load. A text is not
            // loaded twice, so the same numbers are read here on the corpus already holding them. Every
            // word is addressed canonically — text, verse of the shared frame, folded form and the number
            // it carried — so a word already read, or one another edition spells otherwise, is left alone
            // and a second run does nothing.
            //
            // Nestle, Tischendorf and Westcott-Hort tag Joda of Luke 3:26 with G2448, the region of
            // Judah, and every text linked to them named him as the town. He is G2455, as Strong's and
            // the Textus Receptus have him; the names the region's number put on the words of his verse
            // go with it, and the Greek namesake pass, which runs on every load, names him from there.
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE witness_number (text text, book int, chapter int, verse int, folded text,
                                                  tagged text, read text) ON COMMIT DROP;
                INSERT INTO witness_number VALUES
                    ('NESTLE1904', 42, 3, 26, 'ιωδα', 'G2448', 'G2455'),
                    ('TISCH', 42, 3, 26, 'ιωδα', 'G2448', 'G2455'),
                    ('WH1881', 42, 3, 26, 'ιωδα', 'G2448', 'G2455');

                DELETE FROM word_entity a
                USING word w, verse_reference r, witness_number n, entity_name named
                WHERE a.word_id = w.id
                  AND r.verse_id = w.verse_id AND r.is_primary
                  AND r.canonical_book = n.book AND r.canonical_chapter = n.chapter AND r.canonical_verse = n.verse
                  AND named.greek_strong_number = n.tagged
                  AND a.entity_id = coalesce(named.aspect_of_entity_id, named.entity_id)
                  AND a.confidence IS NOT NULL
                  AND EXISTS (SELECT 1 FROM word tagged
                              JOIN text t ON t.id = tagged.text_id AND t.slug = n.text
                              JOIN verse_reference tr ON tr.verse_id = tagged.verse_id AND tr.is_primary
                                   AND tr.canonical_book = n.book AND tr.canonical_chapter = n.chapter
                                   AND tr.canonical_verse = n.verse
                              WHERE tagged.normalised_text = n.folded AND tagged.strong_number = n.tagged);

                UPDATE word w SET strong_number = n.read
                FROM text t, verse_reference r, witness_number n
                WHERE t.id = w.text_id AND t.slug = n.text
                  AND r.verse_id = w.verse_id AND r.is_primary
                  AND r.canonical_book = n.book AND r.canonical_chapter = n.chapter AND r.canonical_verse = n.verse
                  AND w.normalised_text = n.folded AND w.strong_number = n.tagged;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to put back: the numbers it replaces are the ones the readers no longer write,
            // and loading the texts again from their sources writes these.
        }
    }
}
