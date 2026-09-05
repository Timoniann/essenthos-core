using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheComposedGuessJoinsTheStatementItAgreesWith : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Where a model and a source name exactly the same words, fold them into one link with
            // two claims -- which is what link_claim exists for and what the integrity check that
            // caught this asks for by name.
            //
            // This is the second time. The first was the Slavic interlinears being reloaded against
            // a corpus that already had aligner links; this is the same collision reached from the
            // other side, by recomposing a pair whose stated links were already there. 699 word
            // pairs between the Ukrainian and BHSA ended up named twice. Two links for one
            // correspondence make it read as contended when nothing is in doubt, and they
            // double-count it in every measure.
            //
            // The stated link survives and the model's becomes a claim on it. That direction is not
            // arbitrary: a guess is rebuildable by running the aligner again and a statement is not,
            // so the row that carries the source's identity is the one to keep. The aligner's own
            // method, confidence and source move onto it, so nothing about what the model found is
            // lost -- it stops being a second answer and becomes a second voice for the same one.
            //
            // The links are fingerprinted on integers before their words are assembled into a
            // string. The obvious form asks Postgres to sort four and a half million assembled
            // strings across parallel workers, which is what the verification pass had to stop
            // doing; counting the words and summing their ids is cheap, and the exact comparison
            // then runs on the handful that collide.
            //
            // The temporary table needs a name no earlier migration has used. Every pending
            // migration runs inside one transaction, and `ON COMMIT DROP` waits for that
            // transaction rather than for the migration, so a name reused from an earlier one is
            // still occupied when this runs on a database being built from nothing.
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE agreeing ON COMMIT DROP AS
                WITH fingerprint AS (
                    SELECT l.id, l.from_text_id, l.to_text_id, l.relation, l.method,
                           l.confidence, l.source, l.note,
                           count(*) AS words, min(lw.word_id) AS lowest,
                           max(lw.word_id) AS highest, sum(lw.word_id) AS total
                    FROM link l
                    JOIN link_word lw ON lw.link_id = l.id
                    GROUP BY l.id
                ),
                colliding AS (
                    SELECT f.*
                    FROM fingerprint f
                    JOIN (SELECT from_text_id, to_text_id, words, lowest, highest, total
                          FROM fingerprint
                          GROUP BY 1, 2, 3, 4, 5, 6
                          HAVING count(*) > 1) c
                      ON c.from_text_id = f.from_text_id AND c.to_text_id = f.to_text_id
                     AND c.words = f.words AND c.lowest = f.lowest
                     AND c.highest = f.highest AND c.total = f.total
                ),
                shaped AS (
                    SELECT co.id, co.from_text_id, co.to_text_id, co.relation, co.method,
                           co.confidence, co.source, co.note,
                           string_agg(lw.side || ':' || lw.word_id, ',' ORDER BY lw.side, lw.word_id) AS shape
                    FROM colliding co
                    JOIN link_word lw ON lw.link_id = co.id
                    GROUP BY co.id, co.from_text_id, co.to_text_id, co.relation, co.method,
                             co.confidence, co.source, co.note
                )
                SELECT stated.id AS keep, guess.id AS drop_id,
                       guess.method, guess.confidence, guess.source, guess.note
                FROM shaped stated
                JOIN shaped guess
                  ON guess.from_text_id = stated.from_text_id
                 AND guess.to_text_id = stated.to_text_id
                 AND guess.relation = stated.relation
                 AND guess.shape = stated.shape
                 AND guess.id <> stated.id
                WHERE stated.method = 'stated-by-source' AND guess.method <> 'stated-by-source';

                INSERT INTO link_claim (link_id, method, confidence, source, note)
                SELECT keep, method, confidence, source, note FROM agreeing
                ON CONFLICT DO NOTHING;

                DELETE FROM link WHERE id IN (SELECT drop_id FROM agreeing);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible, and splitting one correspondence back into two rows is not a state
            // worth returning to.
        }
    }
}
