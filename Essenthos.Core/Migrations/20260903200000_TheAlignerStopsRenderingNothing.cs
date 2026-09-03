using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheAlignerStopsRenderingNothing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Aligner links standing on a word that writes nothing at all.
            //
            // A verse that opens with punctuation has nothing before it to hang that punctuation
            // on, so the tokeniser emits a word with an empty surface. It carries no lemma and no
            // Strong number either, so the aligner writes it as one placeholder character -- and
            // every such word in the text is that same character, which means the model pools them
            // into a single lexical item and pairs it with whatever stands near it. The result was
            // links saying a Hebrew word is rendered by an opening quotation mark.
            //
            // The 92 links a source states on these same words are left alone. This deletes only
            // what a model proposed, and every one of the deleted links has the wordless word as
            // the only word on its side, so nothing is left as a narrower claim.
            //
            // A Hebrew word that has assimilated into the letter before it is a real word the
            // annotation records, it carries a Strong number, and its links are correct and stay.
            migrationBuilder.Sql(
                """
                DELETE FROM link
                WHERE method = 'aligner'
                  AND id IN (
                    SELECT lw.link_id
                    FROM link_word lw
                    JOIN word w ON w.id = lw.word_id
                    WHERE w.text = ''
                      AND w.strong_number IS NULL
                      AND (w.lemma IS NULL OR w.lemma = ''))
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible. These are a model's guesses about a character that is not a word, and
            // the run that produced them is not worth reproducing.
        }
    }
}
