using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Sources">How many sources' annotations were carried again, each on its own.</param>
/// <param name="Seeds">The annotations the carrying starts from: every row no link produced.</param>
/// <param name="Kept">Carried rows the current links reproduce exactly, which are left untouched.</param>
/// <param name="Withdrawn">
/// Carried rows the current links no longer reproduce — the link is gone, it now loses to a firmer
/// one, or it is worth a different number — and which are therefore taken back.
/// </param>
/// <param name="Written">Rows the current links reach that nothing had written.</param>
/// <param name="ByText">Words carrying any annotation in each text, before and after.</param>
internal sealed record CarryOutcome(
    int Sources,
    int Seeds,
    int Kept,
    int Withdrawn,
    int Written,
    IReadOnlyList<(string Text, int Before, int After)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        $"{Sources} sources carried again from {Seeds} seeds in {Elapsed}: {Kept} carried " +
        $"annotations the links still reproduce, {Withdrawn} withdrawn and {Written} written. " +
        "Words naming somebody, per text: " +
        string.Join(", ", ByText
            .Where(t => t.Before != t.After)
            .Select(t => $"{t.Text} {t.Before} -> {t.After}"));
}

/// <summary>
/// The annotations the links carried into other texts, carried again over the links as they stand.
///
/// <para>
/// Every pass that names a word — a resolution, a reading, a ruling, a people — writes its answer on
/// a witness word and carries it one hop along the links that exist at that moment. The links do
/// not stay as they were. A mapping loaded afterwards adds hundreds of thousands of them and removes
/// the aligner's guesses it contradicts, and nothing a pass already carried hears about either: the
/// Synodal gained 650,049 numbered links and kept every annotation its removed guesses had put on
/// the wrong word, while the right word the new link reaches stayed blank. <em>Хиддекель</em> was
/// unnamed and the pronoun beside it named the Tigris.
/// </para>
///
/// <para>
/// <strong>What is carried again is exactly what was carried.</strong> The seeds are the rows no
/// link produced, read back out of <c>word_entity</c>, and they go through
/// <see cref="Annotating.Carry"/> — the one rule every pass carries by — one source at a time,
/// because a source is the batch a pass wrote and unanimity is asked within it. The name resolution
/// is the exception that proves the grain: its four source strings were one batch, and they are
/// carried as one.
/// </para>
///
/// <para>
/// <strong>A carried row is its seed crossed with a link, and nothing else.</strong> The confidence
/// is the seed's multiplied by the link's; the claims are the seed's claims crossed the same way;
/// a source's own testimony about the verse stays testimony. The one thing re-asked of the word it
/// lands on is the resolution's: whether that word's own number is several records', which is what
/// decides between <c>strong-number</c> and the form of the word.
/// </para>
///
/// <para>
/// <strong>What the links still reproduce is not touched.</strong> A row survives where the carry
/// arrives at the same word, the same entity, the same method and the same confidence, so a text
/// whose links did not change comes out as it went in, row for row and id for id. Everything else is
/// withdrawn and written again, and the difference is the whole of what the new links changed.
/// </para>
///
/// <para>
/// A batch run and not a start-up step. On a corpus whose links have not moved it is minutes of work
/// that changes nothing, and the commands that move links are the ones that call it.
/// </para>
/// </summary>
internal sealed class AnnotationCarrier(
    AppDbContext db,
    CrossedNameLoader crossed,
    ILogger<AnnotationCarrier> logger)
{
    /// <summary>
    /// How far two confidences may differ and still be the same number. They are products of
    /// doubles computed in two statements, so equality is asked to the ninth place rather than
    /// exactly.
    /// </summary>
    private const double Unchanged = 1e-9;

    /// <summary>
    /// The seeds of one source: every annotation no link produced.
    ///
    /// <para>
    /// Most are the source's own rows. Some are not, because a pass that reaches a word another
    /// source already names with the same entity writes its claim onto that row rather than a
    /// second row: a Scrivener word the Nestle occurrence had already been carried to holds the
    /// Scrivener occurrence only as a claim, and the Stephanus words were carried from it. So a claim
    /// of the source that no link produced, standing on a row a link did produce, is a seed too, at
    /// the claim's own method and number.
    /// </para>
    ///
    /// <para>
    /// A word a source named twice is seeded once, because the workspace holds one answer per word.
    /// </para>
    /// </summary>
    private const string Seed =
        """
        CREATE TEMP TABLE seed (
            row_id bigint NOT NULL,
            word_id bigint NOT NULL,
            entity_id integer NOT NULL,
            confidence double precision,
            method text NOT NULL,
            source text NOT NULL)
        ON COMMIT DROP;
        INSERT INTO seed
        SELECT a.id, a.word_id, a.entity_id, a.confidence, a.method, a.source
        FROM word_entity a
        WHERE a.source = ANY(@sources) AND coalesce(a.note, '') NOT LIKE @carried;
        INSERT INTO seed
        SELECT DISTINCT ON (a.id) a.id, a.word_id, a.entity_id, c.confidence, c.method, c.source
        FROM word_entity a
        JOIN word_entity_claim c ON c.word_entity_id = a.id
        WHERE a.note LIKE @carried
          AND c.source = ANY(@sources) AND c.method <> @stated
          AND coalesce(c.note, '') NOT LIKE @carried
        ORDER BY a.id, c.id;
        CREATE INDEX ON seed (word_id, entity_id);
        INSERT INTO pending_annotation (word_id, entity_id, confidence, corroborated, note)
        SELECT DISTINCT ON (s.word_id) s.word_id, s.entity_id, s.confidence, FALSE, ''
        FROM seed s
        ORDER BY s.word_id, s.row_id
        """;

    /// <summary>
    /// What the links reach from those seeds, as the rows they should be: the seed's source, the
    /// seed's method or — for the resolution — the form of the word where the word it lands on
    /// carries a number several records bear.
    /// </summary>
    private static readonly string Expected =
        $"""
         CREATE TEMP TABLE expected (
             word_id bigint NOT NULL,
             entity_id integer NOT NULL,
             confidence double precision,
             note text NOT NULL,
             link double precision NOT NULL,
             seed_id bigint NOT NULL,
             source text NOT NULL,
             seed_method text NOT NULL,
             method text NOT NULL)
         ON COMMIT DROP;
         INSERT INTO expected
         SELECT DISTINCT ON (p.word_id) p.word_id, p.entity_id, p.confidence, p.note, p.link,
                seed.row_id AS seed_id, seed.source, seed.method AS seed_method,
                CASE WHEN @reconsider AND {EntityAnnotationLoader.Distinguished}
                     THEN @form ELSE seed.method END AS method
         FROM pending_annotation p
         JOIN word w ON w.id = p.word_id
         JOIN seed ON seed.word_id = p.through AND seed.entity_id = p.entity_id
         WHERE p.through IS NOT NULL
         ORDER BY p.word_id, seed.row_id;
         CREATE INDEX ON expected (word_id, entity_id)
         """;

    private const string Carried =
        """
        SELECT count(*) FROM word_entity a
        WHERE a.source = ANY(@sources) AND a.note LIKE @carried
        """;

    private const string Withdraw =
        """
        DELETE FROM word_entity a
        WHERE a.source = ANY(@sources) AND a.note LIKE @carried
          AND NOT EXISTS (
              SELECT 1 FROM expected x
              WHERE x.word_id = a.word_id AND x.entity_id = a.entity_id
                AND x.source = a.source AND x.method = a.method
                AND abs(coalesce(x.confidence, -1) - coalesce(a.confidence, -1)) < @unchanged)
        """;

    /// <summary>
    /// The rows the links reach and nothing holds. A word another source already names with this
    /// entity keeps that row, and gains this source's claims below — the same thing every pass does
    /// when a second method arrives at an answer already given.
    /// </summary>
    private const string Write =
        """
        INSERT INTO word_entity (word_id, entity_id, method, confidence, source, note)
        SELECT x.word_id, x.entity_id, x.method, x.confidence, x.source, x.note
        FROM expected x
        ON CONFLICT (word_id, entity_id) DO NOTHING
        """;

    /// <summary>
    /// The seed's claims, crossed with the link. A source's testimony about the verse is the same
    /// testimony wherever the word stands and keeps its words and its null; everything else is
    /// multiplied by what the link is worth, and a claim a person made without a number gains one
    /// only where the link is less than certain.
    /// </summary>
    private const string Claim =
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT a.id,
               CASE WHEN c.method = @stated THEN c.method
                    WHEN c.method = x.seed_method THEN x.method
                    ELSE c.method END,
               CASE WHEN c.method = @stated THEN NULL
                    WHEN c.confidence IS NULL THEN nullif(x.link, 1.0)
                    ELSE c.confidence * x.link END,
               c.source,
               CASE WHEN c.method = @stated THEN c.note ELSE x.note END
        FROM expected x
        JOIN word_entity_claim c ON c.word_entity_id = x.seed_id
             AND coalesce(c.note, '') NOT LIKE @carried
        JOIN word_entity a ON a.word_id = x.word_id AND a.entity_id = x.entity_id
        ON CONFLICT DO NOTHING
        """;

    private const string PerText =
        """
        SELECT t.slug, count(DISTINCT a.word_id)
        FROM word_entity a JOIN word w ON w.id = a.word_id JOIN text t ON t.id = w.text_id
        GROUP BY 1
        """;

    public async Task<CarryOutcome> Carry(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var before = await Counts(connection, cancellationToken);

        int seeds = 0, kept = 0, withdrawn = 0, written = 0;
        var groups = await Groups(cancellationToken);
        foreach (var (sources, reconsider) in groups)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var parameters = new (string, object?)[]
            {
                ("sources", sources),
                ("carried", Annotating.CarriedNote),
                ("reconsider", reconsider),
                ("form", EnumSpelling.Of(LinkMethod.Lexical)),
                ("unchanged", Unchanged),
                ("stated", EnumSpelling.Of(LinkMethod.StatedBySource)),
            };

            await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
            await Execute(connection, transaction, Seed, cancellationToken, parameters);
            var seeded = await Scalar(
                connection, transaction, "SELECT count(*) FROM pending_annotation", cancellationToken);
            await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);
            await Execute(connection, transaction, Expected, cancellationToken, parameters);

            var carried = await Scalar(connection, transaction, Carried, cancellationToken, parameters);
            var taken = await Execute(connection, transaction, Withdraw, cancellationToken, parameters);
            var added = await Execute(connection, transaction, Write, cancellationToken, parameters);
            await Execute(connection, transaction, Claim, cancellationToken, parameters);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Carried again from {Seeds} seeds of \"{Source}\": {Kept} kept, {Withdrawn} withdrawn, " +
                "{Written} written",
                seeded, sources[0], carried - taken, taken, added);

            seeds += seeded;
            kept += carried - taken;
            withdrawn += taken;
            written += added;
        }

        var after = await Counts(connection, cancellationToken);
        var byText = before.Keys.Union(after.Keys)
            .Order(StringComparer.Ordinal)
            .Select(text => (text, before.GetValueOrDefault(text), after.GetValueOrDefault(text)))
            .ToList();

        var outcome = new CarryOutcome(
            groups.Count, seeds, kept, withdrawn, written, byText, started.Elapsed);
        logger.LogInformation("Carried: {Outcome}", outcome);

        // The links that give two words of one verse each other's names are still crossed, so the
        // carry has just written the crossed answer again.
        logger.LogInformation("{Outcome}", await crossed.Load(cancellationToken));
        return outcome;
    }

    /// <summary>
    /// The batches, as the passes wrote them: each source alone, except the name resolution's, whose
    /// four source strings one pass wrote together and whose method is the one re-asked of the word
    /// it lands on. A source is found on the claims as well as on the rows, because an answer that
    /// only ever landed on another source's row is held there and nowhere else.
    /// </summary>
    private async Task<List<(string[] Sources, bool Reconsider)>> Groups(CancellationToken cancellationToken)
    {
        var present = await db.WordEntities
            .Select(a => a.Source)
            .Union(db.WordEntityClaims
                .Where(c => c.Method != LinkMethod.StatedBySource)
                .Select(c => c.Source))
            .ToListAsync(cancellationToken);

        var resolution = EntityAnnotationLoader.Written.Where(present.Contains).ToArray();
        var groups = new List<(string[], bool)>();
        if (resolution.Length > 0)
        {
            groups.Add((resolution, true));
        }

        // The crossed-back rows stand on translated words and are derived from the links rather
        // than from a seed, so carrying them would spread a Russian word's name along Russian links.
        // They are written again from scratch once the carry is done.
        groups.AddRange(present
            .Except([.. EntityAnnotationLoader.Written, CrossedNameLoader.Source,
                .. SenseReadingFiles.AllRulings().Where(file => !file.Carry).Select(file => file.Source)], StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(source => (new[] { source }, false)));

        return groups;
    }

    private static async Task<Dictionary<string, int>> Counts(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(PerText, connection);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts[reader.GetString(0)] = (int)reader.GetInt64(1);
        }

        return counts;
    }

    private static async Task<int> Execute(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> Scalar(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>One statement, given the parameters it names out of the batch's list.</summary>
    private static NpgsqlCommand Command(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        (string Name, object? Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        foreach (var (name, value) in parameters.Where(p => sql.Contains('@' + p.Name, StringComparison.Ordinal)))
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}
