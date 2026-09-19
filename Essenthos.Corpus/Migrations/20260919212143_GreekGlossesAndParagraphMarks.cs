using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class GreekGlossesAndParagraphMarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "break",
                table: "word",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "lexicon_gloss",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entry = table.Column<string>(type: "text", nullable: false),
                    strong_number = table.Column<string>(type: "text", nullable: false),
                    lemma = table.Column<string>(type: "text", nullable: false),
                    gloss = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lexicon_gloss", x => x.id);
                    table.CheckConstraint("ck_lexicon_gloss_source_not_empty", "length(btrim(\"source\")) > 0");
                },
                comment: "A lexicon's short gloss for each dictionary form of each entry, filed under the number the lexicon gives it. Which word a gloss belongs to is not stored: a reading reaches it by the word's lemma or number and says which way.");

            migrationBuilder.CreateIndex(
                name: "ix_lexicon_gloss_entry_lemma",
                table: "lexicon_gloss",
                columns: new[] { "entry", "lemma" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lexicon_gloss_lemma",
                table: "lexicon_gloss",
                column: "lemma");

            migrationBuilder.CreateIndex(
                name: "ix_lexicon_gloss_strong_number",
                table: "lexicon_gloss",
                column: "strong_number");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lexicon_gloss");

            migrationBuilder.DropColumn(
                name: "break",
                table: "word");
        }
    }
}
