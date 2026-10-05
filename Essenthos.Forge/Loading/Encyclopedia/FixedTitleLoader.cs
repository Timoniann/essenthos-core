using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="ByRule">Per rule: the original words it named, their verses, and how many of those the dataset lists under the same record.</param>
/// <param name="Missing">Records a rule names that the encyclopedia does not hold, whose rules were skipped.</param>
/// <param name="Contested">Words the links reach that already name somebody else, left as they are.</param>
/// <param name="Written">Annotations standing under this pass's source afterwards, the carried ones included.</param>
internal sealed record FixedTitleOutcome(
    bool AlreadyLoaded,
    IReadOnlyList<(string Rule, int Words, int Verses, int Listed)> ByRule,
    IReadOnlyList<string> Missing,
    int Contested,
    int Written,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the titles the text fixes to one bearer are already named"
            : string.Join("; ", ByRule.Select(r =>
                  $"{r.Rule}: {r.Words} words in {r.Verses} verses, {r.Listed} of them listed by the dataset")) +
              $". {Contested} carried words already name somebody else and were left. {Written} words written in " +
              $"{Elapsed}" + (Missing.Count > 0 ? $"; no record for {string.Join(", ", Missing)}" : "") +
              ". Per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// A title the text fixes to one bearer — the devil and the dragon of Revelation, the Christ, the
/// Son of Man, Ezekiel's 'son of man' — written on the original words that have the title's shape
/// and nothing on them yet, and carried across the links. See <see cref="FixedTitles"/> for the rules.
///
/// <para>
/// A word that names anybody already is never touched, whoever settled it; a carried word that
/// already names somebody else keeps its answer. A word that names a title only is not yet named
/// by anybody, and the bearer is written beside the title. Idempotent by that same rule: what it
/// wrote names its word, so a later run finds only the words a new rule or a new text adds.
/// </para>
/// </summary>
internal sealed class FixedTitleLoader(AppDbContext db, ILogger<FixedTitleLoader> logger)
{
    public const string Source = "Essenthos, reading a title the text itself fixes to one bearer";

    /// <summary>
    /// Every rule was measured against the dataset's verse list before it was written, and none of the
    /// verses it names that the list leaves out named anybody else when read.
    /// </summary>
    private const double Measured = 0.99;

    /// <summary>Enough words after a title for the longest shape a rule asks for.</summary>
    private const int Ahead = 3;

    private static readonly string[] Originals = [BhsaTextSource.Slug, .. FixedTitles.GreekWitnesses];

    private static readonly string Title = EnumSpelling.Of(EntityKind.Title);

    private const string Candidates =
        """
        SELECT w.id, t.slug, r.canonical_book, r.canonical_chapter, r.canonical_verse, w.strong_number,
               w.morphology->>'pos', w.morphology->>'case', w.morphology->>'number', w.morphology->>'state',
               coalesce(w.morphology->>'form', w.morphology->>'robinson'),
               p.morphology->>'pos', p.morphology->>'case', p.morphology->>'number', p.morphology->>'state',
               coalesce(p.morphology->>'form', p.morphology->>'robinson'), p.id IS NULL,
               ARRAY(SELECT coalesce(n.strong_number, '') FROM word n
                     WHERE n.verse_id = w.verse_id AND n.position > w.position
                     ORDER BY n.position LIMIT @ahead),
               (SELECT count(*)::int FROM word same
                WHERE same.verse_id = w.verse_id AND same.strong_number = w.strong_number
                  AND same.position <= w.position)
        FROM word w
        JOIN text t ON t.id = w.text_id AND t.slug = ANY(@originals)
        JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
        LEFT JOIN word p ON p.verse_id = w.verse_id AND p.position = w.position - 1
        WHERE w.strong_number = ANY(@numbers)
          AND NOT EXISTS (SELECT 1 FROM word_entity spoken
                          JOIN entity whom ON whom.id = spoken.entity_id AND whom.kind <> @title
                          WHERE spoken.word_id = w.id)
        """;

    private const string Listed =
        """
        SELECT entity_id, canonical_book, canonical_chapter, canonical_verse
        FROM entity_verse WHERE source = @dataset
        """;

    /// <summary>
    /// A carried word that already names another entity is not given a second answer. A title on
    /// the word is not one: it says what the word is and not whose, and the bearer stands beside it.
    /// </summary>
    private const string Contest =
        """
        DELETE FROM pending_annotation a
        USING word_entity already
        JOIN entity whom ON whom.id = already.entity_id AND whom.kind <> @title
        WHERE already.word_id = a.word_id AND already.entity_id <> a.entity_id
        """;

    public async Task<FixedTitleOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var slugs = FixedTitles.Rules.Select(rule => rule.Slug).Distinct().ToList();
        var records = await db.Entities.AsNoTracking()
            .Where(e => slugs.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug, e => e.Id, cancellationToken);
        var missing = slugs.Where(slug => !records.ContainsKey(slug)).ToList();

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var words = await Read(connection, Candidates, reader => (
            Word: new FixedTitleWord(
                reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(5),
                Morphology(reader, 6),
                reader.GetBoolean(16) ? null : Morphology(reader, 11),
                reader.GetFieldValue<string[]>(17),
                Chapter: reader.GetInt32(3),
                Verse: reader.GetInt32(4),
                Nth: reader.GetInt32(18)),
            At: (reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4))),
            cancellationToken, ("originals", Originals), ("numbers", FixedTitles.Numbers.ToArray()),
            ("ahead", Ahead), ("title", Title));
        var listed = (await Read(connection, Listed, reader => (reader.GetInt32(0),
                (reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3))),
            cancellationToken, ("dataset", BibleDataLoader.Source))).ToHashSet();

        var seed = new List<(long, int, double?, bool, string)>();
        var byRule = new Dictionary<FixedTitleRule, List<(int, int, int)>>();
        foreach (var (word, at) in words)
        {
            if (FixedTitles.Of(word) is not { } rule || !records.TryGetValue(rule.Slug, out var entity))
            {
                continue;
            }

            seed.Add((word.Id, entity, Measured, listed.Contains((entity, at)), rule.Note));
            (byRule.TryGetValue(rule, out var verses) ? verses : byRule[rule] = []).Add(at);
        }

        if (seed.Count == 0 && await db.WordEntities.AnyAsync(a => a.Source == Source, cancellationToken))
        {
            logger.LogInformation("The titles the text fixes to one bearer are already named; nothing to do");
            return new FixedTitleOutcome(true, [], missing, 0, 0, [], started.Elapsed);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Seed(connection, seed, cancellationToken);
        await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);
        await using var contest = new NpgsqlCommand(
            Contest, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        contest.Parameters.AddWithValue("title", Title);
        var contested = await contest.ExecuteNonQueryAsync(cancellationToken);

        var method = EnumSpelling.Of(LinkMethod.RuleBased);
        await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
            ("method", method), ("source", Source));
        await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
            ("method", method), ("source", Source));

        var byText = await Annotating.ByText(connection, transaction, Source, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new FixedTitleOutcome(
            false,
            [.. FixedTitles.Rules.Where(byRule.ContainsKey).Select(rule => (
                $"{rule.Number} {rule.Slug} in {rule.Text}", byRule[rule].Count, byRule[rule].Distinct().Count(),
                byRule[rule].Distinct().Count(at => listed.Contains((records[rule.Slug], at)))))],
            missing, contested, byText.Sum(t => t.Words), byText, started.Elapsed);
        logger.LogInformation("Named the titles the text fixes to one bearer: {Outcome}", outcome);
        return outcome;
    }

    private static FixedTitleMorphology Morphology(NpgsqlDataReader reader, int first) =>
        new(Text(reader, first), Text(reader, first + 1), Text(reader, first + 2), Text(reader, first + 3),
            Text(reader, first + 4));

    private static string? Text(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

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
