using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260911120000_TheDivineNamePointedAsElohimIsTheName")]
    public partial class TheDivineNamePointedAsElohimIsTheName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // H3069 is the divine name pointed with the vowels of Elohim -- the King James's GOD in
            // "Lord GOD" -- and BHSA marks all 583 of its words as a person's name, but no name row
            // carried the number, so none of them named anyone. The loader now writes the row on a
            // cold load; loading the encyclopedia does not run again on a loaded corpus, so this
            // writes the same row on the one already here. The annotation pass reads it on the next
            // boot. Addressed by source id, which is the key the loader matches the divine name on.
            migrationBuilder.Sql(
                """
                INSERT INTO entity_name (entity_id, label, hebrew, hebrew_transliterated, meaning,
                                         hebrew_strong_number, kind)
                SELECT e.id, 'GOD', 'יְהֹוִה', 'y-h-v-h',
                       '[the proper name of the one true G-d, pointed with the vowels of Elohim]',
                       'H3069', 'proper name'
                FROM entity e
                WHERE e.source_id = 'person:YHVH_1'
                  AND NOT EXISTS (SELECT 1 FROM entity_name n
                                  WHERE n.entity_id = e.id AND n.hebrew_strong_number = 'H3069');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM entity_name n
                USING entity e
                WHERE e.id = n.entity_id AND e.source_id = 'person:YHVH_1'
                  AND n.hebrew_strong_number = 'H3069' AND n.label = 'GOD';
                """);
        }
    }
}
