using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Database;
using Essenthos.Core.Utils;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Withdrawn">Carried names taken off a word that is no name, beside the word of the verse that is.</param>
/// <param name="ByText">How many of those each text held.</param>
/// <param name="Examples">A few of them in words, for the log: the text, the word and whom it was given.</param>
internal sealed record StrayNameOutcome(
    int Withdrawn,
    IReadOnlyList<(string Text, int Words)> ByText,
    IReadOnlyList<string> Examples,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        Withdrawn == 0
            ? "no person or place stands on a faintly linked word beside the word of its verse that names it"
            : $"{Withdrawn} persons and places carried faintly onto a word beside the word that names them taken back, " +
              $"in {Elapsed}. Per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}")) +
              ". For example: " + string.Join("; ", Examples);
}

/// <summary>
/// A person or a place carried along a faint link onto a word that is not their name, in a verse
/// whose own words name them elsewhere.
///
/// <para>
/// Ohienko's 1 Chronicles 13:11 reads <em>і назвав ім'я тому місцю Перец-Узза, і так воно зветься</em>.
/// The aligner gave the Hebrew פֶּרֶץ עֻזָּא to <em>так</em> at 0.25 and to <em>зветься</em> at 0.44,
/// and the name itself to the verb פָרַץ beside it, so the place was carried onto the two words and
/// the reader met Perez-uzza on <em>so</em> and <em>is called</em>, two words after
/// <em>Перец-Узза</em>, which the text's own verses already name. <see cref="Annotating.Carry"/> refuses
/// a faint link where the same seed reaches the rendering firmly; here the rendering was reached by
/// another pass, and the seed reached nothing else.
/// </para>
///
/// <para>
/// <strong>The test</strong>, all of which must hold:
/// </para>
/// <list type="number">
/// <item>The row was carried, with a number, and nothing else holds it up: no claim stands on it that
/// was read of the word itself.</item>
/// <item>The link it crossed is below <see cref="Annotating.Faint"/>.</item>
/// <item>The seed word is a name: a proper noun, a capitalised lemma, or a word written with a capital.
/// A description — <em>the king</em> read as David — is rendered by a description, and a pronoun the
/// verse settles is the pronoun pass's question.</item>
/// <item>The word is not written as a name where it stands, and is not a pronoun of its language.</item>
/// <item>Another word of the same verse of the same text names the same record more surely — a reading
/// of its own, or a link that is not faint itself — and in a script without capitals it is not the word
/// next to this one, which may be the other half of a name written in two. Of two faint words, neither
/// is the name.</item>
/// <item>The word is not that word's spelling, nor any spelling the encyclopedia holds for the record,
/// nor a part of one.</item>
/// </list>
///
/// <para>
/// Rulings and readings of the word itself are never touched. It finds nothing the second time; a carry leaves such a row out before writing it (<see cref="LeaveOut"/>), and so does the
/// name resolution, which carries afresh every time it runs (<see cref="LeaveOutOfAPass"/>).
/// </para>
/// </summary>
internal sealed class StrayNames(AppDbContext db, ILogger<StrayNames> logger)
{
    /// <summary>How alike two spellings must be to be one name's: the line <see cref="Annotating"/> draws between inflections.</summary>
    private const string Alike = "0.3";

    private static readonly string Faint = Annotating.Faint.ToString(CultureInfo.InvariantCulture);

    private static string Folded(string source) =>
        $"regexp_replace({DiacriticFolding.Expression(source)}, '[^[:alnum:]]', '', 'g')";

    /// <summary>
    /// The rows of <paramref name="candidates"/> the test refuses, as <c>stray_judged (row_id, word_id,
    /// entity_id, text_slug, surface, named)</c>. The candidates are (row_id, word_id, entity_id,
    /// confidence, link, through); <paramref name="named"/> is a relation of (word_id, entity_id,
    /// confidence, carried) holding every name the verses carry.
    /// </summary>
    private static string Judge(string candidates, string named) =>
        $"""
         DROP TABLE IF EXISTS stray_judged;
         CREATE TEMP TABLE stray_judged ON COMMIT DROP AS
         WITH candidate AS MATERIALIZED (
             SELECT c.row_id, c.word_id, c.entity_id, c.confidence, w.verse_id, w.position, w.text AS surface,
                    {Folded("w.text")} AS bare, lower(w.text) <> upper(w.text) AS cased, t.slug AS text_slug,
                    e.slug AS named
             FROM ({candidates}) c
             JOIN entity e ON e.id = c.entity_id AND e.kind IN ('person', 'place')
             JOIN word w ON w.id = c.word_id
             JOIN text t ON t.id = w.text_id
             JOIN word seed ON seed.id = c.through
             WHERE c.link < {Faint} AND c.confidence IS NOT NULL
               AND (seed.morphology ->> 'pos' = 'nmpr' OR seed.lemma ~ '^[[:upper:]]' OR seed.text ~ '^[[:upper:]]')
               AND NOT {WrittenAsName("w")}
               AND (lower(w.text) <> upper(w.text) OR char_length({Folded("w.text")}) > 1)
               AND NOT EXISTS (SELECT 1 FROM {Pronouns.Rows()}
                               WHERE pronoun.language = t.language
                                 AND pronoun.word = lower(regexp_replace(w.text, '[[:punct:]]', '', 'g')))
         ),
         naming AS MATERIALIZED (
             SELECT c.row_id, {Folded("o.text")} AS bare,
                    coalesce(n.confidence, 1.0) > c.confidence
                        AND (NOT n.carried OR coalesce(n.confidence, 1.0) >= {Faint})
                        AND (c.cased OR abs(o.position - c.position) > 1) AS surer
             FROM candidate c
             JOIN word o ON o.verse_id = c.verse_id AND o.id <> c.word_id
             JOIN {named} n ON n.word_id = o.id AND n.entity_id = c.entity_id
         )
         SELECT c.row_id, c.word_id, c.entity_id, c.text_slug, c.surface, c.named
         FROM candidate c
         WHERE EXISTS (SELECT 1 FROM naming n WHERE n.row_id = c.row_id AND n.surer)
           AND NOT EXISTS (SELECT 1 FROM naming n WHERE n.row_id = c.row_id AND {Spells("n.bare", "c.bare")})
           AND NOT EXISTS (
               SELECT 1 FROM (
                   SELECT {Folded("e.name")} AS bare FROM entity e WHERE e.id = c.entity_id
                   UNION ALL SELECT {Folded("f.form")} FROM entity_name_form f WHERE f.entity_id = c.entity_id
                   UNION ALL SELECT {Folded("x.spelling")}
                             FROM entity_name n
                             CROSS JOIN LATERAL (VALUES (n.label), (n.hebrew), (n.hebrew_transliterated),
                                                        (n.greek), (n.greek_transliterated)) x(spelling)
                             WHERE n.entity_id = c.entity_id AND x.spelling IS NOT NULL) known
               WHERE {Spells("known.bare", "c.bare")})
         """;

    /// <summary>
    /// Whether a word is a spelling of a name: alike it, or a part of it — the second half of
    /// <em>Ben-oni</em> — or holding it whole, as a Chinese word holds the name with the particle
    /// written onto it (<em>叫西門</em>).
    /// </summary>
    private static string Spells(string name, string word) =>
        $"""
         (similarity({name}, {word}) >= {Alike}
          OR (char_length({word}) >= 2 AND strpos({name}, {word}) > 0)
          OR (char_length({name}) >= 2 AND strpos({word}, {name}) > 0))
         """;

    /// <summary>
    /// A word written as a name where it stands: a capital, after a word that does not close a sentence.
    /// The capital may follow an elided article or a quotation mark — <em>l’Éternel</em>, <em>'O</em>.
    /// </summary>
    private static string WrittenAsName(string word) =>
        $"""
         ({word}.text ~ '^[^[:alnum:]]*([[:lower:]][[:lower:]]?[’''])?[[:upper:]]'
          AND EXISTS (SELECT 1 FROM word before
                      WHERE before.verse_id = {word}.verse_id AND before.position = {word}.position - 1
                        AND before.trailer !~ '[.!?]'))
         """;

    /// <summary>
    /// The carried rows of the corpus, with the link each crossed: the best a link joining the seed word
    /// and this word is worth (<see cref="Annotating.LinkWorth"/>). A carried row's number is its seed's
    /// times the link's, so only a row already below the line can have crossed a link below it. A row
    /// with a claim no link carried has an answer read of its own word beneath it, and is not a candidate.
    /// </summary>
    private static readonly string Written =
        $"""
         WITH carried AS MATERIALIZED (
             SELECT a.id AS row_id, a.word_id, a.entity_id, a.confidence, k.through
             FROM word_entity a
             JOIN entity e ON e.id = a.entity_id AND e.kind IN ('person', 'place')
             CROSS JOIN LATERAL (SELECT (regexp_match(a.note, '^through \S+ word (\d+)'))[1]::bigint AS through) k
             WHERE a.note LIKE @carried AND a.confidence < {Faint} AND k.through IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM word_entity_claim c
                               WHERE c.word_entity_id = a.id AND c.method <> 'stated-by-source'
                                 AND coalesce(c.note, '') NOT LIKE @carried)
         )
         SELECT c.*,
                (SELECT max({Annotating.LinkWorth})
                 FROM link_word mine
                 JOIN link_word theirs ON theirs.link_id = mine.link_id AND theirs.word_id = c.through
                 JOIN link l ON l.id = mine.link_id
                 WHERE mine.word_id = c.word_id) AS link
         FROM carried c
         """;

    /// <summary>The rows a carry is about to write, with the seed word each was reached from.</summary>
    private const string Expected =
        """
        SELECT x.word_id AS row_id, x.word_id, x.entity_id, x.confidence, s.word_id AS through, x.link
        FROM expected x
        JOIN word_entity s ON s.id = x.seed_id
        """;

    /// <summary>The names the corpus holds and the carry is about to write.</summary>
    private const string NamesWithTheExpected =
        $"""
         (SELECT word_id, entity_id, confidence, coalesce(note, '') LIKE '{Annotating.CarriedNote}' AS carried FROM word_entity
          UNION ALL SELECT word_id, entity_id, confidence, TRUE FROM expected)
         """;

    /// <summary>The names the corpus holds, and whether a link carried each.</summary>
    private const string Names =
        $"(SELECT word_id, entity_id, confidence, coalesce(note, '') LIKE '{Annotating.CarriedNote}' AS carried FROM word_entity)";

    private static readonly string JudgeWritten = Judge(Written, Names);

    private static readonly string JudgeExpected = Judge(Expected, NamesWithTheExpected);

    /// <summary>
    /// Leaves out of a carry's expected rows the ones the test refuses, so the carry neither writes such a
    /// row nor keeps one it wrote before.
    /// </summary>
    /// <returns>How many expected rows were left out.</returns>
    public async Task<int> LeaveOut(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using (var judge = new NpgsqlCommand(JudgeExpected, connection, transaction))
        {
            await judge.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var delete = new NpgsqlCommand(
            "DELETE FROM expected x USING stray_judged j WHERE x.word_id = j.word_id AND x.entity_id = j.entity_id",
            connection, transaction);
        var left = await delete.ExecuteNonQueryAsync(cancellationToken);
        if (left > 0)
        {
            logger.LogInformation("{Left} persons and places a faint link carried beside their named word were not written", left);
        }

        return left;
    }

    /// <summary>
    /// What a pass is about to settle: <c>expected (word_id, entity_id, confidence, through, link)</c>, its
    /// seeds with no <c>through</c> and the rows its links reach with the seed word each was reached from.
    /// </summary>
    private static class Settling
    {
        private const string Candidates =
            """
            SELECT x.word_id AS row_id, x.word_id, x.entity_id, x.confidence, x.through, x.link
            FROM expected x
            WHERE x.through IS NOT NULL
            """;

        private const string Named =
            $"""
             (SELECT word_id, entity_id, confidence, coalesce(note, '') LIKE '{Annotating.CarriedNote}' AS carried FROM word_entity
              UNION ALL SELECT word_id, entity_id, confidence, through IS NOT NULL FROM expected)
             """;

        public static readonly string Judged = Judge(Candidates, Named);
    }

    /// <summary>
    /// Leaves out of what a pass is about to settle the rows the test refuses (<see cref="Settling"/>), so
    /// a pass that names words afresh each time it runs does not write again what <see cref="Withdraw"/>
    /// took back.
    /// </summary>
    /// <returns>How many expected rows were left out.</returns>
    public async Task<int> LeaveOutOfAPass(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using (var judge = new NpgsqlCommand(Settling.Judged, connection, transaction))
        {
            await judge.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var delete = new NpgsqlCommand(
            "DELETE FROM expected x USING stray_judged j WHERE x.word_id = j.word_id AND x.entity_id = j.entity_id",
            connection, transaction);
        var left = await delete.ExecuteNonQueryAsync(cancellationToken);
        if (left > 0)
        {
            logger.LogInformation("{Left} persons and places a faint link carried beside their named word were not written", left);
        }

        return left;
    }

    /// <param name="write">False to count what would be taken back and take back nothing.</param>
    public async Task<StrayNameOutcome> Withdraw(CancellationToken cancellationToken = default, bool write = true)
    {
        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var judge = new NpgsqlCommand(JudgeWritten, connection, transaction))
        {
            judge.Parameters.AddWithValue("carried", Annotating.CarriedNote);
            await judge.ExecuteNonQueryAsync(cancellationToken);
        }

        var byText = new List<(string, int)>();
        await using (var count = new NpgsqlCommand(
                         "SELECT text_slug, count(*) FROM stray_judged GROUP BY 1 ORDER BY 2 DESC, 1", connection, transaction))
        await using (var reader = await count.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                byText.Add((reader.GetString(0), (int)reader.GetInt64(1)));
            }
        }

        var examples = new List<string>();
        await using (var some = new NpgsqlCommand(
                         "SELECT text_slug || ' ' || surface || ': ' || named FROM stray_judged ORDER BY text_slug, row_id LIMIT 12",
                         connection, transaction))
        await using (var reader = await some.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                examples.Add(reader.GetString(0));
            }
        }

        int withdrawn;
        await using (var delete = new NpgsqlCommand(
                         "DELETE FROM word_entity a USING stray_judged j WHERE a.id = j.row_id", connection, transaction))
        {
            withdrawn = await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        if (write)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        var outcome = new StrayNameOutcome(withdrawn, byText, examples, started.Elapsed);
        logger.LogInformation("{Outcome}", outcome);
        return outcome;
    }
}
