using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <summary>
    /// The Synodal's square brackets, out of the text and into word groups, for the rows already
    /// loaded. A text that is loaded is never loaded again, so the parser change alone would leave
    /// this corpus reading the brackets as scripture for ever.
    ///
    /// It reproduces what the parser now does rather than approximating it: the brackets live only
    /// in trailers, all 4,247 of them are balanced, none nests, and the tokeniser gave a
    /// verse-opening bracket a word of its own 145 times. So the depth before each word says which
    /// words a bracket covers, a closing bracket in the preceding trailer says where one span ends
    /// and the next begins, and the wordless bracket rows go.
    /// </summary>
    public partial class SynodalSuppliedWords : Migration
    {
        /// <summary>
        /// Every word of every verse that carries a bracket, with the bracket depth standing before
        /// it and the trailer of the word before it — the two things that decide which span a word
        /// belongs to. Scoped to the three bible4u translations: the Greek editions bracket text
        /// their editors doubt, which is a different statement and not this one.
        /// </summary>
        private const string ScanBrackets =
            """
            CREATE TEMP TABLE bracket_scan ON COMMIT DROP AS
            WITH affected AS (
                SELECT DISTINCT w.verse_id
                FROM word w
                JOIN text t ON t.id = w.text_id
                WHERE t.slug IN ('kjv', 'rusv', 'ukr')
                  AND (w.trailer LIKE '%[%' OR w.trailer LIKE '%]%')
            ),
            scanned AS (
                SELECT w.id, w.verse_id, w."position", w."text", w.trailer,
                       (length(w.trailer) - length(replace(w.trailer, '[', '')))
                           - (length(w.trailer) - length(replace(w.trailer, ']', ''))) AS balance
                FROM word w
                JOIN affected a ON a.verse_id = w.verse_id
            )
            SELECT s.id, s.verse_id, s."position", s."text", s.trailer,
                   coalesce(sum(s.balance) OVER (
                       PARTITION BY s.verse_id ORDER BY s."position"
                       ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS depth,
                   lag(s.trailer) OVER (PARTITION BY s.verse_id ORDER BY s."position") AS previous_trailer
            FROM scanned s
            """;

        /// <summary>
        /// Which span each bracketed word belongs to, numbered from one within its verse. A span
        /// begins where the word before it was not bracketed, and also where it was but its trailer
        /// closed the bracket — the Synodal writes "[для] [управления]" 79 times, and running the
        /// two together would report one editorial mark where the edition made two.
        /// </summary>
        private const string NumberSpans =
            """
            CREATE TEMP TABLE supplied_span ON COMMIT DROP AS
            WITH flagged AS (
                SELECT s.id, s.verse_id, s."position", s.previous_trailer,
                       s.depth > 0 AND s."text" <> '' AS supplied,
                       lag(s.depth > 0 AND s."text" <> '')
                           OVER (PARTITION BY s.verse_id ORDER BY s."position") AS previous_supplied
                FROM bracket_scan s
            ),
            numbered AS (
                SELECT f.id, f.verse_id, f."position", f.supplied,
                       sum(CASE WHEN f.supplied
                                 AND (coalesce(f.previous_supplied, false) IS NOT TRUE
                                      OR strpos(coalesce(f.previous_trailer, ''), ']') > 0)
                                THEN 1 ELSE 0 END)
                           OVER (PARTITION BY f.verse_id ORDER BY f."position") AS span
                FROM flagged f
            )
            SELECT n.id, n.verse_id, n."position", n.span
            FROM numbered n
            WHERE n.supplied
            """;

        /// <summary>
        /// One row per span, in the text order a group's position counts in: the book as this text
        /// orders it, then the chapter, the verse, its label, and where in the verse the span
        /// opens.
        /// </summary>
        private const string OrderSpans =
            """
            CREATE TEMP TABLE supplied_group ON COMMIT DROP AS
            WITH spans AS (
                SELECT s.verse_id, s.span, v.text_id, b."position" AS book, v.chapter_number,
                       v.number AS verse_number, v.label, min(s."position") AS opens_at
                FROM supplied_span s
                JOIN verse v ON v.id = s.verse_id
                JOIN book b ON b.id = v.book_id
                GROUP BY s.verse_id, s.span, v.text_id, b."position", v.chapter_number, v.number, v.label
            )
            SELECT verse_id, span, text_id,
                   row_number() OVER (PARTITION BY text_id
                                      ORDER BY book, chapter_number, verse_number, label, opens_at)::int
                       AS ordinal
            FROM spans
            """;

        /// <summary>
        /// The verses that lose a word, taken before the deletion: a bracket that opened a verse had
        /// nothing to hang on and became a word with no letters, and with the bracket gone there is
        /// nothing left of it at all. Their positions have to close up behind it.
        /// </summary>
        private const string FindWordlessBrackets =
            """
            CREATE TEMP TABLE wordless_bracket ON COMMIT DROP AS
            SELECT id, verse_id FROM bracket_scan
            WHERE "text" = '' AND replace(replace(trailer, '[', ''), ']', '') = ''
            """;

        /// <summary>
        /// Positions are shifted through the negatives because the index on (verse_id, position) is
        /// unique and checked per row: moving 3 to 2 while 2 is still 2 fails, and nothing orders
        /// the rows of a single UPDATE.
        /// </summary>
        private const string CloseUpPositions =
            """
            UPDATE word SET "position" = -"position"
            WHERE verse_id IN (SELECT verse_id FROM wordless_bracket);

            UPDATE word w SET "position" = renumbered.rank
            FROM (SELECT id, (row_number() OVER (PARTITION BY verse_id ORDER BY "position" DESC))::int AS rank
                  FROM word
                  WHERE verse_id IN (SELECT verse_id FROM wordless_bracket)) AS renumbered
            WHERE w.id = renumbered.id
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ScanBrackets);
            migrationBuilder.Sql(NumberSpans);
            migrationBuilder.Sql(OrderSpans);
            migrationBuilder.Sql(FindWordlessBrackets);

            migrationBuilder.Sql(
                """
                INSERT INTO word_group (text_id, kind, "position")
                SELECT text_id, 'supplied', ordinal FROM supplied_group ORDER BY text_id, ordinal
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO word_group_word (word_group_id, word_id)
                SELECT g.id, s.id
                FROM supplied_span s
                JOIN supplied_group sg ON sg.verse_id = s.verse_id AND sg.span = s.span
                JOIN word_group g ON g.text_id = sg.text_id
                                 AND g.kind = 'supplied'
                                 AND g."position" = sg.ordinal
                """);

            // An aligner paired a Hebrew word with a bracket 165 times, and every one of those
            // links names the bracket and nothing else, so the link goes with the word rather than
            // being left naming an empty side.
            migrationBuilder.Sql(
                """
                DELETE FROM link WHERE id IN (
                    SELECT lw.link_id FROM link_word lw
                    JOIN wordless_bracket b ON b.id = lw.word_id)
                """);

            migrationBuilder.Sql("DELETE FROM word WHERE id IN (SELECT id FROM wordless_bracket)");
            migrationBuilder.Sql(CloseUpPositions);

            migrationBuilder.Sql(
                """
                UPDATE word SET "text" = replace(replace("text", '[', ''), ']', ''),
                                trailer = replace(replace(trailer, '[', ''), ']', '')
                WHERE id IN (SELECT id FROM bracket_scan
                             WHERE trailer LIKE '%[%' OR trailer LIKE '%]%')
                """);
        }

        /// <summary>
        /// Only the half that can be undone. The bracket characters are gone from the text and
        /// nothing in the database knows where they stood; putting them back means loading the
        /// Synodal again from its file.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM word_group WHERE kind = 'supplied'");
        }
    }
}
