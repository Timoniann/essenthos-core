using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AGuessAndAStatementAboutOneWordPairAreOneLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Where a model and a source name exactly the same words, fold them into one link with
            // two claims -- which is what link_claim exists for and what the integrity check that
            // caught this asks for by name.
            //
            // Reloading the Slavic interlinears wrote their stated links against a corpus whose
            // aligner links were already there, so 1,084 word pairs ended up named twice: 626 in the
            // Synodal and 458 in the Ukrainian. Two links for one correspondence make it read as
            // contended when nothing is in doubt, and they double-count it in every measure.
            //
            // The stated link survives and the model's becomes a claim on it. That direction is not
            // arbitrary: a guess is rebuildable by running the aligner again and a statement is not,
            // so the row that carries the source's identity is the one to keep. The aligner's own
            // method, confidence and source move onto it, so nothing about what the model found is
            // lost -- it stops being a second answer and becomes a second voice for the same one.
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE folded ON COMMIT DROP AS
                WITH shaped AS (
                    SELECT l.id, l.from_text_id, l.to_text_id, l.relation, l.method,
                           l.confidence, l.source, l.note,
                           md5(string_agg(lw.side || ':' || lw.word_id, ',' ORDER BY lw.side, lw.word_id)) AS shape
                    FROM link l
                    JOIN link_word lw ON lw.link_id = l.id
                    GROUP BY l.id
                ),
                paired AS (
                    SELECT stated.id AS keep, guess.id AS drop_id,
                           guess.method, guess.confidence, guess.source, guess.note
                    FROM shaped stated
                    JOIN shaped guess
                      ON guess.from_text_id = stated.from_text_id
                     AND guess.to_text_id = stated.to_text_id
                     AND guess.relation = stated.relation
                     AND guess.shape = stated.shape
                     AND guess.id <> stated.id
                    WHERE stated.method = 'stated-by-source' AND guess.method <> 'stated-by-source'
                )
                SELECT * FROM paired;

                INSERT INTO link_claim (link_id, method, confidence, source, note)
                SELECT keep, method, confidence, source, note FROM folded
                ON CONFLICT DO NOTHING;

                DELETE FROM link WHERE id IN (SELECT drop_id FROM folded);
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
