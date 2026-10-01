using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AWordThatWritesAnotherWordsInflectionGoesWithIt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "head_word_id",
                table: "link_word",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "link_word",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_link_word_head_word_id",
                table: "link_word",
                column: "head_word_id",
                filter: "head_word_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_link_word_head",
                table: "link_word",
                sql: "head_word_id IS NULL OR (role IS NOT NULL AND head_word_id <> word_id)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_link_word_role",
                table: "link_word",
                sql: "role IS NULL OR role IN ('attached', 'phrase-member')");

            migrationBuilder.AddForeignKey(
                name: "fk_link_word_words_head_word_id",
                table: "link_word",
                column: "head_word_id",
                principalTable: "word",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // The attached-word verdicts already written as links take their role and head from the
            // decisions they were written from. Every decision written before this migration names its
            // head only by the surface in its rationale, so the head is the word of that surface in
            // the same verse, nearest first, that the run placed on the same counterpart or that a link
            // joins to it. Kept as it stood when the columns were added: what is written later is
            // marked by AttachedWords, which may change after this has run.
            migrationBuilder.Sql(
                """
                WITH attached AS (
                    SELECT r.link_id, d.source_word_id AS word_id, d.target_word_id, d.run_id, d.rationale,
                           d.anchor_source_word_id, d.anchor_target_word_id, w.verse_id, w.position
                    FROM evidentia_review r
                    JOIN evidentia_decision d ON d.id = r.decision_id
                    JOIN word w ON w.id = d.source_word_id
                    WHERE d.kind = 'attached-word' AND r.verdict = 'approved' AND r.link_id IS NOT NULL
                ),
                headed AS (
                    SELECT a.link_id, a.word_id, a.anchor_source_word_id AS head_word_id, a.rationale
                    FROM attached a
                    WHERE a.anchor_source_word_id IS NOT NULL AND a.anchor_target_word_id = a.target_word_id
                    UNION ALL
                    (SELECT DISTINCT ON (a.link_id, a.word_id) a.link_id, a.word_id, h.id, a.rationale
                     FROM attached a
                     JOIN word h ON h.verse_id = a.verse_id AND h.id <> a.word_id
                     WHERE a.anchor_source_word_id IS NULL
                       AND starts_with(a.rationale, split_part(a.rationale, ' ', 1) || ' of ''' || h.text || '''')
                       AND (EXISTS (SELECT 1 FROM evidentia_decision hd
                                    WHERE hd.run_id = a.run_id AND hd.source_word_id = h.id
                                      AND hd.target_word_id = a.target_word_id)
                            OR EXISTS (SELECT 1 FROM link_word hl
                                       JOIN link_word ht ON ht.link_id = hl.link_id AND ht.side <> hl.side
                                       WHERE hl.word_id = h.id AND ht.word_id = a.target_word_id))
                     ORDER BY a.link_id, a.word_id, abs(h.position - a.position))
                )
                UPDATE link_word lw
                SET role = CASE WHEN split_part(headed.rationale, ' ', 1) IN ('PhrasalParticle', 'VerbOfItsParticle')
                                THEN 'phrase-member' ELSE 'attached' END,
                    head_word_id = headed.head_word_id
                FROM headed
                WHERE lw.link_id = headed.link_id AND lw.word_id = headed.word_id AND lw.role IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_link_word_words_head_word_id",
                table: "link_word");

            migrationBuilder.DropIndex(
                name: "ix_link_word_head_word_id",
                table: "link_word");

            migrationBuilder.DropCheckConstraint(
                name: "ck_link_word_head",
                table: "link_word");

            migrationBuilder.DropCheckConstraint(
                name: "ck_link_word_role",
                table: "link_word");

            migrationBuilder.DropColumn(
                name: "head_word_id",
                table: "link_word");

            migrationBuilder.DropColumn(
                name: "role",
                table: "link_word");
        }
    }
}
