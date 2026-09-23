using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260923200000_AGreekNameIsFiledUnderItsOwnNumber")]
    public partial class AGreekNameIsFiledUnderItsOwnNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The encyclopedia loader now reads these on a cold load. It is not loaded again on a
            // corpus that already holds it, so the same names are written here on the one already
            // there. Every row is addressed by its record's source id, its label and the number the
            // loader used to write, so a row already corrected, or one a later dataset release changed,
            // is left as it is, and a second run does nothing.
            //
            // The dataset writes Greek numbers with a trailing letter of its own, and the loader used
            // to drop the letter as it does the Hebrew homograph mark. For Judas and Judah that left
            // G2492, Job; for Matthat, Naaman, Menna and Admin a word that is not a name at all. Each
            // is now the Strong number the Greek witnesses write that name under, or none where
            // Strong's has none.
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE lettered_greek (source_id text, label text, was text, now text) ON COMMIT DROP;
                INSERT INTO lettered_greek VALUES
                    ('person:Judah_1', 'Judah', 'G2492', 'G2455'),
                    ('person:David_1', 'King of Judah', 'G935,G2492', 'G935,G2455'),
                    ('person:Rehoboam_1', 'King of Judah', 'G935,G2492', 'G935,G2455'),
                    ('person:Asa_1', 'King of Judah', 'G936,G760,G1909,G2492', 'G936,G760,G1909,G2455'),
                    ('person:Jehoshaphat_3', 'King of Judah', 'G935,G2492', 'G935,G2455'),
                    ('person:Jehoram_1', 'King of Judah', 'G935,G2492', 'G935,G2455'),
                    ('person:Naaman_2', 'Naaman', 'G3483', 'G3497'),
                    ('person:Ahaziah_2', 'King of Judah', 'G935,G2492', 'G935,G2455'),
                    ('person:Joash_3', 'King of Judah', 'G935,G2492', 'G935,G2455'),
                    ('person:Amaziah_1', 'King of Judah', 'G935,G2492', 'G935,G2455'),
                    ('person:Azariah_3', 'King of Judah', 'G935,G2492', 'G935,G2455'),
                    ('person:Jotham_2', 'King of Judah', 'G935,G2492', 'G935,G2455'),
                    ('person:Ahaz_1', 'King of Judah', 'G935,G2492', 'G935,G2455'),
                    ('person:Hezekiah_1', 'King of Judah', 'G935,G2492', 'G935,G2455'),
                    ('person:Manasseh_3', 'King of Judah', 'G935,G2492', 'G935,G2455'),
                    ('person:Josiah_1', 'King of Judah', 'G935,G2492', 'G935,G2455'),
                    ('person:Jehoiachin_1', 'King of Judah', 'G935,G2492', 'G935,G2455'),
                    ('person:Eber_3', 'Eber', 'G2492', 'G5601'),
                    ('person:Judah_3', 'Judah', 'G2492', 'G2455'),
                    ('person:Eliakim_2', 'King of Judah', 'G935,G2492', 'G935,G2455'),
                    ('person:Mattaniah_1', 'King of Judah', 'G935,G2492', 'G935,G2455'),
                    ('person:Simon_2', 'Simon the zealot', 'G4613,G3588,G2580', 'G4613,G3588,G2581'),
                    ('person:Judas_1', 'Judas', 'G2492', 'G2455'),
                    ('person:Judas_1', 'Judas Iscariot', 'G2492,G2469', 'G2455,G2469'),
                    ('person:Judas_2', 'Judas', 'G2492', 'G2455'),
                    ('person:Matthat_1', 'Matthat', 'G3102', 'G3158'),
                    ('person:Matthat_2', 'Matthat', 'G3102', 'G3158'),
                    ('person:Judah_5', 'Judah', 'G2492', 'G2455'),
                    ('person:Menna_1', 'Menna', 'G3303', 'G3104'),
                    ('person:Admin_1', 'Admin', 'G95', NULL),
                    ('person:Thaddaeus_1', 'Judas', 'G2492', 'G2455'),
                    ('person:Judas_4', 'Judas', 'G2492', 'G2455'),
                    ('person:Judas_5', 'Judas', 'G2492', 'G2455'),
                    ('person:Titius_1', 'Titius Justus', 'G5101,G2459', 'G2459'),
                    ('person:Judas_2', 'Jude', 'G2492', 'G2455');

                UPDATE entity_name n SET greek_strong_number = l.now
                FROM lettered_greek l, entity e
                WHERE e.id = n.entity_id AND e.source_id = l.source_id AND n.label = l.label
                  AND n.greek_strong_number = l.was;
                """);

            // Paul answers to Σαούλ at Acts 9:4, 9:17, 22:7, 22:13 and 26:14, which the dataset files
            // under him but whose number it gives only to the king.
            migrationBuilder.Sql(
                """
                INSERT INTO entity_name (entity_id, label, hebrew, hebrew_transliterated, greek,
                                         greek_transliterated, greek_strong_number, kind)
                SELECT paul.id, 'Saul', 'שָׁאוּל', 'shaul', 'Σαούλ', 'Saoúl', 'G4549', 'proper name'
                FROM entity paul
                WHERE paul.source_id = 'person:Saul_2'
                  AND NOT EXISTS (SELECT 1 FROM entity_name n
                                  WHERE n.entity_id = paul.id AND n.greek_strong_number = 'G4549');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to put back: the numbers it replaces are the ones the loader no longer writes,
            // and loading the encyclopedia again from its sources writes these.
        }
    }
}
