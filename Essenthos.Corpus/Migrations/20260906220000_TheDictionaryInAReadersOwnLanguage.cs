using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheDictionaryInAReadersOwnLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "strong_entry_translation",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    strong_number = table.Column<string>(type: "text", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    definition = table.Column<string>(type: "text", nullable: true),
                    derivation = table.Column<string>(type: "text", nullable: true),
                    kjv_definition = table.Column<string>(type: "text", nullable: true),
                    detailed_definition = table.Column<string>(type: "text", nullable: true),
                    method = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strong_entry_translation", x => x.id);
                    table.CheckConstraint("ck_strong_entry_translation_confidence_range", "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");
                    table.CheckConstraint("ck_strong_entry_translation_says_something", "\"definition\" IS NOT NULL OR \"derivation\" IS NOT NULL OR \"kjv_definition\" IS NOT NULL OR \"detailed_definition\" IS NOT NULL");
                    table.CheckConstraint("ck_strong_entry_translation_source_not_empty", "length(btrim(\"source\")) > 0");
                },
                comment: "Strong's four prose fields in one other language, next to the English and never over it. Only these four are language; the lemma, the transliteration, the morphology code and the see-also numbers are identifiers and are not here, because a translated identifier breaks a lookup silently. A row is a machine's reading of Strong rather than Strong in another language, and source says which machine, under which prompt, on which day. It carries no confidence where every other inference in this corpus must: there is no candidate set to be sure between, and what a reader checks it against is the English on strong_entry, not a number nobody measured.");

            migrationBuilder.CreateIndex(
                name: "ix_strong_entry_translation_language",
                table: "strong_entry_translation",
                column: "language");

            migrationBuilder.CreateIndex(
                name: "ix_strong_entry_translation_strong_number",
                table: "strong_entry_translation",
                column: "strong_number");

            migrationBuilder.CreateIndex(
                name: "ix_strong_entry_translation_strong_number_language",
                table: "strong_entry_translation",
                columns: new[] { "strong_number", "language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strong_entry_translation");
        }
    }
}
