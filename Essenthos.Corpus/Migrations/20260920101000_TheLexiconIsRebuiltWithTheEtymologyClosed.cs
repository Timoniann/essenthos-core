using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260920101000_TheLexiconIsRebuiltWithTheEtymologyClosed")]
    public partial class TheLexiconIsRebuiltWithTheEtymologyClosed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The lexicon, thrown away so the loader writes it again from the parser that now closes
            // an etymology cut through a parenthesis. 108 Greek entries open their definition with
            // the tail of their own derivation: G109 ἀήρ answers "by analogy, to blow); "air" (as
            // naturally circumambient)", so a reader looking up "air" is told it means "to blow".
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
