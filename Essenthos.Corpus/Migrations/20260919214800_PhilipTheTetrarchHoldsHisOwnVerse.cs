using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260919214800_PhilipTheTetrarchHoldsHisOwnVerse")]
    public partial class PhilipTheTetrarchHoldsHisOwnVerse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The person register now moves this on a cold load. The encyclopedia is not loaded again
            // on a corpus that already holds it, so the same move is made here on the one already
            // written. Both records are addressed by their source ids, which is what the loader
            // matches on, and every statement is guarded on the husband still holding Luke 3:1 or on
            // rows that only exist while he does, so this is a no-op before anything is loaded and
            // on a corpus loaded after the change.
            //
            // Two sons of Herod the Great are called Philip. Luke 3:1 names the tetrarch of Ituraea
            // and Trachonitis; Matthew 14:3 and Mark 6:17 name Herodias's first husband. BibleData
            // holds them as one man, person:Philip_2, and gives him the tetrarch's verse, the title
            // it prints there and a note calling him the tetrarch. The register reads them as two and
            // added the tetrarch as essenthos:philip3. What the dataset says at Luke 3:1 moves to
            // him: its verse rows, the label it uses only there, its note, the words annotated there
            // through its verse list, and the clauses a reading made of that verse.
            migrationBuilder.Sql(
                """
                INSERT INTO entity_claim (entity_id, method, confidence, source, note)
                SELECT tetrarch.id, 'stated-by-source', NULL,
                       'BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0',
                       'files LUK 3:1 under person:Philip_2, a record it also gives another man of this name; '
                       || 'the verse, the labels it uses only there and its note are this man''s. '
                       || 'Two sons of Herod the Great are called Philip. Luke 3:1 names the tetrarch of Ituraea and '
                       || 'Trachonitis; Matthew 14:3 and Mark 6:17 name Herodias''s first husband, whom Josephus '
                       || 'calls Herod and who ruled nothing. The dataset holds both as one man, filed under the '
                       || 'husband''s verses, with the tetrarch''s verse, his title and a note calling him the '
                       || 'tetrarch.'
                FROM entity husband, entity tetrarch
                WHERE husband.source_id = 'person:Philip_2' AND tetrarch.source_id = 'essenthos:philip3'
                  AND EXISTS (SELECT 1 FROM entity_verse v
                              WHERE v.entity_id = husband.id
                                AND v.source = 'BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0'
                                AND (v.canonical_book, v.canonical_chapter, v.canonical_verse) = (42, 3, 1))
                  AND NOT EXISTS (SELECT 1 FROM entity_claim c
                                  WHERE c.entity_id = tetrarch.id AND c.method = 'stated-by-source'
                                    AND c.source = 'BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0');

                UPDATE entity tetrarch SET notes = COALESCE(tetrarch.notes, husband.notes)
                FROM entity husband
                WHERE husband.source_id = 'person:Philip_2' AND tetrarch.source_id = 'essenthos:philip3'
                  AND husband.notes IS NOT NULL
                  AND EXISTS (SELECT 1 FROM entity_verse v
                              WHERE v.entity_id = husband.id
                                AND (v.canonical_book, v.canonical_chapter, v.canonical_verse) = (42, 3, 1));

                UPDATE entity husband SET notes = NULL
                FROM entity tetrarch
                WHERE husband.source_id = 'person:Philip_2' AND tetrarch.source_id = 'essenthos:philip3'
                  AND EXISTS (SELECT 1 FROM entity_verse v
                              WHERE v.entity_id = husband.id
                                AND (v.canonical_book, v.canonical_chapter, v.canonical_verse) = (42, 3, 1));

                CREATE TEMP TABLE philip_label_at_luk_3_1 ON COMMIT DROP AS
                SELECT n.id
                FROM entity_name n
                JOIN entity husband ON husband.id = n.entity_id AND husband.source_id = 'person:Philip_2'
                WHERE EXISTS (SELECT 1 FROM entity tetrarch WHERE tetrarch.source_id = 'essenthos:philip3')
                  AND n.label IN (SELECT v.label FROM entity_verse v
                                  WHERE v.entity_id = husband.id
                                    AND (v.canonical_book, v.canonical_chapter, v.canonical_verse) = (42, 3, 1))
                  AND NOT EXISTS (SELECT 1 FROM entity_verse o
                                  WHERE o.entity_id = husband.id AND o.label = n.label
                                    AND (o.canonical_book, o.canonical_chapter, o.canonical_verse) <> (42, 3, 1));

                UPDATE entity_name n SET entity_id = tetrarch.id
                FROM entity tetrarch
                WHERE tetrarch.source_id = 'essenthos:philip3'
                  AND n.id IN (SELECT id FROM philip_label_at_luk_3_1)
                  AND NOT EXISTS (SELECT 1 FROM entity_name m
                                  WHERE m.entity_id = tetrarch.id AND m.label = n.label AND m.kind = n.kind);
                DELETE FROM entity_name n
                USING entity husband
                WHERE husband.source_id = 'person:Philip_2' AND n.entity_id = husband.id
                  AND n.id IN (SELECT id FROM philip_label_at_luk_3_1);

                UPDATE entity_verse v SET entity_id = tetrarch.id
                FROM entity husband, entity tetrarch
                WHERE husband.source_id = 'person:Philip_2' AND tetrarch.source_id = 'essenthos:philip3'
                  AND v.entity_id = husband.id
                  AND (v.canonical_book, v.canonical_chapter, v.canonical_verse) = (42, 3, 1);

                DELETE FROM word_entity a
                USING entity husband, entity tetrarch, word w, verse_reference r, word_entity b
                WHERE husband.source_id = 'person:Philip_2' AND tetrarch.source_id = 'essenthos:philip3'
                  AND a.entity_id = husband.id AND w.id = a.word_id
                  AND r.verse_id = w.verse_id AND r.is_primary
                  AND (r.canonical_book, r.canonical_chapter, r.canonical_verse) = (42, 3, 1)
                  AND b.word_id = a.word_id AND b.entity_id = tetrarch.id;
                UPDATE word_entity a SET entity_id = tetrarch.id
                FROM entity husband, entity tetrarch, word w, verse_reference r
                WHERE husband.source_id = 'person:Philip_2' AND tetrarch.source_id = 'essenthos:philip3'
                  AND a.entity_id = husband.id AND w.id = a.word_id
                  AND r.verse_id = w.verse_id AND r.is_primary
                  AND (r.canonical_book, r.canonical_chapter, r.canonical_verse) = (42, 3, 1);

                DELETE FROM entity_descriptor d
                USING entity husband, entity tetrarch
                WHERE husband.source_id = 'person:Philip_2' AND tetrarch.source_id = 'essenthos:philip3'
                  AND d.entity_id = husband.id
                  AND (d.canonical_book, d.canonical_chapter, d.canonical_verse) = (42, 3, 1);
                UPDATE entity_descriptor d SET target_entity_id = tetrarch.id
                FROM entity husband, entity tetrarch
                WHERE husband.source_id = 'person:Philip_2' AND tetrarch.source_id = 'essenthos:philip3'
                  AND d.target_entity_id = husband.id
                  AND (d.canonical_book, d.canonical_chapter, d.canonical_verse) = (42, 3, 1);

                DELETE FROM entity_relationship r
                USING entity husband, entity tetrarch
                WHERE husband.source_id = 'person:Philip_2' AND tetrarch.source_id = 'essenthos:philip3'
                  AND r.from_entity_id = husband.id AND r.source LIKE 'read from Scripture by%'
                  AND (r.canonical_book, r.canonical_chapter, r.canonical_verse) = (42, 3, 1);
                UPDATE entity_relationship r SET to_entity_id = tetrarch.id
                FROM entity husband, entity tetrarch
                WHERE husband.source_id = 'person:Philip_2' AND tetrarch.source_id = 'essenthos:philip3'
                  AND r.to_entity_id = husband.id AND r.source LIKE 'read from Scripture by%'
                  AND (r.canonical_book, r.canonical_chapter, r.canonical_verse) = (42, 3, 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to put back: the rows moved are restored to the dataset's grouping only by
            // loading the encyclopedia again from its sources, which would move them again.
        }
    }
}
