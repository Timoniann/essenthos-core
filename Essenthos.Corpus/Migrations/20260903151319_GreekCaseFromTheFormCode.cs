using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class GreekCaseFromTheFormCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The Greek case, recomputed from the form code already stored beside it.
            //
            // Nestle 1904 writes case="neuter" -- a gender -- where the word is nominative, and
            // never writes "nominative" at all. Measured over the file: the attribute stands against
            // a nominative form code 20,629 times, the code carries a case for 12,401 words the
            // attribute leaves blank, and where both speak sensibly they agree 49,043 times. The
            // parser now reads the code (PRB-0066); this brings the loaded corpus with it.
            //
            // A migration rather than a reload, because reloading the text would cascade away every
            // link into it -- the stated King James and Berean mappings, Clear Bible's claims, the
            // aligner's runs -- to correct a field that is computable from a field already present.
            //
            // The rule is the parser's: the last hyphen-separated group, where it is three
            // characters, is case, number and gender; a pronoun writes its person first so the case
            // is the second character; a code with no hyphen is a part of speech and has no case.
            migrationBuilder.Sql(
                """
                WITH parsed AS (
                    SELECT w.id,
                           w.morphology,
                           CASE
                               WHEN position('-' in (w.morphology->>'form')) = 0 THEN NULL
                               ELSE right(w.morphology->>'form',
                                          length(w.morphology->>'form')
                                          - length(regexp_replace(w.morphology->>'form', '-[^-]*$', '')) - 1)
                           END AS last_group
                    FROM word w
                    JOIN text t ON t.id = w.text_id
                    WHERE t.language = 'grc' AND w.morphology ? 'form'
                ),
                resolved AS (
                    SELECT id, morphology,
                           CASE
                               WHEN last_group IS NULL OR length(last_group) <> 3 THEN NULL
                               WHEN last_group ~ '^[0-9]' THEN substr(last_group, 2, 1)
                               ELSE substr(last_group, 1, 1)
                           END AS letter
                    FROM parsed
                ),
                named AS (
                    SELECT id, morphology,
                           CASE letter
                               WHEN 'N' THEN 'nominative'
                               WHEN 'G' THEN 'genitive'
                               WHEN 'D' THEN 'dative'
                               WHEN 'A' THEN 'accusative'
                               WHEN 'V' THEN 'vocative'
                               ELSE NULL
                           END AS resolved_case
                    FROM resolved
                )
                UPDATE word w
                SET morphology = CASE
                        WHEN n.resolved_case IS NOT NULL
                            THEN jsonb_set(w.morphology, '{case}', to_jsonb(n.resolved_case))
                        -- The code is silent. Keep a case the attribute named only if it is a case:
                        -- it says "neuter" 138 times in this position, and the gender field already
                        -- carries the gender, correctly, everywhere.
                        WHEN w.morphology->>'case' IN
                            ('nominative', 'genitive', 'dative', 'accusative', 'vocative')
                            THEN w.morphology
                        ELSE w.morphology - 'case'
                    END
                FROM named n
                WHERE w.id = n.id
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
