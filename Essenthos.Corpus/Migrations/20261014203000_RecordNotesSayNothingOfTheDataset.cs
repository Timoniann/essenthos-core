using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <summary>
    /// Three record notes ended on how the dataset held the record — Rabshakeh's office kept as his
    /// name, the two Caesars kept as its records, the Judahites as this corpus files them. A note is
    /// read on the record's page, and the records files now end them on what the text says. The
    /// loaders write a note with the record and do not write it again, so this takes the clause off
    /// the corpus already here; a note somebody changed since is left as it is.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261014203000_RecordNotesSayNothingOfTheDataset")]
    public partial class RecordNotesSayNothingOfTheDataset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH said(slug, was, now) AS (VALUES
                    ('rabshakeh', $$Akkadian rab shaqe, the chief cupbearer, by then one of the highest officers of the Assyrian court. The officer who speaks to the people on the wall in the Hebrew tongue (2KI 18:17-37; ISA 36) is known only by his office, and the dataset held the office as his name.$$,
                     $$Akkadian rab shaqe, the chief cupbearer, by then one of the highest officers of the Assyrian court. The officer who speaks to the people on the wall in the Hebrew tongue (2KI 18:17-37; ISA 36) is known only by his office, and the text calls him by it as by a name.$$),
                    ('caesar-title', $$Caesar was the family name of Julius Caesar, taken by Augustus as his heir and borne after him by every emperor, until it was the title of the office. The Gospels and Acts use it both with a name, Caesar Augustus (LUK 2:1), Tiberius Caesar (LUK 3:1) and Claudius Caesar (ACT 11:28, as the King James and the Textus Receptus read; the critical text has Claudius alone), and alone for whoever reigned: the Caesar to whom tribute is given (MAT 22:17-21) is Tiberius, and the Caesar to whom Paul appeals (ACT 25:8-12) is Nero, whom the text never names. Both of those are kept as the records the dataset holds for them, and joined here.$$,
                     $$Caesar was the family name of Julius Caesar, taken by Augustus as his heir and borne after him by every emperor, until it was the title of the office. The Gospels and Acts use it both with a name, Caesar Augustus (LUK 2:1), Tiberius Caesar (LUK 3:1) and Claudius Caesar (ACT 11:28, as the King James and the Textus Receptus read; the critical text has Claudius alone), and alone for whoever reigned: the Caesar to whom tribute is given (MAT 22:17-21) is Tiberius, and the Caesar to whom Paul appeals (ACT 25:8-12) is Nero, whom the text never names.$$),
                    ('judahites', $$Strong's entry runs the tribe and the post-exilic Jews together — a Jehudite, i.e. Judaite or Jew, or descendant of Jehudah — and this corpus does not separate them either, because nothing in the Hebrew word does. What the record refuses is the other conflation: the kingdom and the territory of Judah are a polity and a land, not a people, and an occurrence naming those is left unannotated rather than folded in here.$$,
                     $$Strong's entry runs the tribe and the post-exilic Jews together — a Jehudite, i.e. Judaite or Jew, or descendant of Jehudah — and nothing in the Hebrew word separates them. The kingdom and the territory of Judah are a polity and a land, not a people.$$))
                UPDATE entity e
                SET notes = said.now
                FROM said
                WHERE e.slug = said.slug
                  AND e.notes = said.was;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: the clauses said nothing a reader should be shown again.
        }
    }
}
