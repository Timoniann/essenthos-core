using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class CloseUpTheSpaceBeforePunctuation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The space the bible4u King James leaves between a word and the punctuation after it.
            //
            // "Thus saith the Lord , Behold" -- 2,879 words in 2,633 verses of the King James, and
            // one Ukrainian full stop. It is in the source file, not in the reader: whoever
            // flattened the small-caps LORD markup left the space that had separated the styled
            // name from what followed. The reader now closes it up as a named normalisation
            // (PRB-0151); this brings the loaded corpus with it.
            //
            // A migration rather than a reload, because reloading the King James would cascade away
            // every link into it -- its stated mapping to the Hebrew, the Berean's links to it,
            // Clear Bible's claims -- to correct a character of whitespace.
            //
            // Closing marks only. A space before "(" or the low quotation mark opens a parenthesis
            // or a quotation, a space before "-" is a dash, and "'" is an apostrophe; those are the
            // edition writing what it meant, and the pattern lists what it repairs rather than
            // taking a Unicode category that would hold them all.
            migrationBuilder.Sql(
                """
                UPDATE word
                SET trailer = regexp_replace(trailer, '^ +([,.;:?!)])', '\1')
                WHERE trailer ~ '^ +[,.;:?!)]'
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible, and it should not be. The space carried no information -- it is the
            // residue of a markup flattening -- and putting one back before every comma that
            // follows a word would damage the 2.2 million trailers that never had one.
        }
    }
}
