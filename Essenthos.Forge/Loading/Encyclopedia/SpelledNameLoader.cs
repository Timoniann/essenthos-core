using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Measured">
/// The rule asked of every Greek name already annotated, whatever its number: how many words it
/// answers and how many of them carry the record it answers with.
/// </param>
/// <param name="Named">Per record: the original words named, and how many of their verses the dataset lists under it.</param>
/// <param name="Contested">Words the links reach that already name somebody else, left as they are.</param>
/// <param name="Written">Annotations written under this pass's source, the carried ones included.</param>
internal sealed record SpelledNameOutcome(
    bool AlreadyLoaded,
    (int Answered, int Right) Measured,
    IReadOnlyList<(string Slug, int Words, int Listed)> Named,
    int Contested,
    int Written,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the Greek names a record spells and no record is numbered by are already named"
            : $"held out, the rule answered {Measured.Answered} annotated Greek names and {Measured.Right} of them " +
              $"carry the record it names. Named: " +
              string.Join(", ", Named.Select(n => $"{n.Slug} {n.Words} words ({n.Listed} verses the dataset lists)")) +
              $". {Contested} carried words already name somebody else and were left. {Written} words written in " +
              $"{Elapsed}. Per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// A Greek name no record is held under the number of, named as the one record whose own Greek
/// spelling it is.
///
/// <para>
/// BibleData gives some of its New Testament records the Greek of the name and no Strong number, or
/// a number that is another word's: Timothy has Τιμόθεος and nothing else, so no pass that resolves
/// a number reaches G5095 and twenty-four verses that print his name name nobody. The spelling is
/// on the record all the same. Where the lexical form of a capitalised Greek word is the Greek
/// name of exactly one person or place, nobody else in the encyclopedia is called what that record
/// is called, and no record at all is held under the word's number, the word is that record.
/// </para>
///
/// <para>
/// The spellings are compared composed, because the dataset writes its accents as combining marks
/// and the Greek text writes them precomposed. A word that names anybody already is never touched,
/// and a carried word that already names somebody else keeps its answer. Idempotent on its own
/// source.
/// </para>
/// </summary>
internal sealed class SpelledNameLoader(AppDbContext db, ILogger<SpelledNameLoader> logger)
{
    public const string Source =
        "Essenthos, reading a Greek name no record is numbered by as the one record whose own Greek spelling it is";

    /// <summary>
    /// The rule's precision over the Greek names already annotated, rounded down: on 2026-09-30 it
    /// answered 1,070 words and 1,069 of them carried the record it named.
    /// </summary>
    private const double Measured = 0.99;

    /// <summary>
    /// The one record each Greek spelling belongs to, where it belongs to one and the record's name
    /// is nobody else's.
    /// </summary>
    private const string Spelled =
        """
        spelled AS (
            SELECT normalize(n.greek, NFC) AS greek, min(e.id) AS entity_id
            FROM entity_name n
            JOIN entity e ON e.id = coalesce(n.aspect_of_entity_id, n.entity_id) AND e.kind IN ('person', 'place')
            WHERE n.greek IS NOT NULL
              AND coalesce(n.kind, '') NOT IN ('title', 'description', 'term', 'gentilic', 'collective')
            GROUP BY 1
            HAVING count(DISTINCT e.id) = 1
        ),
        sole AS (
            SELECT s.greek, s.entity_id
            FROM spelled s
            JOIN entity e ON e.id = s.entity_id
            WHERE NOT EXISTS (
                      SELECT 1 FROM entity other
                      WHERE other.id <> e.id AND other.kind IN ('person', 'place', 'people') AND other.name = e.name)
              AND NOT EXISTS (
                      SELECT 1 FROM entity_name other
                      WHERE coalesce(other.aspect_of_entity_id, other.entity_id) <> e.id AND other.label = e.name)
        )
        """;

    private const string Candidates =
        $"""
         WITH {Spelled},
         borne AS (
             SELECT DISTINCT trim(number) AS number
             FROM entity_name n, unnest(string_to_array(n.greek_strong_number, ',')) AS number
             WHERE n.greek_strong_number IS NOT NULL
         )
         SELECT w.id, o.entity_id, r.canonical_book, r.canonical_chapter, r.canonical_verse, w.strong_number, w.lemma
         FROM word w
         JOIN text t ON t.id = w.text_id AND t.slug = @greek
         JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
         JOIN sole o ON o.greek = normalize(w.lemma, NFC)
         WHERE w.lemma ~ '^[[:upper:]]'
           AND w.strong_number IS NOT NULL
           AND w.strong_number NOT IN (SELECT number FROM borne)
           AND NOT EXISTS (SELECT 1 FROM word_entity spoken WHERE spoken.word_id = w.id)
         """;

    private const string HeldOut =
        $"""
         WITH {Spelled}
         SELECT count(*), count(*) FILTER (WHERE EXISTS (
                    SELECT 1 FROM word_entity a WHERE a.word_id = w.id AND a.entity_id = o.entity_id))
         FROM word w
         JOIN text t ON t.id = w.text_id AND t.slug = @greek
         JOIN sole o ON o.greek = normalize(w.lemma, NFC)
         WHERE w.lemma ~ '^[[:upper:]]'
           AND EXISTS (SELECT 1 FROM word_entity a WHERE a.word_id = w.id AND a.source <> @source)
         """;

    private const string Listed =
        """
        SELECT entity_id, canonical_book, canonical_chapter, canonical_verse
        FROM entity_verse WHERE source = @dataset
        """;

    /// <summary>A carried word that already names another entity is not given a second answer.</summary>
    private const string Contest =
        """
        DELETE FROM pending_annotation a
        USING word_entity already
        WHERE already.word_id = a.word_id AND already.entity_id <> a.entity_id
        """;

    public async Task<SpelledNameOutcome> Load(CancellationToken cancellationToken = default)
    {
        if (await db.WordEntities.AnyAsync(a => a.Source == Source, cancellationToken))
        {
            logger.LogInformation("The Greek names a record spells are already named; nothing to do");
            return new SpelledNameOutcome(true, default, [], 0, 0, [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var measured = (await Read(connection, HeldOut, reader => ((int)reader.GetInt64(0), (int)reader.GetInt64(1)),
            cancellationToken, ("greek", NestleTextSource.Slug), ("source", Source))).Single();
        var words = await Read(connection, Candidates, reader => (
            Word: reader.GetInt64(0), Entity: reader.GetInt32(1),
            At: (reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)),
            Number: reader.GetString(5), Lemma: reader.GetString(6)),
            cancellationToken, ("greek", NestleTextSource.Slug));
        var listed = (await Read(connection, Listed, reader => (reader.GetInt32(0),
                (reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3))),
            cancellationToken, ("dataset", BibleDataLoader.Source))).ToHashSet();

        var seed = words.Select(word => (word.Word, word.Entity, (double?)Measured,
            listed.Contains((word.Entity, word.At)),
            $"{word.Lemma}, {word.Number}: the record's own Greek name, and a number no record is held under"));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Seed(connection, seed, cancellationToken);
        await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);
        await using var contest = new NpgsqlCommand(
            Contest, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        var contested = await contest.ExecuteNonQueryAsync(cancellationToken);

        var method = EnumSpelling.Of(LinkMethod.RuleBased);
        await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
            ("method", method), ("source", Source));
        await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
            ("method", method), ("source", Source));

        var byText = await Annotating.ByText(connection, transaction, Source, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var slugs = await db.Entities.AsNoTracking()
            .Where(e => words.Select(word => word.Entity).Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Slug, cancellationToken);
        var outcome = new SpelledNameOutcome(
            false,
            measured,
            [
                .. words.GroupBy(word => word.Entity).OrderByDescending(group => group.Count()).Select(group => (
                    slugs[group.Key], group.Count(),
                    group.Select(word => word.At).Distinct().Count(at => listed.Contains((group.Key, at))))),
            ],
            contested, byText.Sum(t => t.Words), byText, started.Elapsed);
        logger.LogInformation("Named the Greek names a record spells: {Outcome}", outcome);
        return outcome;
    }

    private static async Task<List<T>> Read<T>(
        NpgsqlConnection connection,
        string sql,
        Func<NpgsqlDataReader, T> row,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var rows = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(row(reader));
        }

        return rows;
    }
}
