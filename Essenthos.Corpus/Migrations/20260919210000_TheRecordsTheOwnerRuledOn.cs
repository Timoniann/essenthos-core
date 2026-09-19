using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260919210000_TheRecordsTheOwnerRuledOn")]
    public partial class TheRecordsTheOwnerRuledOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The loaders now write all three of these right on a cold load. The encyclopedia is not
            // loaded again on a corpus that already holds it, so the same corrections are made here
            // on the one already written. Every record is addressed by its source id, which is the
            // key the loaders match it on, and every statement does nothing where the record is
            // absent, so this is a no-op on a database migrated before anything is loaded.
            //
            // Talitha is ταλιθα, Aramaic for 'girl', which Jesus says to Jairus's daughter and MRK
            // 5:41 translates itself. The dataset held the word as a girl's name. Her names, verses
            // and relationships go with the record.
            migrationBuilder.Sql(
                """
                DELETE FROM entity WHERE source_id = 'person:Talitha_1';
                """);

            // Abez and Ebez are one town, named once at JOS 19:20: the King James spells it Abez
            // and the gazetteer surveys it as Ebez. The record the place register wrote for the
            // number is folded into the surveyed one: its name row, its annotations and its verse
            // move across, and the surveyed place becomes the register's, as every other place the
            // register reaches is, with the gazetteer's testimony kept as a claim beside it.
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE abez_into_ebez ON COMMIT DROP AS
                SELECT abez.id AS abez, ebez.id AS ebez
                FROM entity abez, entity ebez
                WHERE abez.source_id = 'essenthos:h77' AND ebez.open_bible_id = 'acd9b19';

                INSERT INTO entity_claim (entity_id, method, confidence, source, note)
                SELECT m.ebez, 'stated-by-source', NULL, e.source,
                       'surveyed as acd9b19, which is where this record''s coordinates come from and whose they stay'
                FROM abez_into_ebez m JOIN entity e ON e.id = m.ebez
                WHERE e.source <> 'Essenthos, from the place names Strong''s Dictionary heads';

                UPDATE entity e SET source = 'Essenthos, from the place names Strong''s Dictionary heads'
                FROM abez_into_ebez m WHERE e.id = m.ebez;

                UPDATE entity_claim c SET entity_id = m.ebez FROM abez_into_ebez m WHERE c.entity_id = m.abez;
                UPDATE entity_name n SET entity_id = m.ebez FROM abez_into_ebez m WHERE n.entity_id = m.abez;
                UPDATE entity_name n SET aspect_of_entity_id = m.ebez
                FROM abez_into_ebez m WHERE n.aspect_of_entity_id = m.abez;
                UPDATE entity_verse v SET entity_id = m.ebez FROM abez_into_ebez m WHERE v.entity_id = m.abez;

                DELETE FROM word_entity a USING abez_into_ebez m, word_entity b
                WHERE a.entity_id = m.abez AND b.entity_id = m.ebez AND b.word_id = a.word_id;
                UPDATE word_entity a SET entity_id = m.ebez FROM abez_into_ebez m WHERE a.entity_id = m.abez;

                UPDATE entity_relationship r SET from_entity_id = m.ebez FROM abez_into_ebez m WHERE r.from_entity_id = m.abez;
                UPDATE entity_relationship r SET to_entity_id = m.ebez FROM abez_into_ebez m WHERE r.to_entity_id = m.abez;
                UPDATE entity_descriptor d SET target_entity_id = m.ebez FROM abez_into_ebez m WHERE d.target_entity_id = m.abez;
                UPDATE entity_alternative a SET alternative_entity_id = m.ebez FROM abez_into_ebez m WHERE a.alternative_entity_id = m.abez;
                UPDATE entity e SET origin_entity_id = m.ebez FROM abez_into_ebez m WHERE e.origin_entity_id = m.abez;
                UPDATE strong_gentilic g SET origin_entity_id = m.ebez FROM abez_into_ebez m WHERE g.origin_entity_id = m.abez;
                UPDATE event v SET entity_id = m.ebez FROM abez_into_ebez m WHERE v.entity_id = m.abez;
                UPDATE period p SET entity_id = m.ebez FROM abez_into_ebez m WHERE p.entity_id = m.abez;

                DELETE FROM entity e USING abez_into_ebez m WHERE e.id = m.abez;
                """);

            // The Arodites are the family of Arod son of Gad (NUM 26:17), whom GEN 46:16 calls Arodi.
            // Strong derives the gentilic from the Arvadite, which is spelled with the same letters,
            // and the record was named after Arvad son of Canaan. The derivation is refused now and
            // the record rests on Strong's definition, as the peoples he derives from nothing do.
            migrationBuilder.Sql(
                """
                DELETE FROM strong_gentilic WHERE strong_number = 'H722' AND origin_number = 'H721';

                UPDATE entity p
                SET origin_entity_id = arod.id,
                    distinguisher = 'an Arodite or descendant of Arod, as Strong''s Dictionary describes them',
                    source = 'Essenthos, from the peoples Strong''s Dictionary describes and derives from no word he numbers'
                FROM entity arod
                WHERE p.source_id = 'essenthos:arodites' AND arod.source_id = 'person:Arodi_1';

                UPDATE entity_claim c
                SET note = 'H722: "an Arodite or descendant of Arod"'
                FROM entity p
                WHERE p.id = c.entity_id AND p.source_id = 'essenthos:arodites' AND c.method = 'stated-by-source';

                INSERT INTO entity_claim (entity_id, method, confidence, source, note)
                SELECT p.id, 'manual', NULL,
                       'Essenthos, from the peoples Strong''s Dictionary describes and derives from no word he numbers',
                       'The family of Arod son of Gad, Numbers 26:17: ''of Arod, the family of the Arodites''. '
                       || 'Genesis 46:16 names the same son of Gad Arodi, the word this gentilic is spelled with. '
                       || 'Strong defines the entry as an Arodite or descendant of Arod, but derives it from H721, '
                       || 'the Arvadite of Genesis 10:18, which is spelled with the same letters; a people is not '
                       || 'named after another people, so the derivation is refused and the record rests on his '
                       || 'definition and on the verse.'
                FROM entity p
                WHERE p.source_id = 'essenthos:arodites'
                  AND NOT EXISTS (SELECT 1 FROM entity_claim c WHERE c.entity_id = p.id AND c.method = 'manual');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to put back: a deleted record and a merged one are restored by loading the
            // encyclopedia again from its sources, which is the only honest way to have them back.
        }
    }
}
