using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class LexiconArticlesStandBesideStrong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lexicon_article",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    dictionary = table.Column<string>(type: "text", nullable: false),
                    strong_number = table.Column<string>(type: "text", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    entry = table.Column<string>(type: "text", nullable: false),
                    headword = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lexicon_article", x => x.id);
                    table.CheckConstraint("ck_lexicon_article_ordinal", "ordinal >= 1");
                    table.CheckConstraint("ck_lexicon_article_source_not_empty", "length(btrim(\"source\")) > 0");
                },
                comment: "The articles of the scholarly lexicons (BDB, Abbott-Smith, LSJ) under the Strong number each lexicon files them at, in its order: one row per article and number. The body is the article as JSON blocks of marked text, its Hebrew and Greek marked, its cited verses and named numbers linked.");

            migrationBuilder.CreateIndex(
                name: "ix_lexicon_article_strong_number_dictionary_ordinal",
                table: "lexicon_article",
                columns: new[] { "strong_number", "dictionary", "ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lexicon_article");
        }
    }
}
