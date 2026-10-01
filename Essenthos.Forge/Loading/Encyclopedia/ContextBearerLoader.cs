using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Measured">
/// The rule asked of every original word already settled on one of several candidates, its own verse
/// left out: how many it answered and how many of those it answered as the corpus had.
/// </param>
/// <param name="Agreed">The same, over the words whose verse the dataset's list files under the same record.</param>
/// <param name="Unnamed">Original words printing a name nothing had named and no reading had answered.</param>
/// <param name="Answered">Of those, the ones the book settles on one bearer.</param>
/// <param name="Contradicted">Of those, the ones the dataset's list files under another of the candidates, left for review.</param>
/// <param name="Contested">Words the links reach that already name somebody else, left for review.</param>
/// <param name="Written">Annotations written under this pass's source, the carried ones included.</param>
internal sealed record ContextBearerOutcome(
    bool AlreadyLoaded,
    bool NoReadings,
    (int Tested, int Answered, int Right) Measured,
    (int Answered, int Right) Agreed,
    int Unnamed,
    int Answered,
    int Contradicted,
    int Contested,
    int Written,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public static double Precision((int Answered, int Right) tally) =>
        tally.Answered == 0 ? 0 : (double)tally.Right / tally.Answered;

    public override string ToString() =>
        AlreadyLoaded ? "the names a book settles on one bearer are already named"
        : NoReadings ? "no model readings are on this disk, so which names were already read is unknown and nothing was written"
        : $"held out, the rule answered {Measured.Answered} of {Measured.Tested} settled namesake words at " +
          $"{Precision((Measured.Answered, Measured.Right)):P2}, and {Agreed.Answered} the dataset files alike at " +
          $"{Precision(Agreed):P2}. {Answered} of {Unnamed} unnamed original names are the one bearer their book " +
          $"names; {Contradicted} of them the dataset files under another bearer and {Contested} carried words " +
          $"already name somebody else, all listed for review. {Written} words written in {Elapsed}. Per text: " +
          string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// A name several records bear, printed where nothing settled which of them it is, settled by the one
/// of them the rest of the book names — see <see cref="ContextBearers"/> for the rule.
///
/// <para>
/// Only the originals are read and only this corpus's own settled words are evidence; a dataset's
/// verse list is never the answer. It is the brake: where the list files the verse under another of
/// the candidates, nothing is written and the word goes to the review list, and so does every word
/// the links reach that already names somebody else. A word a reading already answered keeps that
/// answer, so without the readings on disk nothing is written. One answer is not the last word: a
/// reading shown the bearers of the word's number that found none of them there has said nothing
/// of a record held under another number of the same name, as Saul's son is, and the book may
/// still name that record.
/// </para>
///
/// <para>
/// Idempotent on its own source. It runs after every pass that names an original word, so what is
/// left unnamed is what they all left.
/// </para>
/// </summary>
internal sealed class ContextBearerLoader(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<ContextBearerLoader> logger)
{
    public const string Source =
        "Essenthos, reading a name several records bear as the one of them the rest of the book names";

    /// <summary>
    /// The rule's precision held out over the originals' settled namesake words, rounded down: on
    /// 2026-09-30 it answered 8,556 of 14,985 at 99.67%, and 3,907 the dataset's list files alike
    /// at 99.56%.
    /// </summary>
    private const double Measured = 0.99;

    public static readonly string[] ReviewFile = ["Essenthos", "review", "context-bearer-disagreements.json"];

    private static readonly string[] Originals = [BhsaTextSource.Slug, NestleTextSource.Slug];

    private static readonly string Names =
        $"""
         SELECT e.id, e.kind, coalesce(n.hebrew_strong_number, n.greek_strong_number, ''), n.label
         FROM entity_name n
         JOIN entity e ON e.id = {EntityCandidates.Resolves}
         WHERE e.kind IN ('person', 'place', 'people')
           AND coalesce(n.kind, '') NOT IN ('title', 'description', 'term', 'gentilic', 'collective')
           AND position(',' IN coalesce(n.hebrew_strong_number, '') || coalesce(n.greek_strong_number, '')) = 0
         UNION
         SELECT e.id, e.kind, '', e.name FROM entity e WHERE e.kind IN ('person', 'place', 'people')
         """;

    private static readonly string Attested =
        $"""
         WITH {Annotating.Settled}
         SELECT s.entity_id, r.canonical_book, r.canonical_chapter, r.canonical_verse,
                coalesce(w.strong_number, '')
         FROM settled s
         JOIN word w ON w.id = s.word_id
         JOIN text t ON t.id = w.text_id AND t.slug = ANY(@originals)
         JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
         """;

    private const string Unnamed =
        """
        SELECT w.id, r.canonical_book, r.canonical_chapter, r.canonical_verse, w.strong_number,
               w.morphology->>'nameType', t.slug, w.position, w.text
        FROM word w
        JOIN text t ON t.id = w.text_id AND t.slug = ANY(@originals)
        JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
        WHERE w.strong_number = ANY(@numbers)
          AND (t.slug <> @witness OR w.morphology->>'pos' = 'nmpr')
          AND NOT EXISTS (SELECT 1 FROM word_entity spoken WHERE spoken.word_id = w.id)
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
        RETURNING a.word_id, a.entity_id, already.entity_id
        """;

    public async Task<ContextBearerOutcome> Load(string resources, CancellationToken cancellationToken = default)
    {
        if (await db.WordEntities.AnyAsync(a => a.Source == Source, cancellationToken))
        {
            logger.LogInformation("The names a book settles on one bearer are already named; nothing to do");
            return new ContextBearerOutcome(true, false, default, default, 0, 0, 0, 0, 0, [], TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var bearers = new NameBearers(await Read(connection, Names, reader => new BearerName(
            reader.GetInt32(0), EnumSpelling.ToEntityKind(reader.GetString(1)), reader.GetString(2),
            reader.GetString(3)), cancellationToken));
        var attested = await Read(connection, Attested, reader => new Attestation(
            reader.GetInt32(0), new Address(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)),
            reader.GetString(4)),
            cancellationToken, ("originals", Originals));
        var listed = (await Read(connection, Listed, reader => (reader.GetInt32(0),
            new Address(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3))),
            cancellationToken, ("dataset", BibleDataLoader.Source))).ToHashSet();
        var context = new ContextBearers(attested, bearers);

        var (measured, agreed) = await HeldOut(connection, bearers, context, listed, cancellationToken);

        var directory = Where(resources);
        if (!Directory.Exists(directory))
        {
            logger.LogInformation(
                "No model readings at {Directory}, so which names a reading already answered is unknown and " +
                "no name was taken from its book. Point \"{Key}\" at the run folders to let this pass run",
                directory, SenseReadingFiles.ConfigurationKey);
            return new ContextBearerOutcome(false, true, measured, agreed, 0, 0, 0, 0, 0, [], started.Elapsed);
        }

        var (readings, contradicted, _, _, _) = SenseReadingFiles.Read(directory);
        var foundNobodyListed = readings.Where(r => r.Referent == SenseReading.Unlisted)
            .Select(r => r.WordId)
            .Except(contradicted)
            .ToHashSet();
        var answeredByReading = readings.Select(r => r.WordId)
            .Concat(contradicted)
            .Concat(SenseReadingFiles.Refused().Readings.Select(r => r.WordId))
            .ToHashSet();

        var words = await Read(connection, Unnamed, reader => new UnnamedWord(
            reader.GetInt64(0), new Address(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)),
            reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6),
            reader.GetInt32(7), reader.GetString(8)),
            cancellationToken, ("originals", Originals), ("witness", BhsaTextSource.Slug),
            ("numbers", bearers.Numbers.ToArray()));
        words = words
            .Where(word => !answeredByReading.Contains(word.Id) || foundNobodyListed.Contains(word.Id))
            .ToList();

        var slugs = await db.Entities.AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.Slug, cancellationToken);
        var seed = new List<(long, int, double?, bool, string)>();
        var review = new List<Disagreement>();
        foreach (var word in words)
        {
            var candidates = bearers.Of(word.Number, word.Marking);
            if (context.Bearer(word.At, candidates, word.Number) is not { } bearer)
            {
                continue;
            }

            // A reading that found nobody among the bearers of the number it was shown has not
            // spoken of a record held under another number of the name; of one it was shown, it has.
            var underAnotherNumber = foundNobodyListed.Contains(word.Id);
            if (underAnotherNumber && bearers.Bears(bearer, word.Number))
            {
                continue;
            }

            var filed = candidates.Where(candidate => listed.Contains((candidate, word.At))).ToList();
            if (filed.Count > 0 && !filed.Contains(bearer))
            {
                review.Add(Disagreement.Of(word, slugs[bearer], "the dataset's list", filed.Select(id => slugs[id])));
                continue;
            }

            seed.Add((word.Id, bearer, Measured, filed.Count > 0,
                underAnotherNumber
                    ? $"{word.Number}, a number of the name this record is held under another number of; the book " +
                      "names only this record by it, and a reading found none of this number's own bearers here"
                    : $"{word.Number}, of which the book names only this record"));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Seed(connection, seed, cancellationToken);
        await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);
        var contested = await Contested(connection, transaction, cancellationToken);
        review.AddRange(await Described(connection, transaction, contested, slugs, cancellationToken));

        var method = EnumSpelling.Of(LinkMethod.RuleBased);
        await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
            ("method", method), ("source", Source));
        await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
            ("method", method), ("source", Source));

        var byText = await Annotating.ByText(connection, transaction, Source, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await Review(Path.Combine([resources, .. ReviewFile]), review, cancellationToken);

        var outcome = new ContextBearerOutcome(
            false, false, measured, agreed, words.Count, seed.Count + review.Count(r => r.By != Carried),
            review.Count(r => r.By != Carried), contested.Count, byText.Sum(t => t.Words), byText,
            started.Elapsed);
        logger.LogInformation("Named the names a book settles on one bearer: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The rule asked of every original word already settled on one of two or more candidates, with
    /// the word's verse left out, which is how it is asked of a word nothing settled.
    /// </summary>
    private static async Task<((int, int, int) Measured, (int, int) Agreed)> HeldOut(
        NpgsqlConnection connection,
        NameBearers bearers,
        ContextBearers context,
        HashSet<(int, Address)> listed,
        CancellationToken cancellationToken)
    {
        var settled = await Read(connection,
            $"""
             WITH {Annotating.Settled}
             SELECT s.entity_id, r.canonical_book, r.canonical_chapter, r.canonical_verse,
                    w.strong_number, w.morphology->>'nameType'
             FROM settled s
             JOIN word w ON w.id = s.word_id AND w.strong_number IS NOT NULL
             JOIN text t ON t.id = w.text_id AND t.slug = ANY(@originals)
             JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
             """,
            reader => (Entity: reader.GetInt32(0),
                At: new Address(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)),
                Number: reader.GetString(4), Marking: reader.IsDBNull(5) ? null : reader.GetString(5)),
            cancellationToken, ("originals", Originals));

        int tested = 0, answered = 0, right = 0, agreedAnswered = 0, agreedRight = 0;
        foreach (var word in settled)
        {
            var candidates = bearers.Of(word.Number, word.Marking);
            if (candidates.Count < 2 || !candidates.Contains(word.Entity))
            {
                continue;
            }

            tested++;
            if (context.Bearer(word.At, candidates, word.Number) is not { } bearer)
            {
                continue;
            }

            var isRight = bearer == word.Entity;
            answered++;
            right += isRight ? 1 : 0;
            if (listed.Contains((word.Entity, word.At)))
            {
                agreedAnswered++;
                agreedRight += isRight ? 1 : 0;
            }
        }

        return ((tested, answered, right), (agreedAnswered, agreedRight));
    }

    private static async Task<List<(long Word, int Found, int Already)>> Contested(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            Contest, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        var rows = new List<(long, int, int)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2)));
        }

        return rows;
    }

    private static async Task<IEnumerable<Disagreement>> Described(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        IReadOnlyList<(long Word, int Found, int Already)> contested,
        IReadOnlyDictionary<int, string> slugs,
        CancellationToken cancellationToken)
    {
        if (contested.Count == 0)
        {
            return [];
        }

        await using var command = new NpgsqlCommand(
            """
            SELECT w.id, t.slug, r.canonical_book, r.canonical_chapter, r.canonical_verse, w.position, w.text
            FROM word w
            JOIN text t ON t.id = w.text_id
            JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
            WHERE w.id = ANY(@words)
            """,
            connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("words", contested.Select(c => c.Word).Distinct().ToArray());
        var where = new Dictionary<long, UnnamedWord>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                where[reader.GetInt64(0)] = new UnnamedWord(reader.GetInt64(0),
                    new Address(reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)), "", null,
                    reader.GetString(1), reader.GetInt32(5), reader.GetString(6));
            }
        }

        return contested.GroupBy(c => (c.Word, c.Found)).Select(group => Disagreement.Of(
            where[group.Key.Word], slugs[group.Key.Found], Carried, group.Select(c => slugs[c.Already])));
    }

    private const string Carried = "an annotation the word already carries";

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

    private string Where(string resources)
    {
        var configured = configuration[SenseReadingFiles.ConfigurationKey];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(resources, SenseReadingFiles.DefaultFolder)
            : configured;
    }

    /// <summary>The review list, written whole on every run that writes.</summary>
    private static async Task Review(string path, IReadOnlyList<Disagreement> found, CancellationToken cancellationToken)
    {
        var json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var list = new ReviewList(
            "Words where the one bearer of a name the rest of the book names disagrees with the dataset's verse list "
            + "or with an annotation the word already carries. Nothing here was written. Reference is canonical "
            + "book:chapter:verse, position the word's place in the verse from 1, found the record the book names, "
            + "by what disagrees and against the records it names.",
            DateTimeOffset.UtcNow,
            [.. found.OrderBy(entry => entry.Text, StringComparer.Ordinal)
                .ThenBy(entry => entry.Reference, StringComparer.Ordinal).ThenBy(entry => entry.Position)]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(list, json) + "\n", cancellationToken);
    }

    private sealed record UnnamedWord(
        long Id,
        Address At,
        string Number,
        string? Marking,
        string Text,
        int Position,
        string Surface);

    private sealed record Disagreement(
        string Text,
        string Reference,
        int Position,
        string Word,
        string Found,
        string By,
        IReadOnlyList<string> Against)
    {
        public static Disagreement Of(UnnamedWord word, string found, string by, IEnumerable<string> against) =>
            new(word.Text, $"{word.At.Book}:{word.At.Chapter}:{word.At.Verse}", word.Position, word.Surface, found,
                by, [.. against.Distinct().Order(StringComparer.Ordinal)]);
    }

    private sealed record ReviewList(string About, DateTimeOffset Written, IReadOnlyList<Disagreement> Words);
}
