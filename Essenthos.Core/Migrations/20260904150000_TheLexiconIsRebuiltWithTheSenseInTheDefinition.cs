using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheLexiconIsRebuiltWithTheSenseInTheDefinition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The lexicon, thrown away so the loader writes it again from the parser that now knows
            // "of uncertain affinity" is a whole etymology and nothing after it is more of one.
            //
            // What is stored today: G2316 theos -- the commonest theological word in the New
            // Testament and the entry a reader is likeliest to open -- keeps "a deity, especially
            // (with G3588) the supreme Divinity;" in its derivation and answers "figuratively, a
            // magistrate; by Hebraism, very" as its whole definition. Six entries carry the sense
            // on the wrong side of that cut; four of them had no definition element at all and so
            // answered with the etymology attached to the front of the meaning.
            //
            // Emptying rather than repairing in place, for the reason the previous rebuild gives:
            // the repair is a chain of rules that read each other's output, and writing them a
            // second time in SQL is how the second copy comes to disagree with the first. Nothing
            // points at these rows by key -- a word carries the number as text -- so deleting them
            // cascades nothing away, and the load is two XML files and about fourteen thousand rows.
            migrationBuilder.Sql("DELETE FROM strong_entry");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
