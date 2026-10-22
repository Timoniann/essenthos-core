using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading;

/// <param name="Forms">Forms the texts print, across every text.</param>
/// <param name="Written">Rows added or changed.</param>
/// <param name="Removed">Rows of forms no text prints any longer.</param>
internal sealed record SearchFormOutcome(int Forms, int Written, int Removed, TimeSpan Elapsed)
{
    public override string ToString() =>
        $"{Forms} word forms counted with their stems in {Elapsed}: {Written} written, {Removed} gone.";
}

/// <summary>
/// Every word form of every text with its stem and its count, which the verse search matches a typed
/// word through (<see cref="Database.Entities.TextForm"/>).
///
/// <para>
/// A projection of the words, counted whole and written where it changed, in one transaction. The stem
/// is Postgres's own Snowball stem for a language it has a dictionary for, so the search reads a typed
/// word with the same function; a Ukrainian form is stemmed here by the same code the search calls
/// (<see cref="SearchWords"/>); any other form is its own stem.
/// </para>
/// </summary>
internal sealed class SearchFormLoader(AppDbContext db, ILogger<SearchFormLoader> logger)
{
    private const string Count =
        """
        CREATE TEMP TABLE counted ON COMMIT DROP AS
        SELECT w.text_id, w.normalised_text AS form, count(*)::int AS occurrences, w.normalised_text AS stem
        FROM word w
        WHERE w.normalised_text IS NOT NULL AND w.normalised_text <> ''
        GROUP BY w.text_id, w.normalised_text;
        CREATE INDEX ON counted (text_id, form);
        ANALYZE counted;
        """;

    private const string Stemmed =
        """
        UPDATE counted c
        SET stem = coalesce(nullif((ts_lexize(@dictionary::regdictionary, c.form))[1], ''), c.form)
        FROM text t
        WHERE t.id = c.text_id AND t.language = @language
        """;

    private const string Write =
        """
        WITH gone AS (
            DELETE FROM text_form f
            WHERE NOT EXISTS (SELECT 1 FROM counted c WHERE c.text_id = f.text_id AND c.form = f.form)
            RETURNING 1
        )
        SELECT count(*)::int FROM gone;
        """;

    private const string Upsert =
        """
        INSERT INTO text_form (text_id, form, stem, occurrences)
        SELECT text_id, form, stem, occurrences FROM counted
        ON CONFLICT (text_id, form) DO UPDATE SET stem = excluded.stem, occurrences = excluded.occurrences
        WHERE (text_form.stem, text_form.occurrences) IS DISTINCT FROM (excluded.stem, excluded.occurrences)
        """;

    public async Task<SearchFormOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var languages = await db.Texts.Select(t => t.Language).Distinct().ToListAsync(cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Execute(connection, Count, cancellationToken);

        foreach (var language in languages)
        {
            if (SearchWords.Dictionary(language) is { } dictionary)
            {
                await using var command = new NpgsqlCommand(Stemmed, connection);
                command.Parameters.AddWithValue("dictionary", dictionary);
                command.Parameters.AddWithValue("language", language);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            else if (SearchWords.StemmedHere(language))
            {
                await StemHere(connection, language, cancellationToken);
            }
        }

        var removed = await Scalar(connection, Write, cancellationToken);
        int written;
        await using (var upsert = new NpgsqlCommand(Upsert, connection))
        {
            written = await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        var forms = await Scalar(connection, "SELECT count(*)::int FROM counted", cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new SearchFormOutcome(forms, written, removed, started.Elapsed);
        logger.LogInformation("Counted the word forms a search matches: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>The forms of a language's texts stemmed by <see cref="SearchWords.StemHere"/>.</summary>
    private static async Task StemHere(NpgsqlConnection connection, string language, CancellationToken cancellationToken)
    {
        var forms = new List<(int Text, string Form)>();
        await using (var read = new NpgsqlCommand(
                         "SELECT c.text_id, c.form FROM counted c JOIN text t ON t.id = c.text_id WHERE t.language = @language",
                         connection))
        {
            read.Parameters.AddWithValue("language", language);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                forms.Add((reader.GetInt32(0), reader.GetString(1)));
            }
        }

        await Execute(connection, "CREATE TEMP TABLE stemmed (text_id int, form text, stem text) ON COMMIT DROP", cancellationToken);
        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY stemmed (text_id, form, stem) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var (text, form) in forms)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(text, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(form, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(SearchWords.StemHere(form, language) ?? form, NpgsqlDbType.Text, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await Execute(
            connection,
            "UPDATE counted c SET stem = s.stem FROM stemmed s WHERE s.text_id = c.text_id AND s.form = c.form; DROP TABLE stemmed;",
            cancellationToken);
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> Scalar(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
