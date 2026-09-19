using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260920110000_TheGreekSaysWhoseGlossesItCarries")]
    public partial class TheGreekSaysWhoseGlossesItCarries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A text row is written once -- the loader returns early for a text already loaded --
            // so a rights note added to the definition never reaches a corpus that is already
            // loaded unless it is said here.
            //
            // Nestle's row said CC0 over a repository whose own readme declines to state one, and
            // said nothing at all about the English gloss standing on every word, which is a second
            // work by a different publisher. Resources/Nestle1904/LICENCE.md holds both readings.
            migrationBuilder.Sql(
                """
                UPDATE text
                SET rights_note =
                    'The edition is out of copyright and the digital text carries no condition: the '
                    || 'file read here is biblicalhumanities.org''s morphology file, whose own readme '
                    || 'waives every right under CC0. Its repository states no licence over the whole '
                    || 'and its components differ — the XML markup in the same repository, which is '
                    || 'not what is read, is share-alike. The English gloss on each word is a second '
                    || 'work: the Berean interlinear, whose file still carries Bible Hub''s 2016 '
                    || 'all-rights-reserved notice above a line saying it is now public domain, which '
                    || 'is what the publisher''s licensing page says of the Berean texts.'
                WHERE slug = 'NESTLE1904' AND rights_note IS NULL;
                """);

            // CC0 is what one component of that repository states and not what the repository
            // states, so the row points at the component the bytes came from.
            migrationBuilder.Sql(
                """
                UPDATE text
                SET source_url = 'https://github.com/biblicalhumanities/Nestle1904/tree/master/morph'
                WHERE slug = 'NESTLE1904'
                  AND source_url = 'https://github.com/biblicalhumanities/Nestle1904';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE text SET rights_note = NULL WHERE slug = 'NESTLE1904';");
            migrationBuilder.Sql(
                """
                UPDATE text SET source_url = 'https://github.com/biblicalhumanities/Nestle1904'
                WHERE slug = 'NESTLE1904';
                """);
        }
    }
}
