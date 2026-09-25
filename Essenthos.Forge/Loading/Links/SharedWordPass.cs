using System.Diagnostics;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Two words of a translation that the aligner put on one Hebrew word, where one of them is a
/// pronoun or a particle standing apart from the other. Keeping each word's best answer stops a
/// word claiming two Hebrew words, not two words claiming one, and most of those pairs are right —
/// <i>по роду</i> for מִינֵהוּ, <i>семя его</i> for זַרְעוֹ, <i>Я дал</i> for נָתַתִּי — and every
/// one of the right ones is adjacent. What is left is a function word dumped on a content word
/// somewhere else in the verse: <i>она</i> on מַבְדִּיל, <i>да</i> on עוֹף.
///
/// <para>
/// A pronoun that agrees with the Hebrew word — its suffix, or the person of its verb — is kept
/// wherever it stands, since that is what a pronoun apart from its verb renders.
/// Only the aligner's own links are withdrawn: a link another method also claims, or one a
/// review has spoken about, is somebody's evidence and stays. A function word sharing a Hebrew
/// word with nothing but other function words is left as it is, since there is no content word to
/// say which of them is dumped.
/// </para>
/// </summary>
internal sealed class SharedWordPass(AppDbContext db, ILogger<SharedWordPass> logger)
{
    /// <summary>
    /// Pronouns, with the person and number they speak of, and particles, with neither or with any verb. A pronoun
    /// that agrees with the Hebrew word's suffix, or with the person its verb is inflected for, is
    /// rendering that suffix or that subject wherever it stands — <i>мясо я не ел</i> for אָכַלְתִּי —
    /// and is kept; one that agrees with nothing on the word it was put on, and a particle, is not.
    /// </summary>
    private static readonly (string Form, string? Person, string? Number)[] FunctionWords =
    [
        .. Words("p1", "sg", "я", "меня", "мне", "мене", "мені"),
        .. Words("p2", "sg", "ты", "тебя", "тебе", "ти", "тобі"),
        .. Words("p3", "sg", "он", "она", "оно", "его", "её", "ее", "ему", "ей", "він", "вона", "воно", "його", "її", "йому", "їй"),
        .. Words("p1", "pl", "мы", "нас", "нам", "ми"),
        .. Words("p2", "pl", "вы", "вас", "вам", "ви"),
        .. Words("p3", "pl", "они", "их", "вони", "їх", "їм"),
        .. Words("p3", null, "им"),
        .. Words(AnyVerb, null, "да", "бы", "хай", "нехай", "би"),
        .. Words(null, null, "есть", "же", "ли", "вот", "ведь", "это", "є", "ж", "чи", "ось", "це"),
    ];

    /// <summary>
    /// The person of a particle that marks a verb's mood — <i>да не будет</i> for יְהִי — and so
    /// agrees with any finite verb, wherever the negation puts it.
    /// </summary>
    private const string AnyVerb = "verb";

    private static IEnumerable<(string, string?, string?)> Words(string? person, string? number, params string[] forms) =>
        forms.Select(form => (form, person, number));

    public async Task<string> Run(string fromSlug, string toSlug, bool apply, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var from = await db.Texts.SingleAsync(t => t.Slug == fromSlug, cancellationToken);
        var to = await db.Texts.SingleAsync(t => t.Slug == toSlug, cancellationToken);

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var table = new NpgsqlCommand(
                         "CREATE TEMP TABLE function_word (form text, person text, number text) ON COMMIT DROP",
                         connection, transaction))
        {
            await table.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY function_word (form, person, number) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var (form, person, number) in FunctionWords)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(form, NpgsqlTypes.NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync((object?)person ?? DBNull.Value, NpgsqlTypes.NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync((object?)number ?? DBNull.Value, NpgsqlTypes.NpgsqlDbType.Text, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var find = new NpgsqlCommand(Dumped, connection, transaction) { CommandTimeout = 1800 })
        {
            find.Parameters.AddWithValue("from", from.Id);
            find.Parameters.AddWithValue("to", to.Id);
            find.Parameters.AddWithValue("verb", AnyVerb);
            await find.ExecuteNonQueryAsync(cancellationToken);
        }

        var forms = new List<string>();
        long found = 0;
        await using (var count = new NpgsqlCommand(
                         "SELECT form, count(*) FROM dumped GROUP BY form ORDER BY 2 DESC", connection, transaction))
        await using (var reader = await count.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                found += reader.GetInt64(1);
                forms.Add($"{reader.GetString(0)} {reader.GetInt64(1):N0}");
            }
        }

        if (apply)
        {
            await using var withdraw = new NpgsqlCommand(
                "DELETE FROM link WHERE id IN (SELECT link FROM dumped)", connection, transaction) { CommandTimeout = 1800 };
            await withdraw.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Read the shared words of {From} against {To} in {Elapsed}", fromSlug, toSlug, started.Elapsed);

        return $"{fromSlug} to {toSlug}: {found:N0} aligner links put a pronoun or particle on a Hebrew word it does not agree with "
               + $"and another word of the verse, not beside it, renders; {(apply ? "withdrawn" : "nothing withdrawn without --apply")}\n  "
               + string.Join(", ", forms);
    }

    private const string Dumped =
        """
        CREATE TEMP TABLE dumped ON COMMIT DROP AS
        WITH claimant AS (
            SELECT l.id AS link, t.word_id AS hebrew, w.verse_id, w.position, lower(w.text) AS form,
                   fw.form IS NOT NULL AS function,
                   coalesce(fw.person IS NOT NULL AND (
                       (fw.person = h.morphology ->> 'suffixPerson'
                        AND (fw.number IS NULL OR fw.number = h.morphology ->> 'suffixNumber'))
                       OR (fw.person = @verb AND h.morphology ? 'person')
                       OR (fw.person = h.morphology ->> 'person'
                           AND (fw.number IS NULL OR fw.number = h.morphology ->> 'number'))), false) AS agrees,
                   l.method = 'aligner'
                   AND (SELECT count(*) FROM link_word g WHERE g.link_id = l.id AND g.side = 'from') = 1
                   AND NOT EXISTS (SELECT 1 FROM link_claim c WHERE c.link_id = l.id AND c.method <> 'aligner')
                   AND NOT EXISTS (SELECT 1 FROM evidentia_review r WHERE r.link_id = l.id)
                   AND NOT EXISTS (SELECT 1 FROM evidentia_withdrawal r WHERE r.link_id = l.id) AS withdrawable
            FROM link l
            JOIN link_word f ON f.link_id = l.id AND f.side = 'from'
            JOIN link_word t ON t.link_id = l.id AND t.side = 'to'
            JOIN word w ON w.id = f.word_id
            JOIN word h ON h.id = t.word_id
            LEFT JOIN function_word fw ON fw.form = lower(w.text)
            WHERE l.from_text_id = @from AND l.to_text_id = @to AND l.relation IN ('renders', 'equals')
        )
        SELECT DISTINCT x.link, x.form
        FROM claimant x
        WHERE x.function AND NOT x.agrees AND x.withdrawable
          AND EXISTS (
              SELECT 1 FROM claimant y
              WHERE y.hebrew = x.hebrew AND y.verse_id = x.verse_id AND NOT y.function)
          AND NOT EXISTS (
              SELECT 1 FROM claimant y
              WHERE y.hebrew = x.hebrew AND y.verse_id = x.verse_id AND y.link <> x.link
                AND abs(y.position - x.position) = 1)
        """;
}
