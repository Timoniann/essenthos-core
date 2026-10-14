using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
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

/// <param name="Since">The newest link the carry had already crossed, or null where nothing recorded one.</param>
/// <param name="Through">The newest link now, which is where the carry stands once this is done.</param>
/// <param name="Scope">The words of the verses the newer links reach, whose names were asked again.</param>
internal sealed record NewLinkCarryOutcome(
    long? Since,
    long Through,
    int Scope,
    int Seeds,
    int Kept,
    int Withdrawn,
    int Written,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        Since == Through
            ? $"No link was drawn since the annotations were last carried, at link {Through}"
            : (Since is null
                  ? $"Carried every annotation over every link, up to link {Through}"
                  : $"Carried the annotations over the links after {Since} up to {Through}, asked again of " +
                    $"{Scope} words of the verses they reach") +
              $", from {Seeds} seeds in {Elapsed}: {Kept} kept, {Withdrawn} withdrawn and {Written} written";
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
/// Over every link it is a batch run and not a start-up step: on a corpus whose links have not moved
/// it is minutes of work that changes nothing, and the commands that move links are the ones that
/// call it. The load carries only over the links drawn since (<see cref="CarryNewLinks"/>).
/// </para>
/// </summary>
internal sealed class AnnotationCarrier(
    AppDbContext db,
    CrossedNameLoader crossed,
    ForeignNames foreign,
    EqualTwinNames twins,
    PronounReferents pronouns,
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
    private static string Seed(bool scoped) =>
        $"""
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
        WHERE a.source = ANY(@sources) AND coalesce(a.note, '') NOT LIKE @carried{Near("a", scoped)};
        INSERT INTO seed
        SELECT DISTINCT ON (a.id) a.id, a.word_id, a.entity_id, c.confidence, c.method, c.source
        FROM word_entity a
        JOIN word_entity_claim c ON c.word_entity_id = a.id
        WHERE a.note LIKE @carried
          AND c.source = ANY(@sources) AND c.method <> @stated
          AND coalesce(c.note, '') NOT LIKE @carried{Near("a", scoped)}
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

    private static string Carried(bool scoped) =>
        $"""
        SELECT count(*) FROM word_entity a
        WHERE a.source = ANY(@sources) AND a.note LIKE @carried{InScope("a", scoped)}
        """;

    private static string Withdraw(bool scoped) =>
        $"""
        DELETE FROM word_entity a
        WHERE a.source = ANY(@sources) AND a.note LIKE @carried{InScope("a", scoped)}
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

    private const string NewestLink = "SELECT max(id) FROM link";

    private const string ReadMark = "SELECT link_id FROM carry_mark WHERE id = @only";

    private const string WriteMark =
        """
        INSERT INTO carry_mark (id, link_id) VALUES (@only, @through)
        ON CONFLICT (id) DO UPDATE SET link_id = excluded.link_id
        WHERE carry_mark.link_id IS DISTINCT FROM excluded.link_id
        """;

    /// <summary>
    /// The words a link newer than <c>@since</c> can have changed the carry's answer for, and the
    /// words that can be seeds of that answer.
    ///
    /// <para>
    /// <c>carry_scope</c> is every word of every verse a newer link holds a word of, on both sides.
    /// Not only the linked words: what the carry asks of a word is asked over the other words its
    /// seed reaches in the same verse of the same text — whether a firmer link names the rendering
    /// beside it, whether it is a leftover beside a capitalised name — so a new firm link can take the
    /// name off a word it does not touch. Nothing reaches further than the verse.
    /// </para>
    ///
    /// <para>
    /// <c>carry_near</c> is every word on any link holding a word of the scope, which is every word
    /// that can be a seed reaching one. A seed taken in is taken in with all its links, so every
    /// window over it is whole, and every seed reaching a word of the scope is taken in, so whether
    /// the seeds agree is asked of all of them. Over the scope the answer is the one a carry over
    /// every link gives.
    /// </para>
    /// </summary>
    private const string Scope =
        """
        DROP TABLE IF EXISTS carry_scope, carry_near;
        CREATE TEMP TABLE carry_scope AS
        SELECT w.id AS word_id
        FROM word w
        WHERE w.verse_id IN (
            SELECT DISTINCT touched.verse_id
            FROM link_word lw
            JOIN word touched ON touched.id = lw.word_id
            WHERE lw.link_id > @since);
        CREATE UNIQUE INDEX ON carry_scope (word_id);
        CREATE TEMP TABLE carry_near AS
        SELECT DISTINCT other.word_id
        FROM carry_scope s
        JOIN link_word mine ON mine.word_id = s.word_id
        JOIN link_word other ON other.link_id = mine.link_id;
        CREATE UNIQUE INDEX ON carry_near (word_id);
        ANALYZE carry_scope;
        ANALYZE carry_near
        """;

    private const string DropScope = "DROP TABLE IF EXISTS carry_scope, carry_near";

    private const string Unscoped = "DELETE FROM expected x WHERE NOT EXISTS (SELECT 1 FROM carry_scope s WHERE s.word_id = x.word_id)";

    /// <summary>
    /// The sources with an answer on a word of the scope or on a word that can seed one, on a row or
    /// in a claim: those whose carried rows the newer links can change.
    /// </summary>
    private const string SourcesInScope =
        """
        SELECT a.source FROM word_entity a
        WHERE a.word_id IN (SELECT word_id FROM carry_near UNION SELECT word_id FROM carry_scope)
        UNION
        SELECT c.source FROM word_entity_claim c
        JOIN word_entity a ON a.id = c.word_entity_id
        WHERE c.method <> @stated
          AND a.word_id IN (SELECT word_id FROM carry_near UNION SELECT word_id FROM carry_scope)
        """;

    private static string Near(string row, bool scoped) =>
        scoped ? $" AND {row}.word_id IN (SELECT word_id FROM carry_near)" : string.Empty;

    private static string InScope(string row, bool scoped) =>
        scoped ? $" AND {row}.word_id IN (SELECT word_id FROM carry_scope)" : string.Empty;

    public async Task<CarryOutcome> Carry(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var through = await Newest(connection, cancellationToken);
        var before = await Counts(connection, cancellationToken);

        var groups = Groups(await Present(cancellationToken));
        var (seeds, kept, withdrawn, written) = await CarryGroups(connection, groups, scoped: false, cancellationToken);

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

        // A name a batch carried before the record whose name its word is had reached the word.
        await foreign.Withdraw(cancellationToken);

        // A person carried onto a pronoun the verse does not settle.
        await pronouns.Withdraw(cancellationToken);

        // The other editions' twins of the words just named, which no link from a seed reaches.
        logger.LogInformation("{Outcome}", await twins.Load(cancellationToken));

        await Mark(connection, through, cancellationToken);
        return outcome;
    }

    /// <summary>
    /// The annotations carried over the links drawn since the carry last ran, and only over the
    /// words those links can have changed the answer for (<see cref="Scope"/>): what a load does
    /// before it names anything, so a link a step drew after a name was written still carries it.
    ///
    /// <para>
    /// A corpus with no mark is carried over every link once. The steps the load runs after this
    /// one take back the crossed names, the foreign names and the persons on pronouns, and write
    /// the editions' twins, so neither branch runs them here. A second run finds no newer link and
    /// writes nothing.
    /// </para>
    ///
    /// <para>
    /// A link taken away is not something a mark can see: a name carried over it stays until a
    /// carry over every link withdraws it.
    /// </para>
    /// </summary>
    public async Task<NewLinkCarryOutcome> CarryNewLinks(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var through = await Newest(connection, cancellationToken);
        var since = await CarriedThrough(connection, cancellationToken);

        // A mark above every link is one a rebuild left behind, and says nothing about these links.
        if (since > through)
        {
            since = null;
        }

        if (since == through)
        {
            return new NewLinkCarryOutcome(since, through, 0, 0, 0, 0, 0, started.Elapsed);
        }

        int scope = 0, seeds = 0, kept = 0, withdrawn = 0, written = 0;
        if (since is null)
        {
            (seeds, kept, withdrawn, written) = await CarryGroups(
                connection, Groups(await Present(cancellationToken)), scoped: false, cancellationToken);
        }
        else
        {
            try
            {
                await Run(connection, Scope, cancellationToken, ("since", since.Value));
                scope = await Count(connection, "SELECT count(*) FROM carry_scope", cancellationToken);
                if (scope > 0)
                {
                    (seeds, kept, withdrawn, written) = await CarryGroups(
                        connection, Groups(await PresentInScope(connection, cancellationToken)), scoped: true,
                        cancellationToken);
                }
            }
            finally
            {
                await Run(connection, DropScope, CancellationToken.None);
            }
        }

        await Mark(connection, through, cancellationToken);
        var outcome = new NewLinkCarryOutcome(since, through, scope, seeds, kept, withdrawn, written, started.Elapsed);
        logger.LogInformation("{Outcome}", outcome);
        return outcome;
    }

    private async Task<(int Seeds, int Kept, int Withdrawn, int Written)> CarryGroups(
        NpgsqlConnection connection,
        List<(string[] Sources, bool Reconsider)> groups,
        bool scoped,
        CancellationToken cancellationToken)
    {
        int seeds = 0, kept = 0, withdrawn = 0, written = 0;
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
            await Execute(connection, transaction, Seed(scoped), cancellationToken, parameters);
            var seeded = await Scalar(
                connection, transaction, "SELECT count(*) FROM pending_annotation", cancellationToken);
            await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);

            await Execute(connection, transaction, Expected, cancellationToken, parameters);
            if (scoped)
            {
                await Execute(connection, transaction, Unscoped, cancellationToken);
            }

            await pronouns.LeaveOut(connection, (NpgsqlTransaction)transaction.GetDbTransaction(), cancellationToken);

            var carried = await Scalar(connection, transaction, Carried(scoped), cancellationToken, parameters);
            var taken = await Execute(connection, transaction, Withdraw(scoped), cancellationToken, parameters);
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

        return (seeds, kept, withdrawn, written);
    }

    /// <summary>
    /// The batches, as the passes wrote them: each source alone, except the name resolution's, whose
    /// four source strings one pass wrote together and whose method is the one re-asked of the word
    /// it lands on. A source is found on the claims as well as on the rows, because an answer that
    /// only ever landed on another source's row is held there and nowhere else.
    /// </summary>
    private static List<(string[] Sources, bool Reconsider)> Groups(IReadOnlyCollection<string> present)
    {
        var resolution = EntityAnnotationLoader.Written.Where(present.Contains).ToArray();
        var groups = new List<(string[], bool)>();
        if (resolution.Length > 0)
        {
            groups.Add((resolution, true));
        }

        // The crossed-back rows and the editions' twins stand on words reached from no seed, so
        // carrying them would spread a name along links it never crossed; both are written again
        // from scratch once the carry is done.
        groups.AddRange(present
            .Except([.. EntityAnnotationLoader.Written, CrossedNameLoader.Source, EqualTwinNames.Source,
                .. SenseReadingFiles.AllRulings().Where(file => !file.Carry).Select(file => file.Source)], StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(source => (new[] { source }, false)));

        return groups;
    }

    private async Task<List<string>> Present(CancellationToken cancellationToken) =>
        await db.WordEntities
            .Select(a => a.Source)
            .Union(db.WordEntityClaims
                .Where(c => c.Method != LinkMethod.StatedBySource)
                .Select(c => c.Source))
            .ToListAsync(cancellationToken);

    private static async Task<List<string>> PresentInScope(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SourcesInScope, connection);
        command.Parameters.AddWithValue("stated", EnumSpelling.Of(LinkMethod.StatedBySource));

        var present = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            present.Add(reader.GetString(0));
        }

        return present;
    }

    private static async Task<long> Newest(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(NewestLink, connection);
        return await command.ExecuteScalarAsync(cancellationToken) is long id ? id : 0;
    }

    private static async Task<long?> CarriedThrough(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ReadMark, connection);
        command.Parameters.AddWithValue("only", CarryMark.Only);
        return await command.ExecuteScalarAsync(cancellationToken) is long id ? id : null;
    }

    private static async Task Mark(NpgsqlConnection connection, long through, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(WriteMark, connection);
        command.Parameters.AddWithValue("only", CarryMark.Only);
        command.Parameters.AddWithValue("through", through);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task Run(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> Count(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
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
