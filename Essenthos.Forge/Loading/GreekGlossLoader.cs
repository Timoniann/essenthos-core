using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.StepBible;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading;

/// <param name="Lemmatised">Brenton's words that carry a lemma, which is what a gloss by lemma can reach.</param>
/// <param name="Glossed">Of those, the ones whose lemma is a form the lexicon prints.</param>
internal sealed record GreekGlossOutcome(
    bool AlreadyLoaded,
    int Entries,
    int Forms,
    int Lemmatised,
    int Glossed,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the Greek glosses are already loaded"
            : $"{Entries} TBESG entries under {Forms} dictionary forms in {Elapsed}: {Glossed} of Brenton's " +
              $"{Lemmatised} lemmatised words find a gloss by their lemma";
}

/// <summary>
/// STEPBible's short Greek glosses, keyed by the dictionary form and the number each entry is filed
/// under. Nothing is attached to a word here: a word reaches a gloss when it is read, by its own
/// lemma or number, and the reading says which way it went.
/// </summary>
internal sealed class GreekGlossLoader(AppDbContext db, ILogger<GreekGlossLoader> logger)
{
    private const string Import =
        """
        COPY lexicon_gloss (entry, strong_number, lemma, gloss, source) FROM STDIN (FORMAT BINARY)
        """;

    /// <summary>
    /// Folded in Postgres rather than here, because the build runs with invariant globalization and
    /// <c>string.Normalize</c> under it returns its input unchanged. Every lemma the corpus holds is
    /// already NFC, so this is what makes an exact comparison against them mean something.
    /// </summary>
    private const string Compose = "UPDATE lexicon_gloss SET lemma = normalize(lemma, NFC) WHERE lemma <> normalize(lemma, NFC)";

    private const string Reach =
        """
        SELECT count(*), count(*) FILTER (WHERE EXISTS (SELECT 1 FROM lexicon_gloss g WHERE g.lemma = w.lemma))
        FROM word w JOIN text t ON t.id = w.text_id
        WHERE t.slug = @slug AND w.lemma IS NOT NULL
        """;

    public async Task<GreekGlossOutcome> Load(string path, CancellationToken cancellationToken = default)
    {
        if (await db.LexiconGlosses.AnyAsync(cancellationToken))
        {
            logger.LogInformation("The Greek glosses are already loaded; nothing to do");
            return new GreekGlossOutcome(true, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var entries = BriefGreekLexicon.Read(path).ToList();

        // An entry that prints the same form twice would break the unique index halfway through the
        // COPY, which is a worse place to find out.
        var rows = entries
            .SelectMany(entry => entry.Lemmas.Distinct(StringComparer.Ordinal).Select(lemma => (entry, lemma)))
            .ToList();

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var writer = await connection.BeginBinaryImportAsync(Import, cancellationToken))
        {
            foreach (var (entry, lemma) in rows)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(entry.Entry, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(entry.StrongNumber, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(lemma, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(entry.Gloss, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(Sources.BriefGreekLexicon, NpgsqlDbType.Text, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var compose = new NpgsqlCommand(Compose, connection, transaction))
        {
            await compose.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var (lemmatised, glossed) = await Measure(connection, cancellationToken);
        var outcome = new GreekGlossOutcome(false, entries.Count, rows.Count, lemmatised, glossed, started.Elapsed);
        logger.LogInformation("Loaded {Outcome}", outcome);
        return outcome;
    }

    private static async Task<(int Lemmatised, int Glossed)> Measure(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Reach, connection);
        command.Parameters.AddWithValue("slug", Sources.BrentonSeptuagintSlug);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ((int)reader.GetInt64(0), (int)reader.GetInt64(1));
    }
}
