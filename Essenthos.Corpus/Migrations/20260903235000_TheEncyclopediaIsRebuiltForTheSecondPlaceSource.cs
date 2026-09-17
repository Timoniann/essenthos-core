using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheEncyclopediaIsRebuiltForTheSecondPlaceSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Empties the encyclopedia so the loader builds it again, this time with a second source
            // for places.
            //
            // Loading the encyclopedia is idempotent on whether any entity exists at all, which is
            // right for a start-up pass and wrong exactly once: when the load itself has learnt
            // something new. It has -- BibleData knows 118 places and refers to them only through
            // Genesis and Exodus, and OpenBible knows 1,342 across 61 books. Nothing short of a
            // rebuild reaches that, and a migration that inserted the rows itself would be a second
            // implementation of the loader, disagreeing with it the first time either changed.
            //
            // Nothing outside the encyclopedia points at an entity. A word does not: the reader's
            // entity annotation is resolved through the canonical address, not through a foreign
            // key, so no text, link or word is touched by this. Checked against the schema rather
            // than assumed -- entity_name, entity_relationship and entity_verse cascade, and event
            // and period are cleared here because they do not. Periods go before events: a period
            // names the event it starts and the event it ends, so deleting the events first is
            // refused by that foreign key rather than cascading.
            //
            // Everything deleted is rebuilt from files in Resources/ that the manifest fingerprints.
            migrationBuilder.Sql(
                """
                DELETE FROM event_date;
                DELETE FROM period;
                DELETE FROM event;
                DELETE FROM chronology;
                DELETE FROM entity;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: the rows come from the loader, and it is what puts them back.
        }
    }
}
