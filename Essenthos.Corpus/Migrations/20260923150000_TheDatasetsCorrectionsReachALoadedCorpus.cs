using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260923150000_TheDatasetsCorrectionsReachALoadedCorpus")]
    public partial class TheDatasetsCorrectionsReachALoadedCorpus : Migration
    {
        private const string BibleData =
            "BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0";

        private const string LevitesOfHezekiahsDay =
            "Read as descent and not as father and son. 2CH 31:15 names Levites of Hezekiah''s day who "
            + "served under Kore; the dataset''s own note says so, and it holds the Levites of 31:13 and 31:14 "
            + "beside them as descendants of Levi. Levi is Jacob''s son, centuries before them.";

        private const string LeviteOfNehemiahsDay =
            "Read as descent and not as father and son. NEH 13:13 names Mattaniah as the grandfather of "
            + "Hanan, a Levite treasurer of Nehemiah''s day, and the dataset holds the Levites of that day as "
            + "descendants of Levi. Levi is Jacob''s son, centuries before him.";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The loaders now write each of these right on a cold load. The encyclopedia, the chronology
            // and the world layer are not loaded again on a corpus that already holds them, so the same
            // corrections are made here on the one already written. Every row is addressed by what the
            // loaders match it on — a source id, a slug, a Wikidata item — and every statement does
            // nothing where the row is absent or already corrected, so this is a no-op on a database
            // migrated before anything is loaded and on one loaded after the change.
            //
            // The book of Jashar, which JOS 10:13 and 2SA 1:18 cite, is a writing and not a man; the
            // dataset held it as a person and gave it four verses that name Jarmuth. Its names and
            // verses go with the record, and the object record the thing loader writes takes its place.
            migrationBuilder.Sql(
                """
                DELETE FROM entity WHERE source_id = 'person:Jashar_1';
                """);

            // JER 36:9 says the ninth month; the dataset named the fast for Av, the fifth. The name is
            // this corpus's from here, and the event says so.
            migrationBuilder.Sql(
                $"""
                UPDATE event
                SET name = 'Judah''s fast in the ninth month',
                    name_source = 'generated',
                    notes = concat_ws(' ',
                        'BibleData names this "Judah''s fast during the month of Av (the ninth month)". JER 36:9, '
                        || 'the verse it cites, says the ninth month and names no month; Av is the fifth month of the '
                        || 'later Jewish calendar, and the ninth is the one ZEC 7:1 and NEH 1:1 call Chislev. The '
                        || 'name is Essenthos''s, read from the verse; the date, the description and the arithmetic '
                        || 'are BibleData''s.',
                        nullif(btrim(notes), ''))
                WHERE slug = 'judahsfastduringav' AND source = '{BibleData}'
                  AND name = 'Judah''s fast during the month of Av (the ninth month)';
                """);

            // Six Levites of Hezekiah's and Nehemiah's day were Levi's sons in the dataset, which writes
            // ancestor for every other Levite of those chapters. Both directions of each tie are read
            // as descent, as the loader now reads them.
            migrationBuilder.Sql(
                $"""
                CREATE TEMP TABLE tribal_descent (levite text, why text) ON COMMIT DROP;
                INSERT INTO tribal_descent VALUES
                    ('person:Miniamin_1', '{LevitesOfHezekiahsDay}'),
                    ('person:Jeshua_2', '{LevitesOfHezekiahsDay}'),
                    ('person:Shemaiah_12', '{LevitesOfHezekiahsDay}'),
                    ('person:Amariah_4', '{LevitesOfHezekiahsDay}'),
                    ('person:Shecaniah_3', '{LevitesOfHezekiahsDay}'),
                    ('person:Mattaniah_10', '{LeviteOfNehemiahsDay}');

                UPDATE entity_relationship r
                SET type = CASE r.type WHEN 'father' THEN 'ancestor' ELSE 'descendant' END,
                    notes = concat_ws(' ',
                        CASE WHEN nullif(btrim(r.notes), '') IS NULL THEN NULL
                             WHEN r.notes ~ '[.?!]$' THEN r.notes
                             ELSE r.notes || '.' END,
                        d.why)
                FROM tribal_descent d, entity levi, entity levite
                WHERE levi.source_id = 'person:Levi_1' AND levite.source_id = d.levite
                  AND r.source = '{BibleData}'
                  AND ((r.type = 'father' AND r.from_entity_id = levi.id AND r.to_entity_id = levite.id)
                       OR (r.type = 'son' AND r.from_entity_id = levite.id AND r.to_entity_id = levi.id));
                """);

            // Wikidata's Solomon's Temple is the beginning of the construction BibleData dates from
            // 1 Kings 6:1. The scripture row is told Wikidata's year and the world row goes, its dates
            // with it, exactly as the world loader now hands over its other duplicates.
            migrationBuilder.Sql(
                """
                UPDATE event scripture
                SET notes = concat_ws(' ', nullif(btrim(scripture.notes), ''),
                    'Wikidata has this event as "' || world.name || '" at '
                    || CASE WHEN world.year_from_creation - 3961 <= 0
                            THEN (1 - (world.year_from_creation - 3961))::text || ' BCE'
                            ELSE 'AD ' || (world.year_from_creation - 3961)::text END
                    || ' (' || world.uri || '), where this row holds '
                    || CASE WHEN scripture.year_from_creation - 3961 <= 0
                            THEN (1 - (scripture.year_from_creation - 3961))::text || ' BCE'
                            ELSE 'AD ' || (scripture.year_from_creation - 3961)::text END
                    || '. One event under two datasets, '
                    || abs(world.year_from_creation - scripture.year_from_creation)::text
                    || ' years apart — so world history draws no second mark for it and the disagreement is here.')
                FROM event world
                WHERE world.realm = 'world' AND world.uri = 'http://www.wikidata.org/entity/Q223644'
                  AND scripture.slug = 'beginfirsttempleconstruction' AND scripture.realm = 'scripture'
                  AND scripture.year_from_creation IS NOT NULL AND world.year_from_creation IS NOT NULL
                  AND coalesce(scripture.notes, '') NOT LIKE '%Q223644%';

                DELETE FROM event world
                WHERE world.realm = 'world' AND world.uri = 'http://www.wikidata.org/entity/Q223644'
                  AND EXISTS (SELECT 1 FROM event scripture
                              WHERE scripture.slug = 'beginfirsttempleconstruction'
                                AND scripture.notes LIKE '%Q223644%')
                  AND NOT EXISTS (SELECT 1 FROM period p WHERE world.id IN (p.start_event_id, p.end_event_id));
                """);

            // A dataset's row identifier was written into the claim that says a record rests on it —
            // person:David_1 — and a reader was shown it. The claims say the same thing without it.
            migrationBuilder.Sql(
                """
                UPDATE entity_claim
                SET note = regexp_replace(note, '^holds this (one|man) as .+?, which is where this record''s',
                                          'holds this \1 as a record of its own, which is where this record''s')
                WHERE note ~ '^holds this (one|man) as (person|place|essenthos):.+?, which is where this record''s';

                UPDATE entity_claim
                SET note = regexp_replace(note, '^files ([1-4]?[A-Z]{2,3} [0-9]+:[0-9]+) under (person|place):[^,]+, ',
                                          'files \1 under ')
                WHERE note ~ '^files [1-4]?[A-Z]{2,3} [0-9]+:[0-9]+ under (person|place):[^,]+, a record it also gives';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to put back: a deleted record, a world row and a dataset's wording are restored by
            // loading the sources again, which is the only honest way to have them back.
        }
    }
}
