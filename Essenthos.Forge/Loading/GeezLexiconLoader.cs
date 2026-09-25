using System.Diagnostics;
using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading;

internal sealed record GeezLexiconOutcome(bool AlreadyLoaded, bool Missing, int Read, int Loaded, int Spellings, TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded ? "Dillmann's lexicon is already loaded"
        : Missing ? "Dillmann's lexicon is not in Resources/Dillmann; run scripts/fetch-dillmann.ps1 to load it"
        : $"{Loaded} of Dillmann's {Read} entries, under {Spellings} spellings, in {Elapsed}; the rest only send "
          + "the reader to another headword or repeat one";
}

/// <summary>
/// Dillmann's Lexicon Linguae Aethiopicae, keyed by the consonants of each spelling it files an entry
/// under. Nothing is attached to a word here: a Ge'ez word reaches an entry when it is read.
/// </summary>
internal sealed class GeezLexiconLoader(AppDbContext db, ILogger<GeezLexiconLoader> logger)
{
    private const string Import =
        """
        COPY geez_lexicon_entry (entry, headword, forms, consonants, latin, greek, source) FROM STDIN (FORMAT BINARY)
        """;

    public async Task<GeezLexiconOutcome> Load(string folder, CancellationToken cancellationToken = default)
    {
        if (await db.GeezLexiconEntries.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Dillmann's lexicon is already loaded; nothing to do");
            return new GeezLexiconOutcome(true, false, 0, 0, 0, TimeSpan.Zero);
        }

        if (!Directory.Exists(folder))
        {
            var missing = new GeezLexiconOutcome(false, true, 0, 0, 0, TimeSpan.Zero);
            logger.LogWarning("{Outcome}", missing);
            return missing;
        }

        var started = Stopwatch.StartNew();
        var entries = DillmannLexicon.Read(folder).ToList();
        var headwords = DillmannLexicon.Headwords(entries);

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using (var writer = await connection.BeginBinaryImportAsync(Import, cancellationToken))
        {
            foreach (var headword in headwords)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(headword.Entry, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(headword.Headword, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(headword.Forms.ToArray(), NpgsqlDbType.Array | NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(
                    headword.Forms.Select(GeezLexicon.ConsonantsOf).Distinct().ToArray(),
                    NpgsqlDbType.Array | NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(headword.Latin.ToArray(), NpgsqlDbType.Array | NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(headword.Greek.ToArray(), NpgsqlDbType.Array | NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(Sources.DillmannLexicon, NpgsqlDbType.Text, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        var outcome = new GeezLexiconOutcome(
            false, false, entries.Count, headwords.Count, headwords.Sum(headword => headword.Forms.Count), started.Elapsed);
        logger.LogInformation("Loaded {Outcome}", outcome);
        return outcome;
    }
}
