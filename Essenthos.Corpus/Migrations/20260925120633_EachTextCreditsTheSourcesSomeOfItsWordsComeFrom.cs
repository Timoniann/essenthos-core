using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class EachTextCreditsTheSourcesSomeOfItsWordsComeFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "part_sources",
                table: "text",
                type: "jsonb",
                nullable: true);

            // The texts a corpus already holds, credited as a fresh load credits them: each source
            // whose words the rights note says were restored from it.
            migrationBuilder.Sql("""
                UPDATE text SET part_sources = jsonb_build_array(jsonb_build_object(
                    'name', 'King James Version (eng-kjv2006)',
                    'author', 'eBible.org, from the standardised 1769 text',
                    'licence', 'Public Domain',
                    'licenceUrl', 'https://ebible.org/eng-kjv2006/copyright.htm',
                    'url', 'https://ebible.org/find/details.php?id=eng-kjv2006',
                    'covers', 'The superscriptions of 116 psalms, which the file the text is loaded from does not print.'))
                WHERE upper(slug) = 'KJV' AND part_sources IS NULL AND rights_note LIKE '%eng-kjv2006%';

                UPDATE text SET part_sources = coalesce(part_sources, '[]'::jsonb) || jsonb_build_array(jsonb_build_object(
                    'name', 'Біблія (Огієнко)',
                    'author', 'The contributors to Ukrainian Wikisource, transcribing the 1988 printing',
                    'licence', 'CC BY-SA 4.0',
                    'licenceUrl', 'https://creativecommons.org/licenses/by-sa/4.0/',
                    'url', 'https://uk.wikisource.org/wiki/Біблія_(Огієнко)',
                    'covers', 'The first line of Psalm 7, which the file the text is loaded from lost; its stress marks are removed.'))
                WHERE upper(slug) = 'UBIO' AND rights_note LIKE '%the first line of Psalm 7%';

                UPDATE text SET part_sources = coalesce(part_sources, '[]'::jsonb) || jsonb_build_array(jsonb_build_object(
                    'name', 'Біблія (Огієнко)',
                    'author', 'The contributors to Ukrainian Wikisource, transcribing the 1988 printing',
                    'licence', 'CC BY-SA 4.0',
                    'licenceUrl', 'https://creativecommons.org/licenses/by-sa/4.0/',
                    'url', 'https://uk.wikisource.org/wiki/Біблія_(Огієнко)',
                    'covers', 'The ends of Genesis 22:19, 44:26 and 50:11, 2 Samuel 17:20, Job 2:2, Isaiah 50:9 and Habakkuk 1:8, which the file the text is loaded from cuts short; without stress marks, quotation marks and dashes.'))
                WHERE upper(slug) = 'UBIO' AND rights_note LIKE '%ends of seven verses%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "part_sources",
                table: "text");
        }
    }
}
