using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class StrongLexiconWithoutTheSourceArtefacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The lexicon, thrown away so the loader writes it again from the parser that now takes
            // the Greek file's artefacts out.
            //
            // What is stored today: 101 Greek rows with nothing in them at all, which are the
            // numbers Strong never assigned -- 2717, and the block 3203-3302 -- and which answer a
            // reader 200 with an empty dictionary page while a number past the end of the range
            // answers 404. And 5,520 of the 5,523 assigned Greek entries whose King James
            // renderings still carry the ":--" that separated them from the definition in the
            // printed text; the Hebrew file's preparer took it out and the Greek one did not, which
            // is why 0 of 8,674 Hebrew entries have it.
            //
            // Emptying rather than repairing in place: the repair is three rules that read each
            // other's output, and writing them a second time in SQL is how the second copy comes to
            // disagree with the first. Nothing points at these rows by key -- a word carries the
            // number as text, because 121,077 of them carry morpheme codes no entry will ever have
            // -- so deleting them cascades nothing away, and the load is two XML files and about
            // fourteen thousand rows.
            migrationBuilder.Sql("DELETE FROM strong_entry");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
