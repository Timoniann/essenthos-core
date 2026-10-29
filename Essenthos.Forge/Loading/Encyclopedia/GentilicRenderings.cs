using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Written">Words given the people the original's gentilic names at their verse.</param>
/// <param name="Withdrawn">Such words taken back because the verse no longer leaves the gentilic to them.</param>
/// <param name="ByText">Words this pass names, per text, once it has run.</param>
internal sealed record GentilicRenderingOutcome(
    int Written,
    int Withdrawn,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        Written + Withdrawn == 0
            ? "every gentilic of the original a translation's verse renders already names its people there"
            : $"{Written} words given the people the original's gentilic names at their verse and {Withdrawn} taken back, " +
              $"in {Elapsed}. Per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// The people the original's gentilic names, given to the word of a translation's verse that renders it
/// where no link carried it there.
///
/// <para>
/// Ohienko writes <em>ґів'онянин Їшмая</em> where the Hebrew writes יִשְׁמַעְיָה הַגִּבְעֹנִי, the
/// gentilic after the man, and the aligner followed the Hebrew's order: it paired
/// <em>ґів'онянин</em> with Ishmaiah and nothing with the Gibeonite. The carry leaves a man's name off
/// a word written in lower case beside his capitalised name, so the gentilic carried nothing, and the
/// Gibeonites were named in the Hebrew and in no word of the Ukrainian. 1 Chronicles 11 and 27 had been
/// put right word by word (OhienkoGentilicRecords.json); the same order stands in some seventy more
/// verses of that text alone, and in every text that writes a gentilic before its man.
/// </para>
///
/// <para>
/// <strong>What says which word it is, is the text's own spelling.</strong> At a verse where the
/// original names a people with a gentilic and no word of the translation's verse names that people,
/// a word is given it where all of these hold:
/// </para>
/// <list type="bullet">
/// <item>no pass names the word anybody, and it is not a word its language never names anyone with,
/// nor a pronoun;</item>
/// <item>it is spelled like a word the same text names the people with, or the people's homeland or
/// ancestor, in another verse (<see cref="Alike"/>): <em>ґів'онянин</em> like <em>ґів'онітяни</em> of
/// 2 Samuel 21 and like <em>Ґів'он</em>. Only a spelling the text names them with at least half the
/// times it writes it counts, so <em>sons</em> of <em>the sons of Levi</em> and <em>Краю</em> of
/// <em>the land of Canaan</em>, which a link carried the people onto now and then, spell nobody. The
/// people's own name in the text's language (<c>entity_name_form</c>) counts as it is: the Gibeathite
/// and the Gederathite of 1 Chronicles 12:3-4 are the only ones Scripture names, and
/// <em>ґів'янина</em> is read by <em>ґів'яни</em>;</item>
/// <item>it is the only such word in the verse.</item>
/// </list>
///
/// <para>
/// The answer is the form of the word (<see cref="LinkMethod.Lexical"/>), at
/// <see cref="Confidence"/>: below a reading of the verse, which outranks it. It is never carried on: it
/// stands on a word no link reached it from. After every pass that names a word and idempotent: what it
/// gave and would not give today is taken back, what it would give and has not is written.
/// </para>
/// </summary>
internal sealed class GentilicRenderings(AppDbContext db, ILogger<GentilicRenderings> logger)
{
    public const string Source =
        "Essenthos, the people the original's gentilic names, on the word of the verse the text spells as it spells that people or its homeland";

    /// <summary>
    /// How sure the answer is: the room the gentilic resolution of the original leaves itself. Held out on
    /// the 38,667 verses of 44 texts where a link had already given the people to one word (a copy of
    /// 2026-10-29), the rule chose a word alone in 37,458 of them and it was that word in 37,386 (99.8 %);
    /// the verses it is asked about are the ones the links got wrong, which are harder.
    /// </summary>
    public const double Confidence = 0.9;

    /// <summary>How alike two spellings must be to be one name's: the line <see cref="Annotating"/> draws between inflections.</summary>
    private const string Alike = "0.3";

    /// <summary>The original whose gentilics are read.</summary>
    private const string Original = "BHSA";

    private static string Folded(string source) =>
        $"regexp_replace({DiacriticFolding.Expression(source)}, '[^[:alnum:]]', '', 'g')";

    private static readonly string Wanted =
        $"""
        CREATE TEMP TABLE gentilic_rendering ON COMMIT DROP AS
        WITH occurrence AS MATERIALIZED (
            SELECT DISTINCT a.entity_id AS people, r.canonical_book AS book, r.canonical_chapter AS chapter,
                   r.canonical_verse AS verse
            FROM word_entity a
            JOIN entity e ON e.id = a.entity_id AND e.kind = 'people'
            JOIN word w ON w.id = a.word_id
            JOIN text t ON t.id = w.text_id AND t.slug = '{Original}'
            JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
            WHERE a.source <> @source
              AND w.strong_number IN (SELECT g.strong_number FROM strong_gentilic g)
        ),
        kin AS MATERIALIZED (
            SELECT DISTINCT o.people, o.people AS entity_id FROM occurrence o
            UNION SELECT DISTINCT o.people, e.origin_entity_id
                  FROM occurrence o JOIN entity e ON e.id = o.people WHERE e.origin_entity_id IS NOT NULL
            UNION SELECT DISTINCT o.people, g.origin_entity_id
                  FROM occurrence o JOIN strong_gentilic g ON g.people_entity_id = o.people
                  WHERE g.origin_entity_id IS NOT NULL
        ),
        there AS MATERIALIZED (
            SELECT hw.id AS word_id, hw.text_id, hw.verse_id, o.people, o.book, o.chapter, o.verse,
                   {Folded("hw.text")} AS bare,
                   NOT {Annotating.NeverAName} AND NOT {Pronouns.IsWord()} AS may_name
            FROM occurrence o
            JOIN verse_reference r ON r.canonical_book = o.book AND r.canonical_chapter = o.chapter
                 AND r.canonical_verse = o.verse AND r.is_primary
            JOIN verse v ON v.id = r.verse_id
            JOIN text ht ON ht.id = v.text_id AND ht.slug <> '{Original}'
            JOIN word hw ON hw.verse_id = v.id
        ),
        named AS MATERIALIZED (
            SELECT DISTINCT x.text_id, x.people, x.book, x.chapter, x.verse
            FROM there x
            JOIN word_entity a ON a.word_id = x.word_id AND a.entity_id = x.people AND a.source <> @source
        ),
        spelled AS MATERIALIZED (
            SELECT w.text_id, k.people, w.normalised_text AS form, count(DISTINCT w.id) AS named,
                   array_agg(DISTINCT w.verse_id) AS verses
            FROM kin k
            JOIN word_entity a ON a.entity_id = k.entity_id AND a.source <> @source
            JOIN word w ON w.id = a.word_id
            WHERE w.normalised_text IS NOT NULL
              AND w.text_id IN (SELECT DISTINCT text_id FROM there)
            GROUP BY 1, 2, 3
        ),
        spelling AS MATERIALIZED (
            SELECT s.text_id, s.people, s.verses, {Folded("s.form")} AS bare
            FROM spelled s
            WHERE 2 * s.named >= (SELECT count(*) FROM word same
                                  WHERE same.text_id = s.text_id AND same.normalised_text = s.form)
            UNION ALL
            SELECT DISTINCT t.id, f.entity_id, ARRAY[]::integer[], {Folded("f.form")}
            FROM text t
            JOIN entity_name_form f ON f.language = t.language
            WHERE t.id IN (SELECT DISTINCT text_id FROM there)
              AND f.entity_id IN (SELECT people FROM occurrence)
        ),
        candidate AS (
            SELECT x.*, count(*) OVER (PARTITION BY x.text_id, x.people, x.book, x.chapter, x.verse) AS rivals
            FROM there x
            WHERE x.may_name AND length(x.bare) >= 3
              AND NOT EXISTS (SELECT 1 FROM named n
                              WHERE n.text_id = x.text_id AND n.people = x.people
                                AND n.book = x.book AND n.chapter = x.chapter AND n.verse = x.verse)
              AND NOT EXISTS (SELECT 1 FROM word_entity a WHERE a.word_id = x.word_id AND a.source <> @source)
              AND EXISTS (SELECT 1 FROM spelling s
                          WHERE s.text_id = x.text_id AND s.people = x.people AND s.verses <> ARRAY[x.verse_id]
                            AND length(s.bare) >= 3 AND similarity(s.bare, x.bare) >= {Alike})
        )
        SELECT DISTINCT ON (c.word_id) c.word_id, c.people AS entity_id
        FROM candidate c
        WHERE c.rivals = 1
          AND NOT EXISTS (SELECT 1 FROM candidate other
                          WHERE other.word_id = c.word_id AND other.people <> c.people)
        ORDER BY c.word_id
        """;

    private const string TakeBack =
        """
        DELETE FROM word_entity a
        WHERE a.source = @source
          AND NOT EXISTS (SELECT 1 FROM gentilic_rendering g
                          WHERE g.word_id = a.word_id AND g.entity_id = a.entity_id)
        """;

    private const string Give =
        """
        INSERT INTO word_entity (word_id, entity_id, method, confidence, source, note)
        SELECT g.word_id, g.entity_id, @method, @confidence, @source, NULL
        FROM gentilic_rendering g
        WHERE NOT EXISTS (SELECT 1 FROM word_entity a WHERE a.word_id = g.word_id AND a.entity_id = g.entity_id)
        """;

    private const string Claim =
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT a.id, a.method, a.confidence, @source, NULL
        FROM word_entity a
        WHERE a.source = @source
        ON CONFLICT DO NOTHING
        """;

    public async Task<GentilicRenderingOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Run(connection, transaction, Wanted, cancellationToken);
        var withdrawn = await Run(connection, transaction, TakeBack, cancellationToken);
        var written = await Run(connection, transaction, Give, cancellationToken);
        await Run(connection, transaction, Claim, cancellationToken);
        var byText = written + withdrawn == 0
            ? []
            : await Annotating.ByText(connection, transaction, Source, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new GentilicRenderingOutcome(written, withdrawn, byText, started.Elapsed);
        logger.LogInformation("Named the gentilics the links did not carry: {Outcome}", outcome);
        return outcome;
    }

    private static async Task<int> Run(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("source", Source);
        command.Parameters.AddWithValue("method", EnumSpelling.Of(LinkMethod.Lexical));
        command.Parameters.AddWithValue("confidence", Confidence);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
