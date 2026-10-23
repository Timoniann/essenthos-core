using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheCreditsOfOurOwnRowsNameNoTicketAndQuoteNoInstruction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A credit is served with its row, so one that named a ticket of the project's board, or
            // repeated an instruction as it was given, was in the JSON of every page that showed the
            // row. The files that write these credits no longer say it, but a file already recorded is
            // skipped whole (a fold already made, the rulings of a file whose claims carry its source),
            // so the rows a database holds keep the old words until they are changed here. The new
            // words are the ones the files hold, which is also what lets a file whose source string
            // changed be recognised as recorded: the rulings ask whether a claim carries its source.
            migrationBuilder.Sql(
                """
                UPDATE entity_descriptor
                SET source = regexp_replace(source, ' \((?:PRB|TSK|FTR|NOT|DOC)-[0-9]+(?:, option [a-z])?\)$', '')
                WHERE source LIKE 'read from Scripture by an agent reading%' AND source ~ '\((?:PRB|TSK|FTR|NOT|DOC)-[0-9]+';

                UPDATE entity_descriptor_claim
                SET source = regexp_replace(source, ' \((?:PRB|TSK|FTR|NOT|DOC)-[0-9]+(?:, option [a-z])?\)$', '')
                WHERE source LIKE 'read from Scripture by an agent reading%' AND source ~ '\((?:PRB|TSK|FTR|NOT|DOC)-[0-9]+';

                UPDATE entity_relationship
                SET source = regexp_replace(source, ' \((?:PRB|TSK|FTR|NOT|DOC)-[0-9]+(?:, option [a-z])?\)$', '')
                WHERE source LIKE 'read from Scripture by an agent reading%' AND source ~ '\((?:PRB|TSK|FTR|NOT|DOC)-[0-9]+';
                """);

            migrationBuilder.Sql(
                """
                WITH said(old, new) AS (VALUES
                    ('read from Scripture by the project owner, answered on PRB-0829 2026-09-28',
                     'read from Scripture by the project owner, decided 2026-09-28'),
                    ('read from Scripture by the project owner, decided 2026-10-08 (his answer on PRB-0977: add the relation adoptive mother, Rachel of Dan, and do not draw it on the family tree)',
                     'read from Scripture by the project owner, decided 2026-10-08'))
                UPDATE entity_descriptor t SET source = said.new FROM said WHERE t.source = said.old;

                WITH said(old, new) AS (VALUES
                    ('read from Scripture by the project owner, answered on PRB-0829 2026-09-28',
                     'read from Scripture by the project owner, decided 2026-09-28'),
                    ('read from Scripture by the project owner, decided 2026-10-08 (his answer on PRB-0977: add the relation adoptive mother, Rachel of Dan, and do not draw it on the family tree)',
                     'read from Scripture by the project owner, decided 2026-10-08'))
                UPDATE entity_descriptor_claim t SET source = said.new FROM said WHERE t.source = said.old;

                WITH said(old, new) AS (VALUES
                    ('read from Scripture by the project owner, answered on PRB-0829 2026-09-28',
                     'read from Scripture by the project owner, decided 2026-09-28'),
                    ('read from Scripture by the project owner, decided 2026-10-08 (his answer on PRB-0977: add the relation adoptive mother, Rachel of Dan, and do not draw it on the family tree)',
                     'read from Scripture by the project owner, decided 2026-10-08'))
                UPDATE entity_relationship t SET source = said.new FROM said WHERE t.source = said.old;
                """);

            migrationBuilder.Sql(
                """
                UPDATE entity SET source = replace(source, ' left on PRB-0490, decided', ' left, decided')
                WHERE source LIKE 'Essenthos, two verses the tie-break of 2026-10-08 left on PRB-0490, decided%';

                UPDATE entity_claim SET source = replace(source, ' left on PRB-0490, decided', ' left, decided')
                WHERE source LIKE 'Essenthos, two verses the tie-break of 2026-10-08 left on PRB-0490, decided%';

                UPDATE entity_verse SET source = replace(source, ' left on PRB-0490, decided', ' left, decided')
                WHERE source LIKE 'Essenthos, two verses the tie-break of 2026-10-08 left on PRB-0490, decided%';

                UPDATE word_entity SET source = replace(source, ' left on PRB-0490, decided', ' left, decided')
                WHERE source LIKE 'Essenthos, two verses the tie-break of 2026-10-08 left on PRB-0490, decided%';

                UPDATE word_entity_claim SET source = replace(source, ' left on PRB-0490, decided', ' left, decided')
                WHERE source LIKE 'Essenthos, two verses the tie-break of 2026-10-08 left on PRB-0490, decided%';

                UPDATE entity_claim SET source = replace(source, ' — see DOC-0099', '')
                WHERE source = 'Essenthos, separated from BibleData''s YHVH_1 — see DOC-0099';
                """);

            migrationBuilder.Sql(
                """
                UPDATE merged_record
                SET source = replace(replace(replace(source,
                        ' held twice (PRB-0490), ', ' held twice, '),
                        ' to decide the identities as is most logical, decided', ', decided'),
                        ' ruling in chat of 2026-10-15 to follow the common identification, decided', ' ruling of 2026-10-15, decided')
                WHERE source LIKE '% held twice (PRB-0490), %'
                   OR source LIKE '% to decide the identities as is most logical, decided%'
                   OR source LIKE '% ruling in chat of 2026-10-15 to follow the common identification, decided%';
                """);

            migrationBuilder.Sql(
                """
                UPDATE word_entity
                SET note = replace(replace(note,
                        ' The owner''s word: the land where a land record is held, and the record holds',
                        ' The land is held as a record of its own, and the record holds'),
                        ' under the preceding instruction to use tribal names.',
                        ', and the gates are named for the tribes.')
                WHERE note LIKE '%The owner''s word: the land where a land record is held%'
                   OR note LIKE '% under the preceding instruction to use tribal names.';

                UPDATE word_entity_claim
                SET note = replace(replace(note,
                        ' The owner''s word: the land where a land record is held, and the record holds',
                        ' The land is held as a record of its own, and the record holds'),
                        ' under the preceding instruction to use tribal names.',
                        ', and the gates are named for the tribes.')
                WHERE note LIKE '%The owner''s word: the land where a land record is held%'
                   OR note LIKE '% under the preceding instruction to use tribal names.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: the words taken off told a reader nothing about where the claim comes from.
        }
    }
}
