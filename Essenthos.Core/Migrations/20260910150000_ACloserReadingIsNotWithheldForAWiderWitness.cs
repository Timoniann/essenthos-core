using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260910150000_ACloserReadingIsNotWithheldForAWiderWitness")]
    public partial class ACloserReadingIsNotWithheldForAWiderWitness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The relationships read off the descriptor clauses are derived rows: their loader writes
            // them from entity_descriptor on the next boot, for every entity that holds none. A reading
            // closer than the witness -- son of, where BibleData says descendant -- used to be withheld
            // as a disagreement, and on a loaded corpus the entities it belongs to already hold rows,
            // so the loader would never look at it again. Taking the derived rows back is how a loaded
            // corpus reaches what a cold one now loads. BibleData's rows are not touched.
            migrationBuilder.Sql(
                """
                DELETE FROM entity_relationship
                WHERE method = 'model-reading' AND source LIKE 'read from Scripture%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to put back: the rows are rebuilt from the clauses on the boot that follows.
        }
    }
}
