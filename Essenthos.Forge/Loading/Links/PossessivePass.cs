using System.Diagnostics;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The second word a Slavic translation spends on a Hebrew word with a pronominal suffix. בְּנוֹ is
/// "his son"; the Synodal writes <i>сына своего</i>, and an aligner that picks one target per Hebrew
/// word links <i>сына</i> and leaves <i>своего</i> reaching nothing.
///
/// <para>
/// A rule, not an alignment, and written as one: a possessive (or the genitive pronoun that serves
/// as one) that stands next to a word already linked to a suffixed Hebrew word, agrees with the
/// suffix's person and number (and gender, where the pronoun has one), and reaches nothing in that
/// Hebrew text itself, is linked to the same Hebrew word under its own source. The reflexive свой /
/// свій agrees with every person. The word before the possessive is preferred to the word after
/// it, which is the order <i>сына своего</i> and <i>его сына</i> both come in once the noun is
/// found; a possessive with a linked neighbour on both sides takes the one before.
/// </para>
/// </summary>
internal sealed class PossessivePass(AppDbContext db, ILogger<PossessivePass> logger)
{
    public const string Source =
        "the possessive beside a word linked to a Hebrew word with a pronominal suffix, agreeing with the suffix";

    /// <summary>
    /// What the rule is worth, written as the confidence of its links: agreement and adjacency are
    /// strong evidence, and a possessive can still belong to the neighbouring word's own suffix-less
    /// phrase, which a measure against a stated mapping would put a number on.
    /// </summary>
    private const double Confidence = 0.8;

    /// <summary>Person, number, and gender or null for any, of each form.</summary>
    private static readonly IReadOnlyList<(string Form, string? Person, string? Number, string? Gender)> Forms = BuildForms();

    public async Task<string> Run(string fromSlug, string toSlug, bool apply, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var from = await db.Texts.SingleAsync(t => t.Slug == fromSlug, cancellationToken);
        var to = await db.Texts.SingleAsync(t => t.Slug == toSlug, cancellationToken);

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var forms = new NpgsqlCommand(
                         "CREATE TEMP TABLE possessive_form (form text, person text, number text, gender text) ON COMMIT DROP",
                         connection, transaction))
        {
            await forms.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
                         "COPY possessive_form (form, person, number, gender) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var (form, person, number, gender) in Forms)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(form, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync((object?)person ?? DBNull.Value, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync((object?)number ?? DBNull.Value, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync((object?)gender ?? DBNull.Value, NpgsqlDbType.Text, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var candidates = new NpgsqlCommand(Candidates, connection, transaction))
        {
            candidates.Parameters.AddWithValue("from", from.Id);
            candidates.Parameters.AddWithValue("to", to.Id);
            candidates.Parameters.AddWithValue("source", Source);
            await candidates.ExecuteNonQueryAsync(cancellationToken);
        }

        var found = await Scalar(connection, transaction, "SELECT count(*) FROM possessive", cancellationToken);
        var suffixed = await Scalar(connection, transaction, "SELECT count(DISTINCT hebrew) FROM possessive", cancellationToken);
        var examples = new List<string>();
        await using (var sample = new NpgsqlCommand(Examples, connection, transaction))
        await using (var reader = await sample.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                examples.Add($"  {reader.GetString(0)}: {reader.GetString(1)} {reader.GetString(2)} → {reader.GetString(3)}");
            }
        }

        if (apply)
        {
            await using var write = new NpgsqlCommand(Write, connection, transaction);
            write.Parameters.AddWithValue("from", from.Id);
            write.Parameters.AddWithValue("to", to.Id);
            write.Parameters.AddWithValue(
                "provenance", await ProvenanceIds.Of(connection, transaction, Source, null, cancellationToken));
            write.Parameters.AddWithValue("confidence", Confidence);
            await write.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Read the possessives of {From} against {To} in {Elapsed}", fromSlug, toSlug, started.Elapsed);

        return $"{fromSlug} to {toSlug}: {found:N0} possessives reaching nothing stand beside a word linked to a suffixed word they agree with, "
               + $"over {suffixed:N0} suffixed words; {(apply ? "linked" : "nothing written without --apply")}\n"
               + string.Join('\n', examples);
    }

    private static async Task<long> Scalar(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>
    /// One row per possessive: the Hebrew word its linked neighbour renders, the neighbour before it
    /// preferred. Words that already reach the Hebrew text, or that this rule already linked, are
    /// left alone, which makes a second run write nothing.
    /// </summary>
    private const string Candidates =
        """
        CREATE TEMP TABLE possessive ON COMMIT DROP AS
        WITH linked AS (
            SELECT DISTINCT here.word_id AS word, there.word_id AS hebrew
            FROM link l
            JOIN link_word here ON here.link_id = l.id AND here.side = 'from'
            JOIN link_word there ON there.link_id = l.id AND there.side = 'to'
            WHERE l.from_text_id = @from AND l.to_text_id = @to AND l.relation IN ('renders', 'equals')
        ),
        reaching AS (
            SELECT DISTINCT here.word_id AS word
            FROM link l
            JOIN link_word here ON here.link_id = l.id AND here.side = 'from'
            WHERE l.from_text_id = @from AND l.to_text_id = @to
        ),
        suffixed AS (
            SELECT w.id, w.morphology ->> 'suffixPerson' AS person, w.morphology ->> 'suffixNumber' AS number,
                   w.morphology ->> 'suffixGender' AS gender
            FROM word w
            WHERE w.text_id = @to AND w.morphology ? 'suffixPerson'
        ),
        candidate AS (
            SELECT p.id AS word, s.id AS hebrew, abs(p.position - n.position) AS distance,
                   CASE WHEN n.position < p.position THEN 0 ELSE 1 END AS side
            FROM word p
            JOIN possessive_form f ON f.form = lower(p.text)
            JOIN word n ON n.verse_id = p.verse_id AND n.position IN (p.position - 1, p.position + 1)
            JOIN linked k ON k.word = n.id
            JOIN suffixed s ON s.id = k.hebrew
            WHERE p.text_id = @from
              AND (f.person IS NULL OR f.person = s.person)
              AND (f.number IS NULL OR f.number = s.number)
              AND (f.gender IS NULL OR f.gender = s.gender)
              AND NOT EXISTS (SELECT 1 FROM reaching r WHERE r.word = p.id)
        )
        SELECT DISTINCT ON (word) word, hebrew
        FROM candidate
        ORDER BY word, side, hebrew
        """;

    private const string Examples =
        """
        SELECT b.name || ' ' || v.chapter_number || ':' || v.number, n.text, p.text, h.text
        FROM possessive x
        JOIN word p ON p.id = x.word
        JOIN verse v ON v.id = p.verse_id
        JOIN book b ON b.id = v.book_id
        JOIN word h ON h.id = x.hebrew
        LEFT JOIN LATERAL (
            SELECT string_agg(o.text, ' ' ORDER BY o.position) AS text FROM word o
            WHERE o.verse_id = p.verse_id AND o.position IN (p.position - 1)
        ) n ON TRUE
        ORDER BY p.id
        LIMIT 8
        """;

    private const string Write =
        """
        WITH numbered AS (
            SELECT word, hebrew, nextval(pg_get_serial_sequence('link', 'id')) AS id FROM possessive
        ),
        links AS (
            INSERT INTO link (id, from_text_id, to_text_id, relation, method, confidence, provenance_id, fingerprint)
            SELECT id, @from, @to, 'renders', 'rule-based', @confidence, @provenance,
                   md5('f' || word || ' t' || hebrew)::uuid
            FROM numbered
            RETURNING id
        ),
        claims AS (
            INSERT INTO link_claim (link_id, method, confidence, provenance_id)
            SELECT id, 'rule-based', @confidence, @provenance FROM numbered
        ),
        froms AS (
            INSERT INTO link_word (link_id, word_id, side) SELECT id, word, 'from' FROM numbered
        )
        INSERT INTO link_word (link_id, word_id, side) SELECT id, hebrew, 'to' FROM numbered
        """;

    private static List<(string, string?, string?, string?)> BuildForms()
    {
        var forms = new List<(string, string?, string?, string?)>();

        void Add(string? person, string? number, string? gender, params string[] words) =>
            forms.AddRange(words.Select(word => (word, person, number, gender)));

        string[] Russian(string stem) =>
            [stem + "й", stem + "я", stem + "ё", stem + "е", stem + "его", stem + "ей", stem + "ему", stem + "им",
             stem + "ем", stem + "ём", stem + "и", stem + "их", stem + "ими", stem + "ю"];

        string[] RussianHard(string stem) =>
            [stem, stem + "а", stem + "е", stem + "его", stem + "ей", stem + "ему", stem + "им", stem + "ем",
             stem + "и", stem + "их", stem + "ими", stem + "у"];

        string[] Ukrainian(string nominative, string stem) =>
            [nominative, stem + "я", stem + "є", stem + "го", stem + "єї", stem + "єму", stem + "їм", stem + "їй",
             stem + "ї", stem + "їх", stem + "їми", stem + "ю"];

        string[] UkrainianHard(string stem) =>
            [stem, stem + "а", stem + "е", stem + "ого", stem + "ої", stem + "ому", stem + "им", stem + "ім",
             stem + "ій", stem + "і", stem + "их", stem + "ими", stem + "у"];

        Add(null, null, null, [.. Russian("сво"), .. Ukrainian("свій", "сво")]);
        Add("p1", "sg", null, [.. Russian("мо"), .. Ukrainian("мій", "мо")]);
        Add("p2", "sg", null, [.. Russian("тво"), .. Ukrainian("твій", "тво")]);
        Add("p1", "pl", null, [.. RussianHard("наш"), .. UkrainianHard("наш")]);
        Add("p2", "pl", null, [.. RussianHard("ваш"), .. UkrainianHard("ваш")]);
        Add("p3", "sg", "m", "его", "його");
        Add("p3", "sg", "f", "её", "ее", "її");
        Add("p3", "pl", null, "их", "їх", "їхній", "їхня", "їхнє", "їхнього", "їхньої", "їхньому", "їхнім",
            "їхні", "їхніх", "їхніми", "їхню");

        return [.. forms.DistinctBy(form => form.Item1)];
    }
}
