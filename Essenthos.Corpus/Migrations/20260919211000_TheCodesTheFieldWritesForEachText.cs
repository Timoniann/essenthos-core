using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260919211000_TheCodesTheFieldWritesForEachText")]
    public partial class TheCodesTheFieldWritesForEachText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Seven identifiers that were spelled out in full, or hyphenated, move to the codes the
            // field already prints for the same editions: RP2018 is the Robinson-Pierpont
            // repository's own, TR1894 and TR1550 are Bible Gateway's, TISCH is CrossWire's, STEP's
            // and bolls.life's, WH is the siglum an apparatus writes for Westcott and Hort, and
            // GRCBRENT is eBible's for Brenton's Greek. SWETE has no code anywhere and is this
            // project's own. Every old spelling is declared in TextAliases and resolves for ever.
            //
            // Case-insensitive on the old spelling, as the capitalisation before it was, so a
            // database somebody half-moved by hand is not left holding both.
            //
            // The prose in link.source keeps the names the texts had when the links were written,
            // for the reason the capitalisation gave: it describes a method and is resolved by
            // nothing. The one loader whose guard reads those sentences asks under every spelling
            // TextAliases declares.
            migrationBuilder.Sql(
                """
                UPDATE text SET slug = 'RP2018' WHERE upper(slug) = 'ROBINSONPIERPONT2018';
                UPDATE text SET slug = 'TR1894' WHERE upper(slug) = 'SCRIVENER1894';
                UPDATE text SET slug = 'TR1550' WHERE upper(slug) = 'STEPHANUS1550';
                UPDATE text SET slug = 'TISCH' WHERE upper(slug) = 'TISCHENDORF1872';
                UPDATE text SET slug = 'WH1881' WHERE upper(slug) = 'WESTCOTTHORT1881';
                UPDATE text SET slug = 'GRCBRENT' WHERE upper(slug) = 'LXX-BRENTON';
                UPDATE text SET slug = 'SWETE' WHERE upper(slug) = 'LXX-SWETE';
                """);

            // What Brenton's Greek is, said more exactly than "following Codex Vaticanus": his
            // preface says the translation was made from the Vatican text in Valpy's edition, which
            // is the Sixtine edition of 1587 reprinted, and that edition supplies the codex's lost
            // leaves from other manuscripts. A text row is written once, so the loader's new wording
            // reaches the corpus already loaded only through here.
            migrationBuilder.Sql(
                """
                UPDATE text
                SET edition = 'The Greek Brenton printed facing his English translation: the Sixtine edition of 1587, which follows Codex Vaticanus, as Valpy reprinted it',
                    about = 'The Greek here is not Brenton''s work in the way the English beside it is. His preface says '
                         || 'the translation was made from the Vatican text in Valpy''s edition, which is the edition '
                         || 'printed at Rome in 1587 under Sixtus V: it follows Codex Vaticanus but is not a transcript '
                         || 'of it, and where the codex is lost, as for nearly all of Genesis, its editors filled the '
                         || 'gap from other manuscripts. Swete''s edition, also here, prints the codex as it stands. Who '
                         || 'first put these books into Greek is not known — the translation was made in Alexandria '
                         || 'between roughly the third and the first century BC, by different hands book by book, '
                         || 'which is why its books differ so much from one another in manner. Samuel Bagster and Sons '
                         || 'published Brenton''s edition in London in 1844 and added the Apocrypha in 1851. It arrived '
                         || 'here with no annotation at all; its lemmas come from GLAUx.'
                WHERE slug = 'GRCBRENT';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE text SET slug = 'ROBINSONPIERPONT2018' WHERE slug = 'RP2018';
                UPDATE text SET slug = 'SCRIVENER1894' WHERE slug = 'TR1894';
                UPDATE text SET slug = 'STEPHANUS1550' WHERE slug = 'TR1550';
                UPDATE text SET slug = 'TISCHENDORF1872' WHERE slug = 'TISCH';
                UPDATE text SET slug = 'WESTCOTTHORT1881' WHERE slug = 'WH1881';
                UPDATE text SET slug = 'LXX-BRENTON' WHERE slug = 'GRCBRENT';
                UPDATE text SET slug = 'LXX-SWETE' WHERE slug = 'SWETE';
                """);
        }
    }
}
