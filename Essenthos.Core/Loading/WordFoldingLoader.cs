using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Endpoints;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading;

internal sealed record FoldingOutcome(int Folded, int Texts, TimeSpan Elapsed)
{
    public override string ToString() =>
        Folded == 0
            ? "every word already has a searchable form"
            : $"{Folded} words of {Texts} texts given a searchable form in {Elapsed}";
}

/// <summary>
/// Fills the form a word is searched by, for every word that has none.
///
/// Idempotent by the column itself: a word with a folded form is skipped, so this costs one
/// indexed count on a loaded corpus and does the work exactly once per text. That is what lets it
/// sit in the startup pipeline beside the other fillers rather than being a migration somebody has
/// to remember to run.
///
/// It writes through a temporary table and one UPDATE ... FROM rather than a row at a time,
/// because four and a half million single-row updates is an afternoon.
/// </summary>
internal sealed class WordFoldingLoader(AppDbContext db, ILogger<WordFoldingLoader> logger)
{
    /// <summary>Enough to keep the round trips down; small enough that the temporary table fits.</summary>
    private const int Batch = 200_000;

    public async Task<FoldingOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var pending = await db.Words.CountAsync(w => w.NormalisedText == null, cancellationToken);
        if (pending == 0)
        {
            logger.LogInformation("Every word already has a searchable form; nothing to do");
            return new FoldingOutcome(0, 0, TimeSpan.Zero);
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var folded = 0;
        var texts = new HashSet<int>();

        while (true)
        {
            var rows = await db.Words
                .Where(w => w.NormalisedText == null)
                .Select(w => new { w.Id, w.Surface, w.TextId, Language = w.Text!.Language })
                .Take(Batch)
                .ToListAsync(cancellationToken);

            if (rows.Count == 0)
            {
                break;
            }

            await using (var command = new NpgsqlCommand(
                // No ON COMMIT DROP: nothing here opens a transaction, so each statement commits
                // on its own and the table would be gone before the COPY could reach it.
                "CREATE TEMP TABLE IF NOT EXISTS folding (id bigint PRIMARY KEY, folded text NOT NULL); " +
                "TRUNCATE folding", connection))
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var writer = await connection.BeginBinaryImportAsync(
                "COPY folding (id, folded) FROM STDIN (FORMAT BINARY)", cancellationToken))
            {
                foreach (var row in rows)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(row.Id, NpgsqlDbType.Bigint, cancellationToken);
                    await writer.WriteAsync(
                        WordFolding.Fold(row.Surface, row.Language), NpgsqlDbType.Text, cancellationToken);
                    texts.Add(row.TextId);
                }

                await writer.CompleteAsync(cancellationToken);
            }

            await using (var command = new NpgsqlCommand(
                "UPDATE word SET normalised_text = folding.folded FROM folding WHERE word.id = folding.id",
                connection))
            {
                folded += await command.ExecuteNonQueryAsync(cancellationToken);
            }

            logger.LogInformation("Gave {Folded} of {Pending} words a searchable form", folded, pending);
        }

        var outcome = new FoldingOutcome(folded, texts.Count, started.Elapsed);
        logger.LogInformation("Folded: {Outcome}", outcome);
        return outcome;
    }
}
