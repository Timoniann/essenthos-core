using System.Diagnostics;
using Essenthos.Core.Berean;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

internal sealed record BereanNumberOutcome(bool AlreadyNumbered, int Verses, int Drifted, int Words, TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyNumbered
            ? "the Berean already carries the numbers its tables state"
            : $"{Words} Berean words carry the Strong number the tables state for the original word they render, "
              + $"over {Verses} verses in {Elapsed}; {Drifted} verses whose English did not line up are left without";
}

/// <summary>
/// The Strong number the Berean's tables state for each original word, kept on the English words that
/// render it.
///
/// <para>
/// Each row of the tables is one Hebrew or Greek word with its number (<c>Str Heb</c>, <c>Str Grk</c>)
/// and the English phrase that renders it. The link loader uses the row to join the phrase to BHSA or
/// Nestle and has no place for the number, so without this the Berean carried none, and neither
/// English text carried one anywhere between Genesis and Malachi. A number here is the file's
/// statement about its own English, independent of how BHSA or Nestle number the word — which is why
/// it lives in <c>word_strong</c> under the tables' name rather than in <c>word.strong_number</c>, and
/// why every word of a phrase carries its row's number: the tables number the phrase, and saying which
/// of its words the number belongs to would be ours.
/// </para>
///
/// <para>
/// The phrases are walked against our verse exactly as the link loader walks them, and a verse where
/// they part gets nothing. Idempotent on its own rows: a corpus that has any is left alone.
/// </para>
/// </summary>
internal sealed class BereanNumberLoader(AppDbContext db, ILogger<BereanNumberLoader> logger)
{
    public const string Source =
        "Berean Standard Bible translation tables (Str Heb, Str Grk), bereanbible.com, public domain";

    private const string Import =
        "COPY word_strong (word_id, number, method, source) FROM STDIN (FORMAT BINARY)";

    public async Task<BereanNumberOutcome> Load(string tables, CancellationToken cancellationToken = default)
    {
        var text = await db.Texts
            .Where(t => t.Slug == BereanTextSource.Slug)
            .Select(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (text == 0)
        {
            return new BereanNumberOutcome(true, 0, 0, 0, TimeSpan.Zero);
        }

        if (await db.WordStrongs.AnyAsync(s => s.Source == Source, cancellationToken))
        {
            logger.LogInformation("The Berean already carries the numbers its tables state; nothing to do");
            return new BereanNumberOutcome(true, 0, 0, 0, TimeSpan.Zero);
        }

        if (!File.Exists(tables))
        {
            logger.LogWarning(
                "The Berean tables are not at {Path}, so the Berean carries no Strong numbers. They are 85 MB "
                + "and are fetched rather than committed; run scripts/fetch-berean.ps1", tables);
            return new BereanNumberOutcome(true, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var english = await BereanLinkLoader.WordsByAddress(db, text, cancellationToken);
        var numbered = new HashSet<(long Word, string Number)>(800_000);
        int verses = 0, drifted = 0;

        foreach (var (reference, rows) in BereanTable.Verses(tables))
        {
            if (!BereanTextSource.Address(reference, out var book, out var chapter, out var number)
                || !english.TryGetValue((book, chapter, number), out var ours))
            {
                continue;
            }

            if (BereanLinkLoader.Claims(rows, ours) is not { } claimed)
            {
                drifted++;
                continue;
            }

            verses++;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].StrongNumber.Length == 0)
                {
                    continue;
                }

                foreach (var word in claimed[i] ?? [])
                {
                    numbered.Add((word, rows[i].StrongNumber));
                }
            }
        }

        await Write(numbered, cancellationToken);

        var outcome = new BereanNumberOutcome(false, verses, drifted, numbered.Count, started.Elapsed);
        logger.LogInformation("{Outcome}", outcome);
        return outcome;
    }

    private async Task Write(HashSet<(long Word, string Number)> numbered, CancellationToken cancellationToken)
    {
        if (numbered.Count == 0)
        {
            return;
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var method = EnumSpelling.Of(LinkMethod.StatedBySource);

        await using var writer = await connection.BeginBinaryImportAsync(Import, cancellationToken);
        foreach (var (word, number) in numbered)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(word, NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(number, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(Source, NpgsqlDbType.Text, cancellationToken);
        }

        await writer.CompleteAsync(cancellationToken);
    }
}
