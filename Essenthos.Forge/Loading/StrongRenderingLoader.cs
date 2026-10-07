using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
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

        int counted = 0, numbers = 0, renderings = 0;
        var written = false;
        foreach (var text in texts)
        {
            var primary = LinkedOriginals.Primary(await LinkedOriginals.Of(db, text.Id, cancellationToken));
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
            if (await Holds(text.Id, count, variety, cancellationToken))
            {
                continue;
            }

            await Replace(text.Id, count, variety, cancellationToken);
            written = true;
            logger.LogInformation(
                "Counted {Text}: {Renderings} phrases, {Numbers} numbers reached",
                text.Slug, count.Renderings.Count, count.Reach.Count);
        }

        var outcome = new StrongRenderingOutcome(!written, counted, numbers, renderings, started.Elapsed);
        logger.LogInformation("The lexicon's phrases: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>Whether what is kept for the text is exactly what was counted.</summary>
    private async Task<bool> Holds(
        int textId,
        StrongTextCount count,
        TextVariety variety,
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
            .Select(r => new { r.StrongNumber, r.WitnessId, r.Occurrences, r.Reached, r.Phrases, r.Renderings, r.RenderingLinks })
            .ToListAsync(cancellationToken);
        if (!reach.Select(r => (r.StrongNumber, r.WitnessId, r.Occurrences, r.Reached, new Variety(r.Phrases, r.Renderings, r.RenderingLinks)))
                .ToHashSet()
                .SetEquals(count.Reach.Select(r => (r.Number, r.WitnessId, r.Occurrences, r.Reached, variety.Of(r.Number)))))
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

    private async Task Replace(
        int textId,
        StrongTextCount count,
        TextVariety variety,
        CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.StrongRenderings.Where(r => r.TextId == textId).ExecuteDeleteAsync(cancellationToken);
        await db.StrongReachMethods.Where(m => m.TextId == textId).ExecuteDeleteAsync(cancellationToken);
        await db.StrongReaches.Where(r => r.TextId == textId).ExecuteDeleteAsync(cancellationToken);

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
                         "COPY strong_reach (text_id, strong_number, witness_id, occurrences, reached, phrases, renderings, rendering_links) " +
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

        await transaction.CommitAsync(cancellationToken);
    }
}
