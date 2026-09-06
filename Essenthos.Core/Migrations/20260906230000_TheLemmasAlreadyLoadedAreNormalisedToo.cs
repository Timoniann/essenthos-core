using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260906230000_TheLemmasAlreadyLoadedAreNormalisedToo")]
    public partial class TheLemmasAlreadyLoadedAreNormalisedToo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PRB-0384 fixed the loader; PRB-0393 is what the fix did not reach. GlauxLemmaLoader
            // returns early for a text whose lemmas are already written (RUL-0005), so a database
            // loaded before that commit keeps the decomposed strings for ever and the twelfth
            // integrity check reports 560,219 of them -- every one Brenton's, and every one
            // unjoinable to the Nestle lemma it is spelled identically to on screen.
            //
            // The fix and the check that catches it shipped together, so neither was ever run
            // against a database that predates the fix. This is the half that repairs what exists.
            //
            // Greek only, and deliberately: the Hebrew texts order their points differently from
            // canonical order as well, and there the column holds the witness's own text. Whether
            // that may be rewritten is PRB-0386 and is the owner's decision, not a migration's.
            migrationBuilder.Sql(
                """
                UPDATE word SET lemma = normalize(lemma, NFC)
                FROM text
                WHERE text.id = word.text_id
                  AND text.language = 'grc'
                  AND word.lemma IS NOT NULL
                  AND word.lemma <> normalize(word.lemma, NFC);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. There is no going back to a decomposed lemma: the original
            // spelling is not recoverable from the composed one for the rows that had both forms,
            // and restoring it would put back a value that joins to nothing. Reload the text if the
            // old bytes are genuinely wanted.
        }
    }
}
