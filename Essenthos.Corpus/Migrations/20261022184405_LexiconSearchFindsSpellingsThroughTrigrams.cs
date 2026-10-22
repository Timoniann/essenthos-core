using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <summary>
    /// Trigram indexes for what the lexicon search compares: the lemma and the transliteration folded
    /// as the search folds them, and the two glosses as written. Without them every search folds and
    /// reads all fourteen thousand entries twice, a tenth of a second Go To spends as the reader types.
    /// The folded two are expression indexes, which the planner uses only while the search folds
    /// with the same text; a test holds <c>DiacriticFolding</c> to it, so a new fold comes with a
    /// migration that rebuilds them.
    /// </summary>
    public partial class LexiconSearchFindsSpellingsThroughTrigrams : Migration
    {
        /// <summary>The fold the indexes were built with, over the column <c>{0}</c>.</summary>
        public const string Fold =
            "lower(regexp_replace(regexp_replace(normalize({0}, NFD), " +
            "'[\u0300-\u036F\u0591-\u05BD\u05BF\u05C1\u05C2\u05C4\u05C5\u05C7]', '', 'g'), " +
            "'[\u02B9\u02BA\u02BB\u02BC\u02BD\u2018\u2019'']', '', 'g'))";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX ix_strong_entry_lemma_folded ON strong_entry " +
                $"USING gin (({string.Format(Fold, "lemma")}) gin_trgm_ops);");
            migrationBuilder.Sql(
                "CREATE INDEX ix_strong_entry_transliteration_folded ON strong_entry " +
                $"USING gin (({string.Format(Fold, "transliteration")}) gin_trgm_ops);");

            migrationBuilder.CreateIndex(
                name: "ix_strong_entry_definition",
                table: "strong_entry",
                column: "definition")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_strong_entry_kjv_definition",
                table: "strong_entry",
                column: "kjv_definition")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX ix_strong_entry_lemma_folded;");
            migrationBuilder.Sql("DROP INDEX ix_strong_entry_transliteration_folded;");

            migrationBuilder.DropIndex(
                name: "ix_strong_entry_definition",
                table: "strong_entry");

            migrationBuilder.DropIndex(
                name: "ix_strong_entry_kjv_definition",
                table: "strong_entry");
        }
    }
}
