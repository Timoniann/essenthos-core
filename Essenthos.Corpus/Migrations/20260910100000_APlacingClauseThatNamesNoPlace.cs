using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260910100000_APlacingClauseThatNamesNoPlace")]
    public partial class APlacingClauseThatNamesNoPlace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The clauses already loaded that place something somewhere that is not a place.
            //
            // A tribe is three records in this encyclopedia -- a man, a people and a territory --
            // and a pass reading "Bethlehem, a city in Judah" means the territory while the name it
            // reaches for is most often the patriarch's. The sentence reads correctly and the link
            // under it is false: follow "a city in Judah" and you arrive at Jacob's son. 107 of
            // them, 60 the twelve tribes (PRB-0480).
            //
            // The loader refuses these now, but a refusal only governs what it is asked to load,
            // and these were loaded before it existed. What happened to this database is a fact
            // about it, so the relations are written out here rather than read from
            // PlacingRelations: retuning that set later must not silently change what this
            // migration meant when it ran.
            //
            // The relationships go with the clauses because they are read off them, and their own
            // loader writes only for an entity it holds no rows for -- leaving them would keep the
            // false link on the page while the clause under it was gone. One statement each, in one
            // transaction, which every migration already is.
            migrationBuilder.Sql(
                """
                DELETE FROM entity_relationship r
                USING entity t
                WHERE t.id = r.to_entity_id
                  AND r.source LIKE 'read from Scripture%'
                  AND r.type IN ('lived-in', 'buried-in', 'from-place', 'city-in', 'region-of',
                                 'river-of', 'mountain-in', 'gate-of', 'near')
                  AND t.kind <> 'place';
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM entity_descriptor d
                USING entity t
                WHERE t.id = d.target_entity_id
                  AND d.relation IN ('lived-in', 'buried-in', 'from-place', 'city-in', 'region-of',
                                     'river-of', 'mountain-in', 'gate-of', 'near')
                  AND t.kind <> 'place';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to put back. What was removed is in the generation pass's files under
            // Resources, and a corpus loaded from them again would refuse it a second time.
        }
    }
}
