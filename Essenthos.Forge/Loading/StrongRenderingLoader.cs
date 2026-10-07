using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Links.Evidentia;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading;

/// <param name="Texts">Texts linked to an original, each counted.</param>
/// <param name="Numbers">Strong numbers counted, summed over the texts.</param>
/// <param name="Renderings">Phrases kept, the commonest of each number in each text.</param>
internal sealed record StrongRenderingOutcome(
    bool AlreadyLoaded,
    int Texts,
    int Numbers,
    int Renderings,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? $"the lexicon's {Renderings} phrases from {Texts} texts are already counted, checked in {Elapsed}"
            : $"{Renderings} phrases counted from {Texts} texts for {Numbers} Strong numbers in {Elapsed}";
}

/// <summary>
/// Which texts a Forge run changed the links of, so their Strong counts are counted again at its end:
/// the texts named, the two texts of a stored EVIDENTIA run, or every text where the run cannot say.
/// </summary>
internal sealed record Relinked(IReadOnlyList<string>? Texts, int? EvidentiaRun = null)
{
    public static Relinked None { get; } = new([]);

    public static Relinked Every { get; } = new((IReadOnlyList<string>?)null);

    public static Relinked Of(params string[] texts) => new([.. texts.Select(text => text.ToUpperInvariant()).Distinct()]);

    public static Relinked Run(int run) => new([], run);

    public bool Nothing => Texts is { Count: 0 } && EvidentiaRun is null;

    public override string ToString() =>
        EvidentiaRun is { } run ? $"the texts of EVIDENTIA run {run}"
        : Texts is null ? "every text"
        : string.Join(", ", Texts);
}

/// <summary>
/// What the lexicon and the entry page say about how each text renders each Strong number — the
/// phrases, how many of the number's places it reaches and by which methods — counted from the links
/// once rather than for every page a reader turns.
///
/// <para>
/// **A run that changes links outside a load counts its own texts again** (<see cref="Relinked"/>),
/// at the end, the way it ends with a vacuum: otherwise the page keeps the old numbers for that text
/// until the next load.
/// </para>
///
/// <para>
/// **Counted whole on every load.** It is a projection of the links, which the recipe, the composed
/// routes and the corrections all write after the texts are loaded, so a guard asking whether it had
/// already run would keep the counts of links that no longer exist. It is one pass over each text's
/// links to its originals, about ten seconds for the King James, and it writes only the texts whose
/// count came out differently, each in one transaction so a reader never sees a text half counted.
/// </para>
/// </summary>
internal sealed class StrongRenderingLoader(AppDbContext db, ILogger<StrongRenderingLoader> logger)
{
    public Task<StrongRenderingOutcome> Load(CancellationToken cancellationToken = default) =>
        Load(Relinked.Every, cancellationToken);

    /// <summary>The texts a run changed the links of, counted again; the rest are left as they were counted.</summary>
    public async Task<StrongRenderingOutcome> Load(Relinked relinked, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var named = relinked.Texts?.ToList() ?? [];
        if (relinked.EvidentiaRun is { } run)
        {
            foreach (var pair in await db.EvidentiaRuns.Where(r => r.Id == run)
                         .Select(r => new { From = r.FromText!.Slug, To = r.ToText!.Slug })
                         .ToListAsync(cancellationToken))
            {
                named.AddRange([pair.From, pair.To]);
            }
        }

        var every = relinked.Texts is null;
        var texts = await db.Texts
            .Where(t => every || named.Contains(t.Slug))
            .OrderBy(t => t.Slug)
            .Select(t => new { t.Id, t.Slug, t.Language })
            .ToListAsync(cancellationToken);

        var lexical = Lexical(await StrongRenderingCounts.ClassesOf(db, cancellationToken));
        int counted = 0, numbers = 0, renderings = 0;
        var written = false;
        var editions = new HashSet<int>();
        foreach (var text in texts)
        {
            var primary = LinkedOriginals.Primary(await LinkedOriginals.Of(db, text.Id, cancellationToken));
            editions.UnionWith(primary.Select(original => original.Id));
            var count = primary.Count == 0
                ? new StrongTextCount([], [])
                : await StrongRenderingCounts.CountText(db, text.Id, primary, cancellationToken);

            if (primary.Count > 0)
            {
                counted++;
                numbers += count.Renderings.Select(row => row.Number).Concat(count.Reach.Select(row => row.Number))
                    .Distinct().Count();
                renderings += count.Renderings.Count;
            }

            var variety = RenderingVariety.Count(text.Language, count.Everything);
            if (await Holds(text.Id, count, variety, lexical, cancellationToken))
            {
                continue;
            }

            await Replace(text.Id, count, variety, lexical, cancellationToken);
            written = true;
            logger.LogInformation(
                "Counted {Text}: {Renderings} phrases, {Numbers} numbers reached",
                text.Slug, count.Renderings.Count, count.Reach.Count);
        }

        foreach (var edition in editions.Order())
        {
            written |= await CountTheBooks(edition, cancellationToken);
        }

        var outcome = new StrongRenderingOutcome(!written, counted, numbers, renderings, started.Elapsed);
        logger.LogInformation("The lexicon's phrases: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// How often each number stands in each book of one edition, written only where it changed: the
    /// words of an edition move far less often than the links a text's count rests on.
    /// </summary>
    private async Task<bool> CountTheBooks(int witnessId, CancellationToken cancellationToken)
    {
        var counted = await StrongRenderingCounts.CountBooks(db, witnessId, cancellationToken);
        var held = await db.StrongBooks.AsNoTracking()
            .Where(b => b.WitnessId == witnessId)
            .Select(b => new StrongBookCount(b.StrongNumber, b.Book, b.Occurrences))
            .ToListAsync(cancellationToken);
        if (held.ToHashSet().SetEquals(counted))
        {
            return false;
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.StrongBooks.Where(b => b.WitnessId == witnessId).ExecuteDeleteAsync(cancellationToken);
        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY strong_book (witness_id, strong_number, book, occurrences) FROM STDIN (FORMAT BINARY)",
                         cancellationToken))
        {
            foreach (var row in counted)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(witnessId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.Number, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(row.Book, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.Count, NpgsqlDbType.Integer, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Counted the books of edition {Witness}: {Rows} numbers by book", witnessId, counted.Count);
        return true;
    }

    /// <summary>Whether what is kept for the text is exactly what was counted.</summary>
    /// <summary>
    /// Whether each number is a word of content: most of the words carrying it, in every edition
    /// that states a part of speech, are of an open class. A number no edition classes is unknown.
    /// </summary>
    internal static IReadOnlyDictionary<string, bool> Lexical(IEnumerable<(string Number, string Label, int Words)> classes) =>
        classes
            .GroupBy(row => row.Number, StringComparer.Ordinal)
            .Select(number =>
            {
                var language = number.Key.StartsWith('G') ? "grc" : "hbo";
                var classed = number
                    .Where(row => EvidentiaMorphologyLabels.PartOfSpeech(row.Label, language) is not null)
                    .ToList();
                var open = classed.Where(row => EvidentiaMorphologyLabels.IsOpenClass(row.Label, language)).Sum(row => row.Words);
                return (number.Key, Known: classed.Count > 0, Open: open * 2 > classed.Sum(row => row.Words));
            })
            .Where(number => number.Known)
            .ToDictionary(number => number.Key, number => number.Open, StringComparer.Ordinal);

    private async Task<bool> Holds(
        int textId,
        StrongTextCount count,
        TextVariety variety,
        IReadOnlyDictionary<string, bool> lexical,
        CancellationToken cancellationToken)
    {
        var phrases = await db.StrongRenderings
            .Where(r => r.TextId == textId)
            .Select(r => new StrongRenderingCount(r.StrongNumber, r.Rank, r.Phrase, r.Uses))
            .ToListAsync(cancellationToken);
        if (!phrases.ToHashSet().SetEquals(count.Renderings))
        {
            return false;
        }

        var reach = await db.StrongReaches
            .Where(r => r.TextId == textId)
            .Select(r => new { r.StrongNumber, r.WitnessId, r.Occurrences, r.Reached, r.Phrases, r.Renderings, r.RenderingLinks, r.Lexical })
            .ToListAsync(cancellationToken);
        if (!reach.Select(r => (r.StrongNumber, r.WitnessId, r.Occurrences, r.Reached, new Variety(r.Phrases, r.Renderings, r.RenderingLinks), r.Lexical))
                .ToHashSet()
                .SetEquals(count.Reach.Select(r => (r.Number, r.WitnessId, r.Occurrences, r.Reached, variety.Of(r.Number), LexicalOf(lexical, r.Number)))))
        {
            return false;
        }

        var books = await db.StrongBookReaches
            .Where(b => b.TextId == textId)
            .Select(b => new StrongBookCount(b.StrongNumber, b.Book, b.Reached))
            .ToListAsync(cancellationToken);
        if (!books.ToHashSet().SetEquals(count.ByBook))
        {
            return false;
        }

        var methods = await db.StrongReachMethods
            .Where(m => m.TextId == textId)
            .Select(m => new { m.StrongNumber, m.Method, m.Links })
            .ToListAsync(cancellationToken);
        return methods.Select(m => (m.StrongNumber, m.Method, m.Links)).ToHashSet()
            .SetEquals(Methods(count));
    }

    private static IEnumerable<(string Number, LinkMethod Method, int Links)> Methods(StrongTextCount count) =>
        count.Reach.SelectMany(r => r.Methods.Select(m => (r.Number, m.Method, m.Links)));

    private static bool? LexicalOf(IReadOnlyDictionary<string, bool> lexical, string number) =>
        lexical.TryGetValue(number, out var open) ? open : null;

    private async Task Replace(
        int textId,
        StrongTextCount count,
        TextVariety variety,
        IReadOnlyDictionary<string, bool> lexical,
        CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.StrongRenderings.Where(r => r.TextId == textId).ExecuteDeleteAsync(cancellationToken);
        await db.StrongReachMethods.Where(m => m.TextId == textId).ExecuteDeleteAsync(cancellationToken);
        await db.StrongReaches.Where(r => r.TextId == textId).ExecuteDeleteAsync(cancellationToken);
        await db.StrongBookReaches.Where(b => b.TextId == textId).ExecuteDeleteAsync(cancellationToken);

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY strong_rendering (strong_number, text_id, rank, phrase, uses) FROM STDIN (FORMAT BINARY)",
                         cancellationToken))
        {
            foreach (var row in count.Renderings)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(row.Number, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(textId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.Rank, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.Phrase, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(row.Uses, NpgsqlDbType.Integer, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY strong_reach (text_id, strong_number, witness_id, occurrences, reached, phrases, renderings, rendering_links, lexical) " +
                         "FROM STDIN (FORMAT BINARY)",
                         cancellationToken))
        {
            foreach (var row in count.Reach)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(textId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.Number, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(row.WitnessId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.Occurrences, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.Reached, NpgsqlDbType.Integer, cancellationToken);
                var counted = variety.Of(row.Number);
                await writer.WriteAsync(counted.Phrases, NpgsqlDbType.Integer, cancellationToken);
                if (counted.Renderings is { } renderings)
                {
                    await writer.WriteAsync(renderings, NpgsqlDbType.Integer, cancellationToken);
                }
                else
                {
                    await writer.WriteNullAsync(cancellationToken);
                }

                await writer.WriteAsync(counted.RenderingLinks, NpgsqlDbType.Integer, cancellationToken);
                if (LexicalOf(lexical, row.Number) is { } open)
                {
                    await writer.WriteAsync(open, NpgsqlDbType.Boolean, cancellationToken);
                }
                else
                {
                    await writer.WriteNullAsync(cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY strong_reach_method (text_id, strong_number, method, links) FROM STDIN (FORMAT BINARY)",
                         cancellationToken))
        {
            foreach (var (number, method, links) in Methods(count))
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(textId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(number, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(EnumSpelling.Of(method), NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(links, NpgsqlDbType.Integer, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY strong_book_reach (text_id, strong_number, book, reached) FROM STDIN (FORMAT BINARY)",
                         cancellationToken))
        {
            foreach (var row in count.ByBook)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(textId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.Number, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(row.Book, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.Count, NpgsqlDbType.Integer, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
