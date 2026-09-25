using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheGeezWordsHaveALexiconOfTheirOwn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "geez_lexicon_entry",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entry = table.Column<string>(type: "text", nullable: false),
                    headword = table.Column<string>(type: "text", nullable: false),
                    forms = table.Column<string[]>(type: "text[]", nullable: false),
                    consonants = table.Column<string[]>(type: "text[]", nullable: false),
                    latin = table.Column<string[]>(type: "text[]", nullable: false),
                    greek = table.Column<string[]>(type: "text[]", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geez_lexicon_entry", x => x.id);
                    table.CheckConstraint("ck_geez_lexicon_entry_source_not_empty", "length(btrim(\"source\")) > 0");
                },
                comment: "Dillmann's Lexicon Linguae Aethiopicae as Beta maṣāḥǝft digitised it: each entry's headword, the spellings filed under it, its Latin and its Greek. Which Ge'ez word is a form of which entry is not stored: a reading concludes it and says how.");

            migrationBuilder.CreateIndex(
                name: "ix_geez_lexicon_entry_consonants",
                table: "geez_lexicon_entry",
                column: "consonants")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_geez_lexicon_entry_entry",
                table: "geez_lexicon_entry",
                column: "entry",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "geez_lexicon_entry");
        }
    }
}
