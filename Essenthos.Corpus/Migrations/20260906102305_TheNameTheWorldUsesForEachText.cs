using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheNameTheWorldUsesForEachText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The Ukrainian first, because it is a rename and not a spelling. UKR is the ISO 639-2
            // code for the Ukrainian language and names no edition at all; it stood here only
            // because bible4u calls its download that. The world publishes this translation as
            // UBIO -- YouVersion version 186, bolls.life, Door43's uk_ubio -- and UKR1962 is what
            // API.Bible and the Digital Bible Library call the same 1962 edition. Both older
            // spellings stay reachable: they are declared as aliases and resolve for ever.
            //
            // Matched case-insensitively so this reads the same whether it runs before or after
            // the capitalisation below, and so a database somebody has already half-moved by hand
            // is not left with two Ukrainian Bibles.
            migrationBuilder.Sql(
                """
                UPDATE text SET slug = 'UBIO' WHERE upper(slug) = 'UKR';
                """);

            // Every remaining identifier, capitalised. KJV, BHSA, RUSV, LXX-BRENTON: no Bible
            // software anywhere writes a version code in lower case, and a reader who has seen KJV
            // in every other tool should not have to learn that this one place disagrees.
            //
            // Nothing that was stored under the old spelling stops working. An identifier is
            // matched case-insensitively at every point it is resolved -- the endpoints all go
            // through CanonIndex, whose lookup and whose alias table both ignore case -- so every
            // URL, every saved reading position and every reference pasted from elsewhere still
            // reaches the same text, and every response now names it the new way.
            //
            // The unique index on slug is untouched and stays case-sensitive. It cannot express
            // the invariant that matters, which spans the aliases and the canonical slugs
            // together; that is checked in TextAliases and in the tests over the declared corpus,
            // where both halves are visible at once.
            migrationBuilder.Sql(
                """
                UPDATE text SET slug = upper(slug) WHERE slug <> upper(slug);
                """);

            // The prose in link.source is deliberately left alone. "SIL.Machine, aligned as
            // written and as stems and through kjv" is a sentence describing a method, not an
            // identifier anything resolves: 1.6 million of the link table's 4.9 million rows
            // mention a text by name, rewriting them would rewrite a third of a gigabyte-sized
            // table for a change nothing reads, and the dataset attribution that does read these
            // strings matches on their prefixes, which none of the mentions is in.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE text SET slug = lower(slug) WHERE slug <> lower(slug);
                UPDATE text SET slug = 'ukr' WHERE lower(slug) = 'ubio';
                """);
        }
    }
}
