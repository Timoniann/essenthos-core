using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AFaintLinkGivesWayToTheRenderingBesideIt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The annotations an aligner put on a word it had nowhere else to send. Judges 4:12
            // underlined the Ukrainian зійшов and the Russian взошел -- both of which render the
            // verb "went up" -- as the man Abinoam, at 0.52 and 0.49, in a verse that had already
            // said Abinoam confidently one word earlier. Neither word has any link to עלה at all:
            // the aligner did not add a second correspondence, it missed the right one and
            // attached the word to the only thing it could score.
            //
            // The shape is the second word. Where a Hebrew name already reaches this verse of this
            // text by a firm link, a faint one to the same name explains nothing that is not
            // already explained, and 1,022 annotations are that -- 349 in Brenton's Septuagint, 313
            // in the Synodal, 311 in the Ukrainian, 49 in the Berean, none in the King James, BHSA
            // or the Samaritan, whose links are stated and carry nothing faint.
            //
            // A floor would have been the obvious rule and the wrong one. 2,221 annotations sit
            // below 0.70 and a great many of them are true: Шевна is Shebnah at 0.69, Ісавового
            // is Esau at 0.66, and the 250 places the Ukrainian Бог stands for אדני run from 0.49
            // to 0.59. Cutting at 0.70 would leave 645 verse-and-entity pairs with no account at
            // all of who is named in them. This leaves none: it only ever removes a second claim
            // where a better one is already standing beside it, in the same verse of the same
            // text.
            //
            // The thresholds are written out rather than read from the loader on purpose. What
            // happened to this database is a fact about it, and it should not change meaning
            // afterwards because somebody retuned a constant.
            //
            // An annotation is removed only when every reach it has is outweighed. A word can be
            // reached from two Hebrew words that agree about who is named, and one of those reaches
            // being a leftover is no reason to lose the other; the loader keeps the strongest of
            // what survives, and this has to leave it the same rows to keep.
            //
            // The temporary table needs a name no earlier migration has used, because every pending
            // migration runs inside one transaction and `ON COMMIT DROP` waits for that transaction
            // rather than for the migration.
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE outweighed ON COMMIT DROP AS
                WITH witness AS (
                    SELECT id FROM text WHERE slug = 'bhsa'
                ),
                reached AS (
                    SELECT a.id AS annotation,
                           w.text_id,
                           w.verse_id,
                           hebrew.id AS through,
                           coalesce(l.confidence, 1.0) AS carried
                    FROM word_entity a
                    JOIN word w ON w.id = a.word_id
                    JOIN link_word mine ON mine.word_id = a.word_id
                    JOIN link l ON l.id = mine.link_id
                    JOIN link_word other ON other.link_id = l.id AND other.side <> mine.side
                    JOIN word hebrew ON hebrew.id = other.word_id
                         AND hebrew.text_id = (SELECT id FROM witness)
                ),
                rendered AS (
                    SELECT hebrew.id AS through,
                           w.text_id,
                           w.verse_id,
                           max(coalesce(l.confidence, 1.0)) AS best
                    FROM link l
                    JOIN link_word mine ON mine.link_id = l.id
                    JOIN word hebrew ON hebrew.id = mine.word_id
                         AND hebrew.text_id = (SELECT id FROM witness)
                    JOIN link_word other ON other.link_id = l.id AND other.side <> mine.side
                    JOIN word w ON w.id = other.word_id
                    GROUP BY 1, 2, 3
                )
                SELECT r.annotation
                FROM reached r
                JOIN rendered d ON d.through = r.through
                     AND d.text_id = r.text_id AND d.verse_id = r.verse_id
                GROUP BY r.annotation
                HAVING count(*) FILTER (WHERE r.carried >= 0.70 OR d.best < 0.90) = 0;

                DELETE FROM word_entity WHERE id IN (SELECT annotation FROM outweighed);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible. What was deleted is what the loader now declines to write, so the
            // state to return to is the one the corpus is rebuilt into, not one kept here.
        }
    }
}
