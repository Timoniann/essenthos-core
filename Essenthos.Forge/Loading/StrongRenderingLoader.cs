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
/// What the lexicon and the entry page say about how each text renders each Strong number — the
/// phrases, how many of the number's places it reaches and by which methods — counted from the links
/// once rather than for every page a reader turns.
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
    public async Task<StrongRenderingOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var texts = await db.Texts.OrderBy(t => t.Slug).Select(t => new { t.Id, t.Slug }).ToListAsync(cancellationToken);

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

            if (await Holds(text.Id, count, cancellationToken))
            {
                continue;
            }

            await Replace(text.Id, count, cancellationToken);
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
    private async Task<bool> Holds(int textId, StrongTextCount count, CancellationToken cancellationToken)
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
            .Select(r => new { r.StrongNumber, r.WitnessId, r.Occurrences, r.Reached })
            .ToListAsync(cancellationToken);
        if (!reach.Select(r => (r.StrongNumber, r.WitnessId, r.Occurrences, r.Reached)).ToHashSet()
                .SetEquals(count.Reach.Select(r => (r.Number, r.WitnessId, r.Occurrences, r.Reached))))
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

    private async Task Replace(int textId, StrongTextCount count, CancellationToken cancellationToken)
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
                         "COPY strong_reach (text_id, strong_number, witness_id, occurrences, reached) FROM STDIN (FORMAT BINARY)",
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
