using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260920100000_TheGreekCaseIsReadFromItsOwnGroup")]
    public partial class TheGreekCaseIsReadFromItsOwnGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The Greek case, recomputed from the form code already stored beside it, by the rule the
            // parser now uses: the case group is the second group of the code, or the third for a
            // verb, and it is a case group only by its shape. Groups after it -- ATT, N, S, K, C --
            // are notes about the form. The last group used to be read, which put a case on 476
            // indeclinable numerals (A-NUI), 735 infinitives (V-AAN: the A of aorist) and 101 Attic
            // finite verbs, and missed it on the suffixed adjectives and pronouns.
            //
            // Where the new rule finds no case and the old one found one, the case came from the
            // code and goes. Where neither found one, whatever the file's own attribute supplied
            // stays, as the parser would keep it. A migration rather than a reload, because
            // reloading the text would cascade away every link into it.
            migrationBuilder.Sql(
                """
                WITH coded AS (
                    SELECT w.id,
                           split_part(w.morphology->>'form', '@@', 1) AS form
                    FROM word w
                    JOIN text t ON t.id = w.text_id
                    WHERE t.language = 'grc' AND w.morphology ? 'form'
                ),
                grouped AS (
                    SELECT id,
                           split_part(form, '-',
                               CASE WHEN split_part(form, '-', 1) = 'V' THEN 3 ELSE 2 END) AS case_group,
                           CASE
                               WHEN position('-' in form) = 0 THEN NULL
                               ELSE regexp_replace(form, '^.*-', '')
                           END AS last_group
                    FROM coded
                ),
                lettered AS (
                    SELECT id,
                           CASE
                               WHEN case_group ~ '^[NGDAV][SP][MFN]$' THEN substr(case_group, 1, 1)
                               WHEN case_group ~ '^[123][NGDAV][SP][MFN]?$' THEN substr(case_group, 2, 1)
                               WHEN case_group ~ '^[123][SP][NGDAV][SP][MFN]$' THEN substr(case_group, 3, 1)
                           END AS letter,
                           CASE
                               WHEN last_group IS NULL OR length(last_group) <> 3 THEN NULL
                               WHEN last_group ~ '^[0-9]' THEN substr(last_group, 2, 1)
                               ELSE substr(last_group, 1, 1)
                           END AS old_letter
                    FROM grouped
                ),
                named AS (
                    SELECT id,
                           CASE letter
                               WHEN 'N' THEN 'nominative'
                               WHEN 'G' THEN 'genitive'
                               WHEN 'D' THEN 'dative'
                               WHEN 'A' THEN 'accusative'
                               WHEN 'V' THEN 'vocative'
                           END AS resolved_case,
                           old_letter IN ('N', 'G', 'D', 'A', 'V') AS old_code_spoke
                    FROM lettered
                )
                UPDATE word w
                SET morphology = CASE
                        WHEN n.resolved_case IS NOT NULL
                            THEN jsonb_set(w.morphology, '{case}', to_jsonb(n.resolved_case))
                        ELSE w.morphology - 'case'
                    END
                FROM named n
                WHERE w.id = n.id
                  AND (
                      (n.resolved_case IS NOT NULL
                          AND w.morphology->>'case' IS DISTINCT FROM n.resolved_case)
                      OR (n.resolved_case IS NULL AND n.old_code_spoke AND w.morphology ? 'case')
                  )
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
