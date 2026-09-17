using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TextusReceptusNotesAreNotWords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Four words of the Textus Receptus that are not words, and one word that lost its parse
            // to them.
            //
            // Robinson's composite writes two things inside a variant group that the reader took for
            // text. "(23:14)" is the other edition's number for the verse, written where the two
            // divide it differently; the reader skipped it at the top level of a verse and not
            // inside a group, so Stephanus's Matthew 23:13 opened with "(23:14)" and 23:14 with
            // "(23:13)". And "barnaba 921 | {N-GSM} | {N-DSM} |" is one word parsed two ways --
            // Stephanus reads Barnabas as a genitive, Scrivener as a dative -- which came out as a
            // word whose letters are "{N-GSM}", leaving Barnabas himself with no parse at all.
            //
            // A migration rather than a reload: 783,805 links reach these two editions, and
            // reloading them to remove four rows would cascade every one of those away.
            //
            // Two links stood on nothing but a phantom and go with them. Two more name the phantom
            // alongside real words and keep those, which is why the delete is of link_word rows and
            // only then of the links left with an empty side -- a link is a claim about a set of
            // words on each side, and a side with no words in it is not a narrower claim, it is no
            // claim.
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE phantom ON COMMIT DROP AS
                SELECT w.id, w.verse_id
                FROM word w
                JOIN text t ON t.id = w.text_id
                WHERE t.slug IN ('scrivener1894', 'stephanus1550')
                  AND (w.text ~ '^\(\d+:\d+\)$' OR w.text ~ '^\{[A-Z0-9-]+\}$');

                -- The parse the group was offering, given to the word it describes. Both editions
                -- carry the same word at the same place, so each takes its own side's reading.
                UPDATE word target
                SET morphology = jsonb_set(
                        coalesce(target.morphology, '{}'::jsonb), '{robinson}',
                        to_jsonb(trim(both '{}' from tag.text)))
                FROM word tag, phantom p
                WHERE tag.id = p.id
                  AND tag.text ~ '^\{[A-Z0-9-]+\}$'
                  AND target.verse_id = tag.verse_id
                  AND target.text_id = tag.text_id
                  AND target.position = tag.position - 1;

                DELETE FROM link_word lw USING phantom p WHERE lw.word_id = p.id;

                DELETE FROM link l
                WHERE l.id IN (
                    SELECT id FROM link
                    EXCEPT
                    SELECT link_id FROM link_word GROUP BY link_id HAVING count(DISTINCT side) = 2);

                DELETE FROM word w USING phantom p WHERE w.id = p.id;

                -- The positions of a verse are 1..n and read as an order; a gap left where a word
                -- was removed would make the count and the last position disagree, which is the
                -- kind of quiet inconsistency the integrity sweep is for.
                WITH renumbered AS (
                    SELECT w.id, row_number() OVER (PARTITION BY w.verse_id ORDER BY w.position) AS n
                    FROM word w
                    WHERE w.verse_id IN (SELECT DISTINCT verse_id FROM phantom))
                UPDATE word w SET position = r.n FROM renumbered r
                WHERE w.id = r.id AND w.position <> r.n
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible. Putting a versification note and a morphology tag back into the text
            // as words is not a state worth being able to return to.
        }
    }
}
