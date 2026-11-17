using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheGeezWordsAreReadInContextAndDillmannInFourLanguages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "geez_lexicon_rendering",
                columns: table => new
                {
                    entry = table.Column<string>(type: "text", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    sense = table.Column<string>(type: "text", nullable: false),
                    en = table.Column<string>(type: "text", nullable: true),
                    uk = table.Column<string>(type: "text", nullable: true),
                    de = table.Column<string>(type: "text", nullable: true),
                    es = table.Column<string>(type: "text", nullable: true),
                    model = table.Column<string>(type: "text", nullable: false),
                    run = table.Column<string>(type: "text", nullable: false),
                    made = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geez_lexicon_rendering", x => new { x.entry, x.ordinal });
                    table.CheckConstraint("ck_geez_lexicon_rendering_says_something", "coalesce(en, uk, de, es) IS NOT NULL");
                    table.CheckConstraint("ck_geez_lexicon_rendering_source_not_empty", "length(btrim(\"source\")) > 0");
                },
                comment: "Dillmann's Latin, sense by sense in his order and numbering, rendered into English, Ukrainian, German and Spanish by a language model: a machine rendering shown under his Latin, never in its place. CC BY-NC-SA 4.0, as the digital edition it is made from.");

            migrationBuilder.CreateTable(
                name: "geez_word_gloss",
                columns: table => new
                {
                    word_id = table.Column<long>(type: "bigint", nullable: false),
                    lemma = table.Column<string>(type: "text", nullable: false),
                    lemma_source = table.Column<string>(type: "text", nullable: false),
                    sense_en = table.Column<string>(type: "text", nullable: false),
                    sense_uk = table.Column<string>(type: "text", nullable: false),
                    line_en = table.Column<string>(type: "text", nullable: false),
                    line_uk = table.Column<string>(type: "text", nullable: false),
                    greek_word_ids = table.Column<long[]>(type: "bigint[]", nullable: false),
                    morphology = table.Column<string>(type: "text", nullable: true),
                    confidence = table.Column<string>(type: "text", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    run = table.Column<string>(type: "text", nullable: false),
                    made = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geez_word_gloss", x => x.word_id);
                    table.CheckConstraint("ck_geez_word_gloss_confidence", "confidence IN ('high', 'medium', 'low')");
                    table.CheckConstraint("ck_geez_word_gloss_source_not_empty", "length(btrim(\"source\")) > 0");
                    table.ForeignKey(
                        name: "fk_geez_word_gloss_word_word_id",
                        column: x => x.word_id,
                        principalTable: "word",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "What a Ge'ez word means where it stands, as a language model read it with the verse's Greek and an English translation beside it: lemma, sense, the line under the word, the Greek words it renders. Nobody checked a row; the reader is told it is a model's reading.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "geez_lexicon_rendering");

            migrationBuilder.DropTable(
                name: "geez_word_gloss");
        }
    }
}
