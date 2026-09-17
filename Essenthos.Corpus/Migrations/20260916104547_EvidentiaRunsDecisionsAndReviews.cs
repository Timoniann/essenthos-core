using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class EvidentiaRunsDecisionsAndReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "evidentia_run",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    from_text_id = table.Column<int>(type: "integer", nullable: false),
                    to_text_id = table.Column<int>(type: "integer", nullable: false),
                    parent_run_id = table.Column<int>(type: "integer", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rule_version = table.Column<string>(type: "text", nullable: false),
                    configuration = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    scope = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidentia_run", x => x.id);
                    table.ForeignKey(
                        name: "fk_evidentia_run_evidentia_run_parent_run_id",
                        column: x => x.parent_run_id,
                        principalTable: "evidentia_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_evidentia_run_texts_from_text_id",
                        column: x => x.from_text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_evidentia_run_texts_to_text_id",
                        column: x => x.to_text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evidentia_decision",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_id = table.Column<int>(type: "integer", nullable: false),
                    source_word_id = table.Column<long>(type: "bigint", nullable: false),
                    canonical_book = table.Column<short>(type: "smallint", nullable: false),
                    canonical_chapter = table.Column<short>(type: "smallint", nullable: false),
                    canonical_verse = table.Column<short>(type: "smallint", nullable: false),
                    content = table.Column<bool>(type: "boolean", nullable: false),
                    target_word_id = table.Column<long>(type: "bigint", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: true),
                    tier = table.Column<string>(type: "text", nullable: true),
                    rationale = table.Column<string>(type: "text", nullable: true),
                    confidence = table.Column<float>(type: "real", nullable: true),
                    score = table.Column<float>(type: "real", nullable: true),
                    margin = table.Column<float>(type: "real", nullable: true),
                    candidates = table.Column<short>(type: "smallint", nullable: false),
                    abstention = table.Column<string>(type: "text", nullable: true),
                    exact_address = table.Column<float>(type: "real", nullable: true),
                    neighbouring_address = table.Column<float>(type: "real", nullable: true),
                    matching_form = table.Column<float>(type: "real", nullable: true),
                    shared_strong = table.Column<float>(type: "real", nullable: true),
                    dictionary_sense = table.Column<float>(type: "real", nullable: true),
                    target_gloss = table.Column<float>(type: "real", nullable: true),
                    known_rendering = table.Column<float>(type: "real", nullable: true),
                    morphology = table.Column<float>(type: "real", nullable: true),
                    syntax = table.Column<float>(type: "real", nullable: true),
                    statistical_aligner = table.Column<float>(type: "real", nullable: true),
                    rendering_observations = table.Column<int>(type: "integer", nullable: true),
                    rendering_share = table.Column<float>(type: "real", nullable: true),
                    rendering_next_share = table.Column<float>(type: "real", nullable: true),
                    rendering_form = table.Column<string>(type: "text", nullable: true),
                    alternative_word_ids = table.Column<long[]>(type: "bigint[]", nullable: true),
                    alternative_scores = table.Column<float[]>(type: "real[]", nullable: true),
                    alternative_distances = table.Column<short[]>(type: "smallint[]", nullable: true),
                    alternative_taken = table.Column<bool[]>(type: "boolean[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidentia_decision", x => x.id);
                    table.CheckConstraint("ck_evidentia_decision_abstention_has_reason", "\"target_word_id\" IS NOT NULL OR (\"abstention\" IS NOT NULL AND \"kind\" IS NULL)");
                    table.CheckConstraint("ck_evidentia_decision_confidence_range", "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");
                    table.CheckConstraint("ck_evidentia_decision_proposal_is_described", "\"target_word_id\" IS NULL OR (\"kind\" IS NOT NULL AND \"confidence\" IS NOT NULL AND \"abstention\" IS NULL)");
                    table.ForeignKey(
                        name: "fk_evidentia_decision_evidentia_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "evidentia_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_evidentia_decision_words_source_word_id",
                        column: x => x.source_word_id,
                        principalTable: "word",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_evidentia_decision_words_target_word_id",
                        column: x => x.target_word_id,
                        principalTable: "word",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evidentia_review",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    decision_id = table.Column<long>(type: "bigint", nullable: false),
                    verdict = table.Column<string>(type: "text", nullable: false),
                    examined = table.Column<bool>(type: "boolean", nullable: false),
                    corrected_target_word_id = table.Column<long>(type: "bigint", nullable: true),
                    reviewer = table.Column<string>(type: "text", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    link_id = table.Column<long>(type: "bigint", nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withheld = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidentia_review", x => x.id);
                    table.CheckConstraint("ck_evidentia_review_a_rejection_is_never_applied", "\"verdict\" <> 'rejected' OR (\"applied_at\" IS NULL AND \"link_id\" IS NULL)");
                    table.CheckConstraint("ck_evidentia_review_correction_names_its_word", "(\"verdict\" = 'corrected') = (\"corrected_target_word_id\" IS NOT NULL)");
                    table.CheckConstraint("ck_evidentia_review_only_an_approval_may_be_unexamined", "\"examined\" OR \"verdict\" = 'approved'");
                    table.CheckConstraint("ck_evidentia_review_reviewer_not_empty", "length(btrim(\"reviewer\")) > 0");
                    table.ForeignKey(
                        name: "fk_evidentia_review_evidentia_decision_decision_id",
                        column: x => x.decision_id,
                        principalTable: "evidentia_decision",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_evidentia_review_links_link_id",
                        column: x => x.link_id,
                        principalTable: "link",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_evidentia_review_words_corrected_target_word_id",
                        column: x => x.corrected_target_word_id,
                        principalTable: "word",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_evidentia_decision_run_id_canonical_book_canonical_chapter_",
                table: "evidentia_decision",
                columns: new[] { "run_id", "canonical_book", "canonical_chapter", "canonical_verse" });

            migrationBuilder.CreateIndex(
                name: "ix_evidentia_decision_source_word_id",
                table: "evidentia_decision",
                column: "source_word_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidentia_decision_target_word_id",
                table: "evidentia_decision",
                column: "target_word_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidentia_review_corrected_target_word_id",
                table: "evidentia_review",
                column: "corrected_target_word_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidentia_review_decision_id",
                table: "evidentia_review",
                column: "decision_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_evidentia_review_link_id",
                table: "evidentia_review",
                column: "link_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidentia_run_from_text_id",
                table: "evidentia_run",
                column: "from_text_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidentia_run_parent_run_id",
                table: "evidentia_run",
                column: "parent_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidentia_run_to_text_id",
                table: "evidentia_run",
                column: "to_text_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evidentia_review");

            migrationBuilder.DropTable(
                name: "evidentia_decision");

            migrationBuilder.DropTable(
                name: "evidentia_run");
        }
    }
}
