using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// The steps every annotation goes through once something has decided which Hebrew word names whom:
/// carry the answer along the links into the other texts, write the row, and write the claim that
/// says what asserted it.
///
/// Written once because the three things that produce annotations — a Strong number that resolves,
/// a model that read the verse, and a person who ruled — differ only in what they put in the seed.
/// The carrying rule is the same for all of them and has to stay the same: one hop, always from the
/// Hebrew, never onto a word two Hebrew words disagree about, and the link's own confidence
/// multiplied into the annotation's so that a word reached by a guess is never stored as firmly as
/// one reached by a source's own mapping.
/// </summary>
internal static class Annotating
{
    /// <summary>
    /// The seed a caller fills and everything below reads.
    ///
    /// <c>confidence</c> is null exactly where a person or a source settled it, which is the same
    /// rule the tables themselves enforce. <c>corroborated</c> is whether the encyclopedia's own
    /// list of verses independently names that entity in this word's verse.
    ///
    /// <para>
    /// It drops itself at commit. A temporary table outlives its transaction and belongs to the
    /// connection, and connections here are pooled — so without this, two loaders that both write
    /// annotations in one process fail on the second one with <em>relation already exists</em>,
    /// which is a start-up crash a long way from its cause.
    /// </para>
    /// </summary>
    public const string Workspace =
        """
        CREATE TEMP TABLE pending_annotation (
            word_id bigint PRIMARY KEY,
            entity_id integer NOT NULL,
            confidence double precision,
            corroborated boolean NOT NULL,
            note text NOT NULL)
        ON COMMIT DROP
        """;

    /// <summary>
    /// The same annotations on every word the links say stands for one of the seeded words.
    ///
    /// A word reached from two seeded words that name two different entities is left alone: the
    /// links disagree about who is named, and picking between them is the judgement none of these
    /// loaders makes. Where they agree, the strongest link decides, because being reached twice is
    /// not weaker than being reached once.
    ///
    /// <para>
    /// A seed a person settled carries no confidence, and crossing a link that is itself certain
    /// leaves it that way — the person decided who is named, and a mapping the translators state
    /// does not make that less true. Crossing a link that is not certain does add a number, because
    /// then the annotation is only as sure as the correspondence it travelled along.
    /// </para>
    /// </summary>
    public const string Carry =
        """
        WITH reached AS (
            SELECT other.word_id,
                   seed.entity_id,
                   CASE WHEN seed.confidence IS NULL
                        THEN nullif(coalesce(l.confidence, 1.0), 1.0)
                        ELSE seed.confidence * coalesce(l.confidence, 1.0) END AS confidence,
                   l.method,
                   seed.word_id AS through
            FROM pending_annotation seed
            JOIN link_word mine ON mine.word_id = seed.word_id
            JOIN link l ON l.id = mine.link_id
            JOIN link_word other ON other.link_id = mine.link_id AND other.side <> mine.side
        ),
        unanimous AS (
            SELECT word_id FROM reached GROUP BY 1 HAVING count(DISTINCT entity_id) = 1
        ),
        strongest AS (
            SELECT DISTINCT ON (r.word_id) r.*
            FROM reached r JOIN unanimous u ON u.word_id = r.word_id
            ORDER BY r.word_id, coalesce(r.confidence, 1.0) DESC, r.through
        )
        INSERT INTO pending_annotation (word_id, entity_id, confidence, corroborated, note)
        SELECT s.word_id, s.entity_id, s.confidence, agreed.named,
               'through ' || @witness || ' word ' || s.through || ', linked by ' || s.method
        FROM strongest s
        JOIN word w ON w.id = s.word_id
        CROSS JOIN LATERAL (SELECT EXISTS (
            SELECT 1 FROM verse_reference r
            JOIN entity_verse ev ON ev.entity_id = s.entity_id
                 AND ev.canonical_book = r.canonical_book
                 AND ev.canonical_chapter = r.canonical_chapter
                 AND ev.canonical_verse = r.canonical_verse
            WHERE r.verse_id = w.verse_id AND r.is_primary) AS named) agreed
        ON CONFLICT (word_id) DO NOTHING
        """;

    /// <summary>
    /// The conclusion. A word that already names this entity keeps the row it has: another method
    /// arriving at the same answer is corroboration, and it belongs in the claims rather than in a
    /// second conclusion saying the same thing twice.
    /// </summary>
    public const string Settle =
        """
        INSERT INTO word_entity (word_id, entity_id, method, confidence, source, note)
        SELECT a.word_id, a.entity_id, @method, a.confidence, @source, a.note
        FROM pending_annotation a
        ON CONFLICT (word_id, entity_id) DO NOTHING
        """;

    /// <summary>
    /// What asserted it, written in the same transaction as the conclusion. An annotation nothing
    /// claims is invisible to every measure of agreement, which is the failure the link claims were
    /// already caught by once — and here it would also be an annotation with no model, no prompt
    /// version and no date, which is the one thing a reading must never be stored without.
    ///
    /// <para>
    /// It joins on the conclusion rather than being written beside it, so a reading that agreed with
    /// an annotation already there lands as a second claim on the existing row instead of being
    /// dropped by the conflict clause above.
    /// </para>
    /// </summary>
    public const string Claim =
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT a.id, @method, w.confidence, @source, w.note
        FROM word_entity a
        JOIN pending_annotation w ON w.word_id = a.word_id AND w.entity_id = a.entity_id
        ON CONFLICT DO NOTHING
        """;

    /// <summary>
    /// The verse list's own claim, wherever it independently names the same entity in the same
    /// verse.
    ///
    /// What it records is exactly what the source states — that the entity is named somewhere in
    /// this verse — so it is testimony and carries no confidence. Reaching from there to the word
    /// is the step the annotation's own number is for, and that number is deliberately not raised
    /// by agreement here: the list is known to put verses on the wrong man, so a reading agreeing
    /// with a wrong entry is a shared error rather than a better answer.
    /// </summary>
    public const string Corroboration =
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT a.id, @method, NULL, @source, @note
        FROM word_entity a
        JOIN pending_annotation w ON w.word_id = a.word_id AND w.entity_id = a.entity_id
        WHERE w.corroborated
        ON CONFLICT DO NOTHING
        """;

    /// <summary>
    /// Long enough for a pass over four and a half million words and their links, which is what the
    /// carrying step is. A start-up pass that throws does not fail its own step — it fails every
    /// step after it.
    /// </summary>
    public const int Patient = 1800;

    /// <summary>
    /// The seed, sent as a binary copy rather than as thousands of parameterised inserts. Ten
    /// thousand round trips is a minute of start-up on a corpus that already takes long enough.
    /// </summary>
    public static async Task Seed(
        NpgsqlConnection connection,
        IEnumerable<(long WordId, int EntityId, double? Confidence, bool Corroborated, string Note)> rows,
        CancellationToken cancellationToken)
    {
        await using var writer = await connection.BeginBinaryImportAsync(
            "COPY pending_annotation (word_id, entity_id, confidence, corroborated, note) FROM STDIN (FORMAT BINARY)",
            cancellationToken);

        foreach (var (wordId, entityId, confidence, corroborated, note) in rows)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(wordId, NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(entityId, NpgsqlDbType.Integer, cancellationToken);
            if (confidence is { } sure)
            {
                await writer.WriteAsync(sure, NpgsqlDbType.Double, cancellationToken);
            }
            else
            {
                await writer.WriteNullAsync(cancellationToken);
            }

            await writer.WriteAsync(corroborated, NpgsqlDbType.Boolean, cancellationToken);
            await writer.WriteAsync(note, NpgsqlDbType.Text, cancellationToken);
        }

        await writer.CompleteAsync(cancellationToken);
    }

    /// <summary>
    /// Whether the encyclopedia's own verse list names each seeded entity in each seeded word's
    /// verse, asked of the whole seed at once rather than per word.
    /// </summary>
    public const string MarkCorroboration =
        """
        UPDATE pending_annotation a SET corroborated = TRUE
        FROM word w, verse_reference r, entity_verse ev
        WHERE w.id = a.word_id
          AND r.verse_id = w.verse_id AND r.is_primary
          AND ev.entity_id = a.entity_id
          AND ev.canonical_book = r.canonical_book
          AND ev.canonical_chapter = r.canonical_chapter
          AND ev.canonical_verse = r.canonical_verse
        """;

    public static async Task Run(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(
            sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.CommandTimeout = Patient;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// What each text ended up with, counted by source rather than by method: two things here write
    /// annotations a person settled, and a total that mixed them would credit one with the other's
    /// work.
    /// </summary>
    public static async Task<IReadOnlyList<(string Text, int Words)>> ByText(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string source,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT t.slug, count(*) FROM word_entity a JOIN word w ON w.id = a.word_id " +
            "JOIN text t ON t.id = w.text_id WHERE a.source = @source GROUP BY 1 ORDER BY 2 DESC",
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("source", source);
        command.CommandTimeout = Patient;

        var counts = new List<(string, int)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts.Add((reader.GetString(0), (int)reader.GetInt64(1)));
        }

        return counts;
    }
}
