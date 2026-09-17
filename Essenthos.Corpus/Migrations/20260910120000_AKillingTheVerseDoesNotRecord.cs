using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260910120000_AKillingTheVerseDoesNotRecord")]
    public partial class AKillingTheVerseDoesNotRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Three killings the cited verse does not record, each read by a pass that did not
            // believe it either. Of the 76 killing clauses loaded, these are the only ones whose King
            // James verse carries no word of killing at all:
            //
            //   judas    killer-of jesus      MAT 27:3   0.3  "Judas, which had betrayed him"
            //   joash-4  killer-of amaziah    2CH 25:23  0    Joash took Amaziah captive
            //   haman    killed-by ahasuerus  EST 7:6    0.1  Esther's accusation; the hanging is 7:10
            //
            // Three rows are not worth a loader rule, so they are named here, and removed from the
            // descriptor files as well: a claim left in a file comes back on a cold load. The
            // relationships read off them go first, because their own loader writes only for an
            // entity it holds no rows for, and leaving one would keep "Judas, killer of Jesus" on the
            // page after the clause under it was gone.
            const string Wrong =
                """
                (VALUES ('judas', 'killer-of', 'jesus', 40, 27, 3),
                        ('joash-4', 'killer-of', 'amaziah', 14, 25, 23),
                        ('haman', 'killed-by', 'ahasuerus', 17, 7, 6))
                    AS wrong(entity, relation, target, book, chapter, verse)
                """;

            migrationBuilder.Sql(
                $"""
                DELETE FROM entity_relationship rel
                USING entity f, entity t, {Wrong}
                WHERE f.id = rel.from_entity_id AND t.id = rel.to_entity_id
                  AND f.slug = wrong.entity AND t.slug = wrong.target AND rel.type = wrong.relation
                  AND rel.canonical_book = wrong.book AND rel.canonical_chapter = wrong.chapter
                  AND rel.canonical_verse = wrong.verse
                  AND rel.source LIKE 'read from Scripture%';
                """);

            migrationBuilder.Sql(
                $"""
                DELETE FROM entity_descriptor d
                USING entity e, entity t, {Wrong}
                WHERE e.id = d.entity_id AND t.id = d.target_entity_id
                  AND e.slug = wrong.entity AND t.slug = wrong.target AND d.relation = wrong.relation
                  AND d.canonical_book = wrong.book AND d.canonical_chapter = wrong.chapter
                  AND d.canonical_verse = wrong.verse;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to put back. The claims are removed from the descriptor files too, so there is
            // no record of them left to restore from, which is the point.
        }
    }
}
