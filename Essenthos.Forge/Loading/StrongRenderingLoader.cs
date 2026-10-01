using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading.Encyclopedia;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading;

/// <param name="Numbers">Strong numbers the translation renders at all.</param>
/// <param name="Renderings">Phrases kept, the commonest few of each number.</param>
internal sealed record StrongRenderingOutcome(
    bool AlreadyLoaded,
    string Text,
    int Numbers,
    int Renderings,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? $"the lexicon's {Renderings} phrases from {Text} are already counted, checked in {Elapsed}"
            : $"{Renderings} phrases counted from {Text} for {Numbers} Strong numbers in {Elapsed}";
}

/// <summary>
/// The phrases the lexicon quotes under each entry, counted from the links once rather than for every
/// page a reader turns.
///
/// <para>
/// **Counted whole on every load.** It is a projection of the links, which the recipe, the composed
/// routes and the corrections all write after the texts are loaded, so a guard asking whether it had
/// already run would keep the phrases of links that no longer exist. It is one pass over the
/// translation's links to its originals, about twenty seconds for the King James, and it writes only
/// where the count came out differently, in one transaction so a reader never sees the table half
/// empty.
/// </para>
/// </summary>
internal sealed class StrongRenderingLoader(AppDbContext db, ILogger<StrongRenderingLoader> logger)
{
    public async Task<StrongRenderingOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var text = await db.Texts
            .Where(t => t.Slug == StrongRenderingCounts.CardTranslation)
            .Select(t => new { t.Id, t.Slug })
            .FirstOrDefaultAsync(cancellationToken);
        if (text is null)
        {
            logger.LogWarning(
                "There is no text {Slug}, so the lexicon's phrases were not counted. It is loaded by an " +
                "earlier step of the same pipeline",
                StrongRenderingCounts.CardTranslation);
            return new StrongRenderingOutcome(false, StrongRenderingCounts.CardTranslation, 0, 0, started.Elapsed);
        }

        var counted = await StrongRenderingCounts.Count(
            db, text.Id, null, StrongRenderingCounts.CardRenderings, cancellationToken);

        var held = await db.StrongRenderings
            .Where(r => r.TextId == text.Id)
            .Select(r => new StrongRenderingCount(r.StrongNumber, r.Rank, r.Phrase, r.Uses))
            .ToListAsync(cancellationToken);

        var numbers = counted.Select(row => row.Number).Distinct().Count();
        if (held.ToHashSet().SetEquals(counted))
        {
            return Done(new StrongRenderingOutcome(true, text.Slug, numbers, counted.Count, started.Elapsed));
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.StrongRenderings.Where(r => r.TextId == text.Id).ExecuteDeleteAsync(cancellationToken);

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY strong_rendering (strong_number, text_id, rank, phrase, uses) FROM STDIN (FORMAT BINARY)",
                         cancellationToken))
        {
            foreach (var row in counted)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(row.Number, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(text.Id, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.Rank, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(row.Phrase, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(row.Uses, NpgsqlDbType.Integer, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Done(new StrongRenderingOutcome(false, text.Slug, numbers, counted.Count, started.Elapsed));
    }

    private StrongRenderingOutcome Done(StrongRenderingOutcome outcome)
    {
        logger.LogInformation("The lexicon's phrases: {Outcome}", outcome);
        return outcome;
    }
}
