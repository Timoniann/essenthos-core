using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class LinkClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "link_claim",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    link_id = table.Column<long>(type: "bigint", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_link_claim", x => x.id);
                    table.CheckConstraint("ck_link_claim_confidence_range", "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");
                    table.CheckConstraint("ck_link_claim_inferred_carries_confidence", "\"method\" IN ('stated-by-source', 'manual') OR \"confidence\" IS NOT NULL");
                    table.CheckConstraint("ck_link_claim_source_not_empty", "length(btrim(\"source\")) > 0");
                    table.CheckConstraint("ck_link_claim_stated_carries_no_confidence", "\"method\" <> 'stated-by-source' OR \"confidence\" IS NULL");
                    table.ForeignKey(
                        name: "fk_link_claim_link_link_id",
                        column: x => x.link_id,
                        principalTable: "link",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_link_claim_link_id",
                table: "link_claim",
                column: "link_id");

            migrationBuilder.CreateIndex(
                name: "ix_link_claim_link_id_method_source",
                table: "link_claim",
                columns: new[] { "link_id", "method", "source" },
                unique: true);

            // Every link that exists was asserted by exactly one method, so each gets one claim
            // saying so. Nothing is lost and nothing is inferred: the claim repeats what the link
            // already said about itself.
            migrationBuilder.Sql(
                """
                INSERT INTO link_claim (link_id, method, confidence, source, note)
                SELECT id, method, confidence, source, note FROM link
                """);

            // Where two links name exactly the same words in the same pair of texts, they were
            // never two facts -- they were two methods agreeing, stored as rivals. 4,664 of the
            // 4,599,548 links are like this, all of them the Ukrainian interlinear and the aligner
            // arriving at the same word pair independently, which is the single most valuable thing
            // the corpus knows and the one it could not see.
            //
            // The link that survives is the one whose method stands highest -- testimony over
            // inference -- and the others hand it their claims before they go.
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE merge_shape ON COMMIT DROP AS
                WITH shape AS (
                    SELECT l.id, l.from_text_id, l.to_text_id, l.method,
                           string_agg(lw.word_id::text, ',' ORDER BY lw.side, lw.word_id) AS words
                    FROM link l JOIN link_word lw ON lw.link_id = l.id
                    GROUP BY l.id, l.from_text_id, l.to_text_id, l.method)
                SELECT id,
                       first_value(id) OVER (
                           PARTITION BY from_text_id, to_text_id, words
                           ORDER BY CASE method
                                        WHEN 'manual' THEN 5
                                        WHEN 'stated-by-source' THEN 4
                                        WHEN 'strong-number' THEN 3
                                        WHEN 'lexical' THEN 2
                                        ELSE 1
                                    END DESC, id) AS keeper
                FROM shape;

                INSERT INTO link_claim (link_id, method, confidence, source, note)
                SELECT m.keeper, c.method, c.confidence, c.source, c.note
                FROM merge_shape m JOIN link_claim c ON c.link_id = m.id
                WHERE m.keeper <> m.id
                ON CONFLICT DO NOTHING;

                DELETE FROM link WHERE id IN (SELECT id FROM merge_shape WHERE keeper <> id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "link_claim");
        }
    }
}
