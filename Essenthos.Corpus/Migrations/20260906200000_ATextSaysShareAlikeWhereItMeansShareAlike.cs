using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260906200000_ATextSaysShareAlikeWhereItMeansShareAlike")]
    public partial class ATextSaysShareAlikeWhereItMeansShareAlike : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A text row is written once -- the loader returns early for a text already loaded -- so
            // neither of these reaches the corpus without being said here.

            // Swete's Septuagint was recorded as plain attribution. Its licence has always been read
            // and recorded as CC BY-SA; the enum simply had no value that could say ShareAlike, so
            // the obligation was invisible in the one field whose job is to answer "on what terms may
            // we serve this".
            migrationBuilder.Sql(
                """
                UPDATE text SET redistribution = 'share-alike'
                WHERE slug = 'LXX-SWETE' AND redistribution = 'permitted-with-attribution';
                """);

            // The Ohienko was recorded as public domain, which is not a missing value but a wrong
            // one. PRB-0325: three sources call it public domain by copying one another, the
            // translator died in 1972, and the only grant anybody has produced is CC BY-SA for
            // pre-1991 printings through Wikimedia VRT ticket 2013112610015211. The owner decided to
            // keep the text and accept the clause.
            migrationBuilder.Sql(
                """
                UPDATE text
                SET redistribution = 'share-alike',
                    licence = 'CC-BY-SA-4.0',
                    licence_url = 'https://creativecommons.org/licenses/by-sa/4.0/'
                WHERE slug = 'UBIO';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE text SET redistribution = 'permitted-with-attribution' WHERE slug = 'LXX-SWETE';
                """);

            // Deliberately not restored to public-domain: that value was wrong when it was written
            // and putting it back would reintroduce the false claim rather than undo a change.
            migrationBuilder.Sql(
                """
                UPDATE text SET redistribution = 'permitted-with-attribution' WHERE slug = 'UBIO';
                """);
        }
    }
}
