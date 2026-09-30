using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="ByBearer">Per bearer: the witness words the rulings fix to it.</param>
/// <param name="TitleOnly">Witness words the rulings leave to the title alone.</param>
/// <param name="Unfound">Rulings whose verse holds no such word in any witness.</param>
/// <param name="Missing">Records the file names that the encyclopedia does not hold.</param>
/// <param name="Withdrawn">Annotations to a bearer taken back from words the rulings leave open, the carried ones included.</param>
/// <param name="Contested">Words the links reach that already name somebody else, left as they are.</param>
/// <param name="ByText">Words naming a bearer under these rulings afterwards, per text, the carried ones included.</param>
internal sealed record TitleReadingOutcome(
    bool AlreadyLoaded,
    IReadOnlyList<(string Bearer, int Words)> ByBearer,
    int TitleOnly,
    int Unfound,
    IReadOnlyList<string> Missing,
    int Withdrawn,
    int Contested,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    /// <summary>The title the rulings read, by slug.</summary>
    public string Title { get; init; } = "";

    public override string ToString() =>
        (Title.Length > 0 ? Title + ": " : "") +
        (AlreadyLoaded
            ? "the occurrences of the title are already read as the rulings read them: "
            : "") +
        string.Join(", ", ByBearer.Select(b => $"{b.Bearer} {b.Words}")) +
        $" witness words fixed to a bearer and {TitleOnly} left to the title alone; {Withdrawn} annotations " +
        $"withdrawn from the words left open, {Contested} carried words already name somebody else and were " +
        $"left, in {Elapsed}" +
        (Unfound > 0 ? $"; {Unfound} rulings name a verse that holds no such word" : "") +
        (Missing.Count > 0 ? $"; no record for {string.Join(", ", Missing)}" : "") +
        (ByText.Count > 0 ? ". Per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}")) : "");
}

/// <summary>
/// Whose a title is at each occurrence, as the rulings read it: the Anointed in
/// <c>TitleReadings.json</c>, Pharaoh and Caesar in <c>PharaohReadings.json</c> and
/// <c>CaesarReadings.json</c>. What follows is said of the Anointed, where the pass began, and holds
/// for each.
///
/// <para>
/// <see cref="TitleLoader"/> writes the title on every <em>mashiach</em>, <em>Christos</em> and
/// <em>Messias</em>; that says what the word is and not whom it means. Here the word is given its
/// bearer where the text fixes one — Saul in David's mouth, David in his own last words, Cyrus by
/// name — so that it names the title and the person, and is left to the title where the verse
/// asks, denies, supposes or reports somebody's claim.
/// </para>
///
/// <para>
/// **A word left open loses the bearer a rule gave it.** <see cref="FixedTitleLoader"/> read every
/// <em>Christos</em> as Jesus, the questions of the Gospels with the rest. Its rule now leaves those
/// verses out; on a corpus it had already written, its annotation is taken back here from the
/// witness word and from every word it was carried to, and nothing else on those words is touched.
/// </para>
///
/// <para>
/// Idempotent: the words the rulings fix are compared with the ones written last time, and the
/// pass writes only where they differ or something is left to withdraw. After the titles and the
/// fixed titles, before the verses are read off the words.
/// </para>
/// </summary>
internal sealed class TitleReadingLoader(AppDbContext db, ILogger<TitleReadingLoader> logger)
{
    /// <summary>
    /// Every occurrence of the ruled numbers in the witnesses, where it stands and which of its
    /// number it is in its verse.
    /// </summary>
    private const string Occurrences =
        """
        SELECT w.id, w.strong_number, r.canonical_book, r.canonical_chapter, r.canonical_verse,
               row_number() OVER (PARTITION BY w.verse_id, w.strong_number ORDER BY w.position)::int
        FROM word w
        JOIN text t ON t.id = w.text_id AND t.slug = ANY(@witnesses)
        JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
        WHERE w.strong_number = ANY(@numbers)
        """;

    /// <summary>The seeds written last time: a claim of this source that no link produced.</summary>
    private const string Seeded =
        """
        SELECT a.word_id, a.entity_id
        FROM word_entity_claim c
        JOIN word_entity a ON a.id = c.word_entity_id
        WHERE c.source = @source AND coalesce(c.note, '') NOT LIKE @carried
        """;

    /// <summary>
    /// What the fixed-title rule wrote on a word now left open, and what the links carried from it:
    /// a carried row names the witness word it came from in its note.
    /// </summary>
    private const string Stale =
        """
        FROM word_entity a
        WHERE a.source = @fixed
          AND (a.word_id = ANY(@open)
               OR (a.note LIKE @carried
                   AND substring(a.note FROM '^through \S+ word (\d+),')::bigint = ANY(@open)))
        """;

    /// <summary>
    /// A carried word that already names somebody else is not given a second person. A title on
    /// the word is not somebody else: the bearer stands beside it.
    /// </summary>
    private const string Contest =
        """
        DELETE FROM pending_annotation a
        USING word_entity already
        JOIN entity whom ON whom.id = already.entity_id AND whom.kind <> @title
        WHERE a.through IS NOT NULL AND already.word_id = a.word_id AND already.entity_id <> a.entity_id
        """;

    private static readonly string Title = EnumSpelling.Of(EntityKind.Title);

    /// <summary>Every title's rulings in turn, each under its own source.</summary>
    public async Task<IReadOnlyList<TitleReadingOutcome>> LoadAll(CancellationToken cancellationToken = default)
    {
        var outcomes = new List<TitleReadingOutcome>();
        foreach (var rulings in SenseReadingFiles.AllTitleReadings())
        {
            outcomes.Add(await Load(rulings, cancellationToken));
        }

        return outcomes;
    }

    public Task<TitleReadingOutcome> Load(CancellationToken cancellationToken = default) =>
        Load(SenseReadingFiles.TitleReadings(), cancellationToken);

    internal async Task<TitleReadingOutcome> Load(TitleReadings rulings, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var method = EnumSpelling.Of(EnumSpelling.ToLinkMethod(rulings.Method));

        var slugs = rulings.Readings.Select(r => r.Bearer).OfType<string>().Append(rulings.Title).Distinct().ToList();
        var records = await db.Entities.AsNoTracking()
            .Where(e => slugs.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug, e => e.Id, StringComparer.Ordinal, cancellationToken);
        var missing = slugs.Where(slug => !records.ContainsKey(slug)).ToList();
        if (!records.ContainsKey(rulings.Title))
        {
            logger.LogWarning(
                "The rulings read the occurrences of the title \"{Title}\", and the encyclopedia holds no record " +
                "of that slug. Either the titles have not been loaded yet or the record was renamed, and " +
                "TitleReadings.json has to follow it",
                rulings.Title);
            return new TitleReadingOutcome(false, [], 0, 0, missing, 0, 0, [], started.Elapsed)
            {
                Title = rulings.Title,
            };
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var numbers = rulings.Readings.Select(r => r.Strong).Distinct().ToArray();
        var occurrences = new List<(long Id, string Number, int Book, int Chapter, int Verse, int Nth)>();
        await using (var command = new NpgsqlCommand(Occurrences, connection))
        {
            command.Parameters.AddWithValue("witnesses", (string[])[EntityCandidates.Witness, .. EntityCandidates.GreekWitnesses]);
            command.Parameters.AddWithValue("numbers", numbers);
            command.CommandTimeout = Annotating.Patient;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                occurrences.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3),
                    reader.GetInt32(4), reader.GetInt32(5)));
            }
        }

        var fixedTo = new Dictionary<long, (int Entity, string Bearer, string Note, double? Confidence)>();
        var open = new HashSet<long>();
        var unfound = 0;
        foreach (var ruling in rulings.Readings)
        {
            var words = occurrences
                .Where(o => o.Number == ruling.Strong && ruling.At.Holds(o.Book, o.Chapter, o.Verse, o.Nth))
                .ToList();
            if (words.Count == 0)
            {
                logger.LogWarning(
                    "The ruling on {Number} at {Reference} names a verse where no witness carries that number. " +
                    "Write the reference in canonical numbering, as ISA 45:1 or ACT 17:3#1",
                    ruling.Strong, ruling.Reference);
                unfound++;
                continue;
            }

            if (ruling.Bearer is null)
            {
                open.UnionWith(words.Select(w => w.Id));
            }
            else if (records.TryGetValue(ruling.Bearer, out var bearer))
            {
                foreach (var word in words)
                {
                    fixedTo[word.Id] = (bearer, ruling.Bearer,
                        $"{ruling.Strong} at {BookReferences.Name(word.Book)} {word.Chapter}:{word.Verse}: {ruling.Why}",
                        ruling.Confidence);
                }
            }
        }

        var before = new HashSet<(long, int)>();
        await using (var command = new NpgsqlCommand(Seeded, connection))
        {
            command.Parameters.AddWithValue("source", rulings.Source);
            command.Parameters.AddWithValue("carried", Annotating.CarriedNote);
            command.CommandTimeout = Annotating.Patient;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                before.Add((reader.GetInt64(0), reader.GetInt32(1)));
            }
        }

        var byBearer = fixedTo.Values
            .GroupBy(f => f.Bearer, StringComparer.Ordinal)
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(g => g.Item2)
            .ToList();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var withdrawn = 0;
        await using (var withdraw = new NpgsqlCommand(
                         "DELETE " + Stale, connection, (NpgsqlTransaction)transaction.GetDbTransaction()))
        {
            withdraw.Parameters.AddWithValue("fixed", FixedTitleLoader.Source);
            withdraw.Parameters.AddWithValue("open", open.ToArray());
            withdraw.Parameters.AddWithValue("carried", Annotating.CarriedNote);
            withdraw.CommandTimeout = Annotating.Patient;
            withdrawn = await withdraw.ExecuteNonQueryAsync(cancellationToken);
        }

        if (withdrawn == 0 && before.SetEquals(fixedTo.Select(f => (f.Key, f.Value.Entity))))
        {
            await transaction.RollbackAsync(cancellationToken);
            var standing = new TitleReadingOutcome(
                true, byBearer, open.Count, unfound, missing, 0, 0, [], started.Elapsed)
            {
                Title = rulings.Title,
            };
            logger.LogInformation("The occurrences of the title: {Outcome}", standing);
            return standing;
        }

        await Annotating.Run(connection, transaction,
            "DELETE FROM word_entity_claim WHERE source = @source; DELETE FROM word_entity WHERE source = @source",
            cancellationToken, ("source", rulings.Source));
        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Seed(
            connection,
            fixedTo.Select(f => (f.Key, f.Value.Entity, f.Value.Confidence, false, f.Value.Note)),
            cancellationToken);
        await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);
        await using var contest = new NpgsqlCommand(
            Contest, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        contest.Parameters.AddWithValue("title", Title);
        contest.CommandTimeout = Annotating.Patient;
        var contested = await contest.ExecuteNonQueryAsync(cancellationToken);

        await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
            ("method", method), ("source", rulings.Source));
        await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
            ("method", method), ("source", rulings.Source));
        await Annotating.Run(connection, transaction, "DROP TABLE pending_annotation", cancellationToken);
        var byText = await Annotating.ByText(connection, transaction, rulings.Source, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new TitleReadingOutcome(
            false, byBearer, open.Count, unfound, missing, withdrawn, contested, byText, started.Elapsed)
        {
            Title = rulings.Title,
        };
        logger.LogInformation("The occurrences of the title: {Outcome}", outcome);
        return outcome;
    }
}
