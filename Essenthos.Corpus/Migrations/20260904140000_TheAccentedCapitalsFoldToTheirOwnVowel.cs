using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheAccentedCapitalsFoldToTheirOwnVowel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Five cells of Greek Extended hold a capital belonging to a different vowel than the
            // sixteen-cell row they sit in, and the fold read the row. U+1FEC, capital rho with
            // rough breathing, came out as upsilon: Rome was stored folded as upsilon-omega-mu-eta
            // and answered nobody who typed it with a rho. 730 words -- 671 of Brenton's Septuagint
            // and 59 of Nestle 1904 -- and every one of them a name. The other four, capital
            // epsilon and capital omicron with varia and with oxia, are in no text loaded here and
            // fold correctly now for the one that arrives.
            //
            // Written as codepoint escapes rather than as the letters: U+1FC8 and U+1FF8 are not
            // distinguishable on the page from the monotonic capitals they are not.
            //
            // Clearing the column rather than computing the new value, because the fold lives in C#
            // and exists exactly once on purpose. This hands the 730 rows back to the folding pass,
            // which skips any word that already has a form and refills these on the next start.
            migrationBuilder.Sql(
                """
                UPDATE word
                SET normalised_text = NULL
                WHERE text ~ '[\u1FC8\u1FC9\u1FEC\u1FF8\u1FF9]'
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible. Down would have to write the wrong fold back, and the folding pass
            // would correct it again on the next start.
        }
    }
}
