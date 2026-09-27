using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260927190000_AClauseSaysOfARecordOnlyWhatItsKindCanBe")]
    public partial class AClauseSaysOfARecordOnlyWhatItsKindCanBe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The clauses already loaded that say of a record what its kind cannot be: the brook
            // Besor as David's companion (1SA 30:9), Assyria as the king of Tiglath-pileser, a town
            // of Chronicles as somebody's son, a town as "of the tribe of" Benjamin. 93 of them,
            // every one a model's reading.
            //
            // The loader refuses these now, but a refusal only governs what it is asked to load, and
            // these were loaded before it existed. What happened to this database is a fact about
            // it, so the kinds are written out here rather than read from DescriptorSubjects:
            // retuning that table later must not silently change what this migration meant when it
            // ran. A decision someone made is left as it was.
            //
            // The relationships read off the clauses go with them, as they did for the clauses that
            // placed something somewhere that is not a place.
            const string inadmissible =
                """
                (subject.kind NOT IN ('person', 'title')
                     AND relation IN ('son-of', 'daughter-of', 'father-of', 'mother-of', 'brother-of',
                         'sister-of', 'husband-of', 'wife-of', 'half-brother-of', 'half-sister-of',
                         'grandfather-of', 'grandmother-of', 'grandson-of', 'granddaughter-of',
                         'uncle-of', 'aunt-of', 'nephew-of', 'niece-of', 'father-in-law-of',
                         'mother-in-law-of', 'son-in-law-of', 'daughter-in-law-of',
                         'brother-in-law-of', 'sister-in-law-of', 'concubine-of', 'cousin-of',
                         'king-of', 'queen-of', 'prophet-to', 'priest-of', 'judge-of',
                         'high-priest-of', 'commander-of', 'governor-of', 'tetrarch-of', 'master-of',
                         'disciple-of', 'apostle-of', 'scribe-of', 'companion-of', 'teacher-of',
                         'raped-by', 'raper-of', 'creator-of', 'angel-of'))
                OR (subject.kind NOT IN ('person', 'title', 'people')
                     AND relation IN ('ancestor-of', 'descendant-of', 'servant-of', 'killed-by',
                         'killer-of', 'heir-of', 'of-tribe', 'of-people', 'from-place', 'lived-in',
                         'buried-in'))
                OR (subject.kind NOT IN ('person', 'title', 'people', 'place')
                     AND relation IN ('ally-of', 'supporter-of', 'supported-by', 'exiler-of',
                         'exiled-by', 'inherited-by'))
                OR (subject.kind <> 'people' AND relation = 'descendants-of')
                OR (subject.kind NOT IN ('place', 'people') AND relation = 'near')
                OR (subject.kind <> 'place'
                     AND relation IN ('city-in', 'region-of', 'river-of', 'mountain-in', 'gate-of'))
                """;

            migrationBuilder.Sql(
                $"""
                 DELETE FROM entity_relationship r
                 USING entity subject
                 WHERE subject.id = r.from_entity_id
                   AND r.source LIKE 'read from Scripture%'
                   AND r.source NOT LIKE '%the project owner%'
                   AND ({inadmissible.Replace("relation", "r.type")});
                 """);

            migrationBuilder.Sql(
                $"""
                 DELETE FROM entity_descriptor d
                 USING entity subject
                 WHERE subject.id = d.entity_id
                   AND d.method = 'model-reading'
                   AND ({inadmissible.Replace("relation", "d.relation")});
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
