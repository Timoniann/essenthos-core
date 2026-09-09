using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260909120000_TheNamingGoesToTheHeadOfThePhrase")]
    public partial class TheNamingGoesToTheHeadOfThePhrase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The annotations that landed on the rest of a phrase. A link names a set of words on
            // each side, and the two texts that reach an original through a stated correspondence
            // -- the Berean's own translation tables and the King James's Strong tags -- put a
            // phrase opposite a single original word, because that is what translation does. The
            // carrying step handed the person to every word of that set, so Genesis 11 offered
            // "When", "of", "And" and "lifetime" as words naming Terah, "the" was annotated as a
            // person 7,470 times in the Berean and "of" 6,230.
            //
            // The loader now carries the naming to the set's head and this removes what it would no
            // longer write: 21,873 annotations in the Berean and 17,466 in the King James, taking
            // the share of them sitting on a function word from 45.2% to 3.9% and from 38.3% to
            // 0.5%. Nothing else in the corpus is touched to speak of -- 38 rows in the Ukrainian
            // and one in the Synodal -- because an aligner names one word at a time.
            //
            // The head is the set's last word except where one of its words is a name the
            // encyclopedia records for the entity, which takes the naming from it. Position alone
            // picks the wrong one of two names the source grouped together: the Berean puts
            // "Tahrea and Ahaz" opposite one Hebrew pair, so the last word said Ahaz is Tahrea and
            // left Tahrea unmarked, which reads as a fact where the article read as noise. 217
            // annotations move that way, 216 of them onto a word this same statement would
            // otherwise have deleted, and two of the 217 move onto one word.
            //
            // Rows a person's ruling stands on are removed with the rest, and that is deliberate.
            // A ruling seeds one word of the witness and the carrying is what spread it, so the
            // extra words a manual claim sits on are this defect's own output rather than anybody's
            // judgement -- and the rulings themselves are files under Resources, so what a rebuild
            // writes and what this leaves are the same thing.
            //
            // The head rule is written out rather than read from the loader, because what happened
            // to this database is a fact about it and should not change meaning afterwards because
            // somebody retuned the loader.
            //
            // What a row has to be the head of is a link back to a word the naming started at, and
            // a seeded row is one whose note does not say it was carried. Asking only that some
            // annotated word stand across the link would keep every one of these: the two English
            // texts are linked to each other as well as to the witness, and both ends of such a
            // link carry the same wrong annotation, which would then vouch for itself.
            //
            // The temporary table needs a name no earlier migration has used: every pending
            // migration runs inside one transaction, and ON COMMIT DROP waits for that transaction
            // rather than for the migration.
            //
            // Trigram similarity is how alike two spellings are, and a database the corpus is built
            // into fresh has no pg_trgm in it. It is a contrib module of Postgres itself, under the
            // same licence, and marked trusted, so the owner of the database can create it.
            migrationBuilder.Sql(
                """
                CREATE EXTENSION IF NOT EXISTS pg_trgm;

                CREATE TEMP TABLE past_the_head ON COMMIT DROP AS
                SELECT a.id AS annotation
                FROM word_entity a
                WHERE a.note LIKE 'through %'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM link_word mine
                      JOIN link_word other
                           ON other.link_id = mine.link_id AND other.side <> mine.side
                      JOIN word_entity seed
                           ON seed.word_id = other.word_id AND seed.entity_id = a.entity_id
                              AND seed.note NOT LIKE 'through %'
                      WHERE mine.word_id = a.word_id
                        AND a.word_id = (
                            SELECT peer.word_id
                            FROM (
                                SELECT lw.word_id, hw.verse_id, hw.position,
                                       hw.text ~* '[''’]s?$'
                                           AND row_number() OVER (
                                               ORDER BY hw.verse_id DESC, hw.position DESC) = 2
                                           AS closes,
                                       coalesce((
                                           SELECT max(similarity(
                                               lower(regexp_replace(hw.text, '[^[:alnum:]]', '', 'g')),
                                               lower(known.spelling)))
                                           FROM (
                                               SELECT e.name AS spelling FROM entity e
                                               WHERE e.id = a.entity_id
                                               UNION ALL
                                               SELECT regexp_replace(e.slug, '-[0-9]+$', '')
                                               FROM entity e WHERE e.id = a.entity_id
                                               UNION ALL
                                               SELECT n.label FROM entity_name n
                                               WHERE n.entity_id = a.entity_id
                                                 AND coalesce(n.kind, '')
                                                     NOT IN ('title', 'description')
                                               UNION ALL
                                               SELECT n.hebrew_transliterated FROM entity_name n
                                               WHERE n.entity_id = a.entity_id
                                                 AND coalesce(n.kind, '')
                                                     NOT IN ('title', 'description')
                                               UNION ALL
                                               SELECT n.greek_transliterated FROM entity_name n
                                               WHERE n.entity_id = a.entity_id
                                                 AND coalesce(n.kind, '')
                                                     NOT IN ('title', 'description')) known
                                           WHERE known.spelling ~ '^[[:alnum:]]+$'), 0) AS named
                                FROM link_word lw
                                JOIN word hw ON hw.id = lw.word_id
                                WHERE lw.link_id = mine.link_id AND lw.side = mine.side) peer
                            ORDER BY CASE WHEN peer.named >= 0.5 THEN peer.named ELSE 0 END DESC,
                                     peer.closes DESC, peer.verse_id DESC, peer.position DESC
                            LIMIT 1));

                DELETE FROM word_entity WHERE id IN (SELECT annotation FROM past_the_head);
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
