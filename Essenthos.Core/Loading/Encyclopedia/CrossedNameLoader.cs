using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Pairs">Pairs of words each of which was given the other's name.</param>
/// <param name="Rewritten">Annotations taken back and written again at the other word.</param>
/// <param name="Where">Each pair in words, for the log: the text, the address and the two names.</param>
internal sealed record CrossedNameOutcome(int Pairs, int Rewritten, IReadOnlyList<string> Where, TimeSpan Elapsed)
{
    public override string ToString() =>
        Pairs == 0
            ? "no two words of one verse were given each other's names"
            : $"{Pairs} pairs of words were given each other's names and were crossed back, " +
              $"{Rewritten} annotations rewritten, in {Elapsed}: " + string.Join("; ", Where);
}

/// <summary>
/// Two words of one verse given each other's names, crossed back.
///
/// <para>
/// 1 Kings 7:13 reads <em>и взял из Тира Хирама</em>, and the Synodal's printed numbering tags
/// <em>Тира</em> with Hiram's number and <em>Хирама</em> with Tyre's: the Hebrew names the man
/// before the city and the Russian names the city before the man, and the tagging followed the
/// Hebrew's order onto the Russian's words. The links are then crossed, and every annotation
/// carried along them is crossed with them, so the reader meets Tyre on the word that says Hiram.
/// It is not one edition's slip — the King James reaches יֹואָשׁ and יְהֹויָדָע the same way round
/// in 2 Chronicles 24:2 through a mapping somebody published, and Luther and the Synodal both cross
/// Ishvah with Ishvi in 1 Chronicles 7:30.
/// </para>
///
/// <para>
/// <strong>The word's own spelling is what says so.</strong> The encyclopedia records how each name
/// is written in each language it serves, so <em>Тира</em> is a form of Tyre and of nothing else and
/// <em>Хирама</em> is a form of Hiram. Where two words of one verse each carry exactly the other's
/// name and neither carries its own, one reading accounts for both: the two answers are the right
/// two and they are on the wrong words. Nothing here guesses — the test is equality against a form
/// the encyclopedia holds, in the language of the text the word stands in, and it asks nothing of a
/// word it cannot spell.
/// </para>
///
/// <para>
/// <strong>It rewrites the annotation and leaves the link alone.</strong> A link is a source's own
/// statement about which words correspond, and three of the four pairs come from mappings this
/// corpus carries rather than draws; declining to repeat a dataset's answer about who is named is
/// not the same act as rewriting what it says two words are. The crossed links stay, and the reader
/// still highlights across them.
/// </para>
///
/// <para>
/// <strong>Re-runnable rather than run-once.</strong> Every carrying pass reaches these words again
/// from the same crossed links and writes the crossed answer again, so this runs after each of them
/// — at the end of the start-up pipeline and at the end of <see cref="AnnotationCarrier"/> — and
/// takes the crossed row back each time. On a corpus already crossed back it finds nothing, because
/// after the rewrite each word does spell its own name.
/// </para>
/// </summary>
internal sealed class CrossedNameLoader(AppDbContext db, ILogger<CrossedNameLoader> logger)
{
    public const string Source =
        "Essenthos, crossing back two names a mapping gave to each other's words: each of them is " +
        "exactly the form the encyclopedia records for the name the other was given";

    /// <summary>
    /// How each name is spelled in each language the interface serves, folded the way a word's
    /// searchable form is folded, so the two sides of the comparison are the same expression.
    /// </summary>
    private static readonly string Spellings =
        $"""
         CREATE TEMP TABLE spelling (entity_id integer NOT NULL, language text NOT NULL, form text NOT NULL)
         ON COMMIT DROP;
         INSERT INTO spelling (entity_id, language, form)
         SELECT DISTINCT f.entity_id, f.language, {DiacriticFolding.Expression("f.form")}
         FROM entity_name_form f;
         CREATE INDEX ON spelling (language, form);
         CREATE INDEX ON spelling (entity_id, language)
         """;

    /// <summary>
    /// Every annotation a rule wrote, on a word the corpus can spell, in a language the
    /// encyclopedia declines names into. A row a person or a source settled carries no confidence
    /// and is left out: a ruling is not something a spelling overturns.
    /// </summary>
    private const string Named =
        """
        CREATE TEMP TABLE named (
            row_id bigint PRIMARY KEY,
            word_id bigint NOT NULL,
            entity_id integer NOT NULL,
            confidence double precision NOT NULL,
            source text NOT NULL,
            verse_id integer NOT NULL,
            text_id integer NOT NULL,
            language text NOT NULL,
            form text NOT NULL)
        ON COMMIT DROP;
        INSERT INTO named
        SELECT a.id, a.word_id, a.entity_id, a.confidence, a.source,
               w.verse_id, w.text_id, t.language, w.normalised_text
        FROM word_entity a
        JOIN word w ON w.id = a.word_id
        JOIN text t ON t.id = w.text_id
        WHERE a.confidence IS NOT NULL
          AND w.normalised_text IS NOT NULL AND w.normalised_text <> ''
          AND EXISTS (SELECT 1 FROM spelling s WHERE s.language = t.language);
        CREATE INDEX ON named (verse_id, text_id)
        """;

    /// <summary>
    /// The crossed pairs. Both directions are found, because both rows are wrong and both are
    /// rewritten; and a pair either of whose words stands in a second such pair is dropped whole,
    /// since which of them it belongs to is exactly what the rule has no answer for.
    /// </summary>
    private const string Crossed =
        """
        CREATE TEMP TABLE crossed (
            row_id bigint NOT NULL,
            word_id bigint NOT NULL,
            entity_id integer NOT NULL,
            confidence double precision NOT NULL,
            other_word bigint NOT NULL,
            note text NOT NULL)
        ON COMMIT DROP;
        INSERT INTO crossed
        SELECT mine.row_id, mine.word_id, theirs.entity_id, theirs.confidence, theirs.word_id,
               'crossed with word ' || theirs.word_id || ': ' || mine.source || ' named this word '
               || was.name || ', and the encyclopedia writes ' || instead.name || ' as ' || mine.form
        FROM named mine
        JOIN named theirs ON theirs.verse_id = mine.verse_id AND theirs.text_id = mine.text_id
             AND theirs.word_id <> mine.word_id AND theirs.entity_id <> mine.entity_id
        JOIN entity was ON was.id = mine.entity_id
        JOIN entity instead ON instead.id = theirs.entity_id
        WHERE EXISTS (SELECT 1 FROM spelling s
                      WHERE s.entity_id = theirs.entity_id AND s.language = mine.language AND s.form = mine.form)
          AND EXISTS (SELECT 1 FROM spelling s
                      WHERE s.entity_id = mine.entity_id AND s.language = mine.language AND s.form = theirs.form)
          AND NOT EXISTS (SELECT 1 FROM spelling s
                          WHERE s.entity_id = mine.entity_id AND s.language = mine.language AND s.form = mine.form)
          AND NOT EXISTS (SELECT 1 FROM spelling s
                          WHERE s.entity_id = theirs.entity_id AND s.language = mine.language AND s.form = theirs.form);
        CREATE TEMP TABLE ambiguous ON COMMIT DROP AS
        SELECT word_id FROM crossed GROUP BY word_id HAVING count(*) > 1;
        DELETE FROM crossed c
        WHERE c.word_id IN (SELECT word_id FROM ambiguous)
           OR c.other_word IN (SELECT word_id FROM ambiguous)
        """;

    private const string Withdraw = "DELETE FROM word_entity a USING crossed c WHERE a.id = c.row_id";

    private const string Write =
        """
        INSERT INTO word_entity (word_id, entity_id, method, confidence, source, note)
        SELECT c.word_id, c.entity_id, @method, c.confidence, @source, c.note
        FROM crossed c
        ON CONFLICT (word_id, entity_id) DO NOTHING
        """;

    private const string Claim =
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT a.id, @method, c.confidence, @source, c.note
        FROM word_entity a
        JOIN crossed c ON c.word_id = a.word_id AND c.entity_id = a.entity_id
        ON CONFLICT DO NOTHING
        """;

    private const string Report =
        """
        SELECT t.slug || ' ' || b.name || ' ' || v.chapter_number || ':' || v.number || ' '
               || w.text || ' = ' || e.name
        FROM crossed c
        JOIN word w ON w.id = c.word_id
        JOIN text t ON t.id = w.text_id
        JOIN verse v ON v.id = w.verse_id
        JOIN book b ON b.id = v.book_id
        JOIN entity e ON e.id = c.entity_id
        ORDER BY t.slug, b.canonical_ordinal, v.chapter_number, v.number, w.position
        """;

    public async Task<CrossedNameOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Spellings, cancellationToken);
        await Annotating.Run(connection, transaction, Named, cancellationToken);
        await Annotating.Run(connection, transaction, Crossed, cancellationToken);

        var where = await Where(connection, transaction, cancellationToken);
        if (where.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogInformation("No two words of one verse were given each other's names; nothing to cross back");
            return new CrossedNameOutcome(0, 0, [], started.Elapsed);
        }

        var method = EnumSpelling.Of(LinkMethod.Lexical);
        await Annotating.Run(connection, transaction, Withdraw, cancellationToken);
        await Annotating.Run(connection, transaction, Write, cancellationToken,
            ("method", method), ("source", Source));
        await Annotating.Run(connection, transaction, Claim, cancellationToken,
            ("method", method), ("source", Source));
        await transaction.CommitAsync(cancellationToken);

        var outcome = new CrossedNameOutcome(where.Count / 2, where.Count, where, started.Elapsed);
        logger.LogInformation("Crossed back the names two words were given of each other: {Outcome}", outcome);
        return outcome;
    }

    private static async Task<List<string>> Where(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            Report, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.CommandTimeout = Annotating.Patient;

        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
