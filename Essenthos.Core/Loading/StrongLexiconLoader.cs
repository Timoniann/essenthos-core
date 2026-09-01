using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Strong;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading;

/// <param name="Unresolved">
/// Words carrying a Strong number no entry answers, excluding the prefix morphemes ETCBC numbers in
/// the H9000 range, which Strong never catalogued. This is the number the feature exists to
/// produce: it is the cheapest measure of whether the Strong data is sound, and everything that
/// leans on Strong numbers leans on that.
/// </param>
internal sealed record LexiconOutcome(
    bool AlreadyLoaded,
    int Entries,
    int Unresolved,
    int Unused,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the Strong lexicon is already loaded"
            : $"{Entries} Strong entries in {Elapsed}: {Unresolved} words carry a number no entry answers, " +
              $"{Unused} entries no word in the corpus points at";
}

/// <summary>
/// Strong's concordance, which the corpus has been carrying numbers into since the first text
/// loaded and has never been able to resolve.
///
/// Loading it is small. The measurement it makes possible is not: until now a Strong number on a
/// word was a string nobody could check, and the corpus could not say whether its own tagging was
/// sound. It now can, on every load.
/// </summary>
internal sealed class StrongLexiconLoader(AppDbContext db, ILogger<StrongLexiconLoader> logger)
{
    private const string Import =
        """
        COPY strong_entry (strong_number, lemma, transliteration, pronunciation, definition, derivation,
                           kjv_definition, morphology, detailed_definition, see_also, source_language,
                           twot_reference)
        FROM STDIN (FORMAT BINARY)
        """;

    public async Task<LexiconOutcome> Load(
        string hebrewPath,
        string greekPath,
        CancellationToken cancellationToken = default)
    {
        if (await db.StrongEntries.AnyAsync(cancellationToken))
        {
            logger.LogInformation("The Strong lexicon is already loaded; nothing to do");
            return new LexiconOutcome(true, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var parser = new StrongXmlParser();
        var entries = new List<StrongParsedEntry>(14_000);
        entries.AddRange(parser.ParseHebrew(await File.ReadAllTextAsync(hebrewPath, cancellationToken)));
        entries.AddRange(parser.ParseGreek(await File.ReadAllTextAsync(greekPath, cancellationToken)));

        // The two files are separate concordances and cannot collide — one numbers in H, the other
        // in G — but a file that repeats an entry would break the unique index halfway through a
        // COPY, which is a worse way to find out.
        var byNumber = entries
            .GroupBy(entry => entry.StrongNumber)
            .ToDictionary(number => number.Key, number => number.First());

        await Write(byNumber.Values, cancellationToken);
        var (unresolved, unused) = await Coverage(cancellationToken);

        var outcome = new LexiconOutcome(false, byNumber.Count, unresolved, unused, started.Elapsed);
        logger.LogInformation("Loaded {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// What the lexicon and the corpus say about each other. Both directions matter and neither is
    /// an error on its own: a word with no entry may be a prefix morpheme, and an entry no word
    /// points at may simply be a lexeme these particular texts do not use.
    /// </summary>
    public async Task<(int Unresolved, int Unused)> Coverage(CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var unresolved = await Count(connection, cancellationToken,
            """
            SELECT count(*) FROM word w
            WHERE w.strong_number IS NOT NULL
              AND w.strong_number !~ '^H9[0-9]{3}$'
              AND NOT EXISTS (SELECT 1 FROM strong_entry e WHERE e.strong_number = w.strong_number)
            """);

        var unused = await Count(connection, cancellationToken,
            """
            SELECT count(*) FROM strong_entry e
            WHERE NOT EXISTS (SELECT 1 FROM word w WHERE w.strong_number = e.strong_number)
            """);

        return (unresolved, unused);
    }

    private static async Task<int> Count(
        NpgsqlConnection connection,
        CancellationToken cancellationToken,
        string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task Write(IEnumerable<StrongParsedEntry> entries, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var writer = await connection.BeginBinaryImportAsync(Import, cancellationToken);
        foreach (var entry in entries)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(entry.StrongNumber, NpgsqlDbType.Text, cancellationToken);
            await Optional(writer, entry.Lemma, cancellationToken);
            await Optional(writer, entry.Transliteration, cancellationToken);
            await Optional(writer, entry.Pronunciation, cancellationToken);
            await Optional(writer, entry.Definition, cancellationToken);
            await Optional(writer, entry.Derivation, cancellationToken);
            await Optional(writer, entry.KjvDefinition, cancellationToken);
            await Optional(writer, entry.Morphology, cancellationToken);
            await Optional(writer, entry.DetailedDefinition, cancellationToken);
            await Optional(writer, entry.SeeAlso, cancellationToken);
            await Optional(writer, entry.SourceLanguage, cancellationToken);
            await Optional(writer, entry.TwotReference, cancellationToken);
        }

        await writer.CompleteAsync(cancellationToken);
    }

    private static async Task Optional(
        NpgsqlBinaryImporter writer,
        string? value,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            await writer.WriteNullAsync(cancellationToken);
            return;
        }

        await writer.WriteAsync(value, NpgsqlDbType.Text, cancellationToken);
    }
}
