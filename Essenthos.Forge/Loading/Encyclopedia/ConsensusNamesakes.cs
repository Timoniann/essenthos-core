using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Given">Words given the namesake the verse names instead of the one the consensus chose.</param>
/// <param name="Withdrawn">Words taken back where the verse names several namesakes and none of them is the one chosen.</param>
/// <param name="ByText">How many words each text had of either.</param>
internal sealed record ConsensusNamesakeOutcome(
    int Given,
    int Withdrawn,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        Given + Withdrawn == 0
            ? "every name the verses' consensus wrote is the bearer its verse names"
            : $"{Given} names the verses' consensus wrote given to the namesake the verse names instead, and " +
              $"{Withdrawn} taken back where it names several, in {Elapsed}. Per text: " +
              string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}"));
}

/// <summary>
/// A name the verses' consensus wrote on the right word for the wrong man.
///
/// <para>
/// The consensus reads a text's spelling of a name off the verses a record is named in, and writes
/// the record on the words of those verses that spell it. Where two men share the name, the spelling
/// is both men's and the verse list decides which, and a list can be wrong: the Douay, the Vulgate
/// and the Hindi named King Manasseh at Revelation 7:6, because the Greek resolution had once put
/// the king's number there, and the consensus kept what it read after the Greek was corrected.
/// </para>
///
/// <para>
/// <strong>The verse's own readings say which bearer it is.</strong> Where a word was given a
/// person by the consensus, and every direct reading of the verse — in any text, an answer read of
/// a word rather than carried to it or inferred by this same consensus — names another man of the
/// same name and none names this one, the word is that other man's: the spelling says the name and
/// the verse says the man. Where the verse names two other men of the name, it is taken back
/// without an answer. A title and its bearer are one answer and never each other's namesakes.
/// </para>
///
/// <para>
/// After the consensus, and idempotent: what it writes is no longer the consensus's.
/// </para>
/// </summary>
internal sealed class ConsensusNamesakes(AppDbContext db, ILogger<ConsensusNamesakes> logger)
{
    public const string Source =
        "Essenthos, the name the verses share in this text, given to the bearer of that name the verse's " +
        "own readings name";

    private const string Find =
        """
        CREATE TEMP TABLE name_key ON COMMIT DROP AS
        SELECT DISTINCT e.id AS entity_id, lower(n.label) AS k
        FROM entity e JOIN entity_name n ON n.entity_id = e.id
        WHERE coalesce(n.kind, '') NOT IN ('title', 'description')
        UNION SELECT id, lower(name) FROM entity;
        CREATE INDEX ON name_key (entity_id);
        CREATE INDEX ON name_key (k);
        CREATE TEMP TABLE consensus ON COMMIT DROP AS
        SELECT a.id, a.word_id, a.entity_id, a.confidence, t.slug, r.canonical_book AS b,
               r.canonical_chapter AS c, r.canonical_verse AS v
        FROM word_entity a
        JOIN entity named ON named.id = a.entity_id AND named.kind = 'person'
        JOIN word w ON w.id = a.word_id
        JOIN text t ON t.id = w.text_id
        JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
        WHERE a.source = @consensus AND coalesce(a.note, '') NOT LIKE @carried;
        CREATE INDEX ON consensus (b, c, v);
        CREATE TEMP TABLE read_here ON COMMIT DROP AS
        SELECT DISTINCT r.canonical_book AS b, r.canonical_chapter AS c, r.canonical_verse AS v, a.entity_id
        FROM (SELECT DISTINCT b, c, v FROM consensus) at_verse
        JOIN verse_reference r ON r.canonical_book = at_verse.b AND r.canonical_chapter = at_verse.c
             AND r.canonical_verse = at_verse.v AND r.is_primary
        JOIN word w ON w.verse_id = r.verse_id
        JOIN word_entity a ON a.word_id = w.id
        WHERE a.source NOT IN (@consensus, @source) AND coalesce(a.note, '') NOT LIKE @carried;
        CREATE INDEX ON read_here (b, c, v);
        CREATE TEMP TABLE misread ON COMMIT DROP AS
        SELECT c.*, namesakes.ids
        FROM consensus c
        CROSS JOIN LATERAL (
            SELECT array_agg(DISTINCT d.entity_id) AS ids
            FROM read_here d
            JOIN entity other ON other.id = d.entity_id AND other.kind = 'person'
            WHERE d.b = c.b AND d.c = c.c AND d.v = c.v AND d.entity_id <> c.entity_id
              AND EXISTS (SELECT 1 FROM name_key mine JOIN name_key theirs ON theirs.k = mine.k
                          WHERE mine.entity_id = c.entity_id AND theirs.entity_id = d.entity_id)
              AND NOT EXISTS (SELECT 1 FROM title_bearer borne
                              WHERE (borne.title_entity_id = c.entity_id AND borne.bearer_entity_id = d.entity_id)
                                 OR (borne.title_entity_id = d.entity_id AND borne.bearer_entity_id = c.entity_id))) namesakes
        WHERE namesakes.ids IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM read_here d
                          WHERE d.b = c.b AND d.c = c.c AND d.v = c.v AND d.entity_id = c.entity_id)
        """;

    private const string Write =
        """
        INSERT INTO word_entity (word_id, entity_id, method, confidence, source, note)
        SELECT m.word_id, m.ids[1], @method, m.confidence, @source,
               'the word its verses share in this text, and the verse''s own readings name ' || e.name
        FROM misread m JOIN entity e ON e.id = m.ids[1]
        WHERE cardinality(m.ids) = 1
        ON CONFLICT (word_id, entity_id) DO NOTHING
        """;

    private const string Claim =
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT a.id, a.method, a.confidence, a.source, a.note
        FROM misread m
        JOIN word_entity a ON a.word_id = m.word_id AND a.entity_id = m.ids[1] AND a.source = @source
        WHERE cardinality(m.ids) = 1
        ON CONFLICT DO NOTHING
        """;

    public async Task<ConsensusNamesakeOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var parameters = new (string, object)[]
        {
            ("consensus", NameConsensusPass.Source), ("source", Source), ("carried", Annotating.CarriedNote),
            ("method", EnumSpelling.Of(LinkMethod.RuleBased)),
        };

        await Execute(connection, transaction, Find, parameters, cancellationToken);
        await Execute(connection, transaction, Write, parameters, cancellationToken);
        await Execute(connection, transaction, Claim, parameters, cancellationToken);

        int given = 0, withdrawn = 0;
        var byText = new List<(string, int)>();
        await using (var count = new NpgsqlCommand(
                         """
                         SELECT slug, count(*), count(*) FILTER (WHERE cardinality(ids) = 1)
                         FROM misread GROUP BY 1 ORDER BY 2 DESC, 1
                         """, connection, transaction))
        await using (var reader = await count.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                byText.Add((reader.GetString(0), (int)reader.GetInt64(1)));
                given += (int)reader.GetInt64(2);
                withdrawn += (int)(reader.GetInt64(1) - reader.GetInt64(2));
            }
        }

        await Execute(connection, transaction, "DELETE FROM word_entity a USING misread m WHERE a.id = m.id",
            parameters, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new ConsensusNamesakeOutcome(given, withdrawn, byText, started.Elapsed);
        logger.LogInformation("{Outcome}", outcome);
        return outcome;
    }

    private static async Task Execute(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        (string Name, object Value)[] parameters,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters.Where(p => sql.Contains('@' + p.Name, StringComparison.Ordinal)))
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
