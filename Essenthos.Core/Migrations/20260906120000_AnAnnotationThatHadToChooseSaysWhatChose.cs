using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AnAnnotationThatHadToChooseSaysWhatChose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // `strong-number` is the method that means no judgement was required: the number named
            // one record in the encyclopedia and the occurrence resolved without anybody weighing
            // anything. Writing the peoples made that false for 12,258 annotations, in two
            // different ways, and neither of them is a wrong answer -- it is a right answer
            // declared as the wrong kind of claim, which is the one thing a reader cannot check.
            //
            // 12,096 of them are the gentilic path, and its own source string says what decided
            // them: the gentilic Strong's Dictionary derives. A gentilic shares its number with
            // whoever it is derived from -- H6430 is Goliath as well as the Philistines, H3778 is
            // Chaldea as well as the Chaldeans -- so the number alone leaves the answer open and
            // the word's form is what closes it. `lexical` is the method for a conclusion the form
            // reached, and the reader is shown it as "by the form of the word".
            //
            // 162 are the name resolution, on 17 Hebrew words and the 145 words of six other texts
            // the annotation travelled to. Every one is right and every one was decided by BHSA's
            // analysis of the occurrence rather than by the number: fifteen of Chaldea, where the
            // toponym is a separate lexeme from the gentilic and is marked singular against the
            // gentilic's plural, two of them carrying the directional he that only a place takes;
            // Arodi in the list of Gad's sons and Machbannai among David's Gadites, both marked as
            // a man's name against the people Strong derives from each. The marking was not
            // agreeing with the only answer there was. It was choosing, and the row now says so.
            //
            // The strings are written out rather than read from the loaders because what happened
            // to this database is a fact about it, and it must not change meaning afterwards
            // because somebody reworded a constant.
            //
            // The claims move with the conclusion they stand on. An annotation whose method says
            // one thing and whose only claim says another is worse than either, and the claim is
            // where an audit of provenance looks first.
            //
            // The temporary table needs a name no earlier migration has used, because every
            // pending migration runs inside one transaction and `ON COMMIT DROP` waits for that
            // transaction rather than for the migration.
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE chosen_by_the_marking ON COMMIT DROP AS
                WITH marked AS (
                    SELECT a.id, a.word_id, a.entity_id
                    FROM word_entity a
                    JOIN word w ON w.id = a.word_id
                    JOIN text t ON t.id = w.text_id AND t.slug = 'bhsa'
                    WHERE a.method = 'strong-number'
                      AND a.source = 'BHSA''s proper-noun marking, and the Strong number the encyclopedia records for the name'
                      AND w.strong_number IS NOT NULL
                      AND (SELECT count(DISTINCT n.entity_id) FROM entity_name n
                           WHERE n.hebrew_strong_number = w.strong_number) > 1
                )
                SELECT id FROM marked
                UNION
                SELECT b.id
                FROM marked m
                JOIN link_word mine ON mine.word_id = m.word_id
                JOIN link_word other ON other.link_id = mine.link_id AND other.side <> mine.side
                JOIN word_entity b ON b.word_id = other.word_id AND b.entity_id = m.entity_id
                WHERE b.method = 'strong-number'
                  AND b.source = 'BHSA''s proper-noun marking, and the Strong number the encyclopedia records for the name';

                UPDATE word_entity SET method = 'lexical'
                WHERE method = 'strong-number'
                  AND source = 'the gentilic Strong''s Dictionary derives, resolving to exactly one people';

                UPDATE word_entity_claim SET method = 'lexical'
                WHERE method = 'strong-number'
                  AND source = 'the gentilic Strong''s Dictionary derives, resolving to exactly one people';

                UPDATE word_entity SET method = 'lexical'
                WHERE id IN (SELECT id FROM chosen_by_the_marking);

                UPDATE word_entity_claim SET method = 'lexical'
                WHERE method = 'strong-number'
                  AND word_entity_id IN (SELECT id FROM chosen_by_the_marking);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible. The method these rows carried was a claim the data does not support,
            // and the state to return to is the one the loaders now build, not one kept here.
        }
    }
}
