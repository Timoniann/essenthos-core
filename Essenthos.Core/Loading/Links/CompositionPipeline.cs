using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Reached">Pairs the composed route proposes, before merging.</param>
/// <param name="Agreed">Pairs both routes reached, which is the measure of whether either works.</param>
/// <param name="Added">Pairs only the composed route reached — the coverage this buys.</param>
internal sealed record CompositionOutcome(
    string From,
    string Via,
    string To,
    int Direct,
    int Reached,
    int Agreed,
    int Added,
    int Written,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        $"{From} to {To} through {Via}: {Written} links from {Direct} direct and {Reached} composed — " +
        $"{Agreed} reached by both ({Share(Agreed, Direct)} of the direct route), {Added} only through {Via}";

    private static string Share(int part, int whole) =>
        whole == 0 ? "none" : (part / (double)whole).ToString("P1", CultureInfo.InvariantCulture);}

/// <summary>
/// The second route to the same Hebrew word, through the English.
///
/// Russian against Hebrew is one hard hop. Russian against the King James is an easy one — two
/// modern languages, similar length, similar order — and the King James against BHSA is not a hop
/// at all, because a file states it word by word. So the same correspondence can be found a second
/// time over evidence the first route never touches, and the two answers can be compared.
///
/// The gain is in the words the direct route leaves bare. Genesis 1:4 in the Synodal had no Hebrew
/// for <em>отделил</em>, <em>от</em> or <em>он</em>; the first two reach וַיַּבְדֵּל and בֵּין through
/// <em>divided</em> and <em>from</em>, which the file states outright. The third has no Hebrew word
/// to reach, and still has none, which is the right answer and not a gap.
///
/// The merge is <see cref="Routes"/>. This class only fetches, joins and writes.
/// </summary>
internal sealed class CompositionPipeline(
    AppDbContext db,
    AlignmentPipeline aligner,
    ILogger<CompositionPipeline> logger)
{
    /// <summary>
    /// How faint a proposal may be and still count as one route's half of an agreement.
    ///
    /// A pair written on one route's word alone has to clear the ordinary threshold. A pair two
    /// routes reach over different evidence does not: the Synodal's "отделил" against וַיַּבְדֵּל is 0.23
    /// aligned directly and 0.13 through the English, and either alone is noise. Both naming the
    /// same word out of the twenty in that verse is not, and the combination has to clear the
    /// ordinary threshold anyway -- so this decides only what is allowed to meet, never what is
    /// written.
    /// </summary>
    public const double AgreementFloor = 0.1;

    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, confidence, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport =
        """
        COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)
        """;

    public async Task<CompositionOutcome> Run(
        string fromSlug,
        string viaSlug,
        string toSlug,
        double minimumConfidence,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var from = await Text(fromSlug, cancellationToken);
        var via = await Text(viaSlug, cancellationToken);
        var to = await Text(toSlug, cancellationToken);

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var direct = await aligner.Proposals(
            fromSlug, toSlug, Workspace(fromSlug, toSlug), AgreementFloor, cancellationToken: cancellationToken);
        var first = await aligner.Proposals(
            fromSlug, viaSlug, Workspace(fromSlug, viaSlug), AgreementFloor, cancellationToken: cancellationToken);
        var second = await Carried(connection, via.Id, to.Id, cancellationToken);

        var composed = Compose(first, second);

        // Each route already keeps only its own best answer per word; the merge can put two
        // different best answers back on one word, and one of them is again a runner-up. Measured
        // on the pair a source states, dropping those costs 0.3% of the coverage and returns half a
        // point of precision — and it is what stops two words in a verse sharing a third and being
        // highlighted as though they were one phrase.
        var merged = Routes.Merge(direct, composed)
            .Where(link => link.Confidence >= minimumConfidence)
            .GroupBy(link => link.From)
            .SelectMany(group =>
            {
                var best = group.Max(link => link.Confidence);
                return group.Where(link => link.Confidence >= best);
            })
            .ToList();

        var outcome = new CompositionOutcome(
            fromSlug, viaSlug, toSlug,
            direct.Count,
            composed.Count,
            merged.Count(link => link.Route == Route.Both),
            merged.Count(link => link.Route == Route.Composed),
            merged.Count,
            started.Elapsed);

        await Write(connection, from, to, viaSlug, merged, cancellationToken);
        logger.LogInformation("Composed {Outcome}", outcome);
        return outcome;
    }

    private static string Workspace(string from, string to) =>
        Path.Combine(Path.GetTempPath(), "essenthos-align", $"{from}-{to}");

    /// <summary>
    /// The two hops joined at the middle text's word. One link of the middle text may name several
    /// words on each side -- "it was good" is three English words against one Hebrew one -- so the
    /// join is over words rather than links, and several paths may reach the same pair. Those are
    /// one claim found several times over the same evidence, so the pair takes the best path and
    /// not the sum of them.
    /// </summary>
    private static List<(long From, long To, double Confidence)> Compose(
        IReadOnlyList<(long From, long To, double Confidence)> first,
        ILookup<long, (long To, double Confidence)> second)
    {
        var best = new Dictionary<(long, long), double>(400_000);

        foreach (var (from, bridge, carried) in first)
        {
            foreach (var (to, stated) in second[bridge])
            {
                var key = (from, to);
                var confidence = carried * stated;
                if (!best.TryGetValue(key, out var standing) || confidence > standing)
                {
                    best[key] = confidence;
                }
            }
        }

        return [.. best.Select(entry => (entry.Key.Item1, entry.Key.Item2, entry.Value))];
    }

    /// <summary>
    /// What the middle text's words carry to the target. A null confidence is a link somebody
    /// stated, which adds no doubt of its own and passes the first hop's through unchanged -- which
    /// is the whole reason this route is worth walking.
    /// </summary>
    private static async Task<ILookup<long, (long To, double Confidence)>> Carried(
        NpgsqlConnection connection,
        int viaTextId,
        int toTextId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT f.word_id, t.word_id, l.confidence,
                   (SELECT count(*) FROM link_word n WHERE n.link_id = l.id AND n.side = 'from')
            FROM link l
            JOIN link_word f ON f.link_id = l.id AND f.side = 'from'
            JOIN link_word t ON t.link_id = l.id AND t.side = 'to'
            WHERE l.from_text_id = @via AND l.to_text_id = @to AND l.method <> 'aligner'
            """, connection);
        command.Parameters.AddWithValue("via", viaTextId);
        command.Parameters.AddWithValue("to", toTextId);

        var rows = new List<(long Bridge, long To, double Confidence)>(400_000);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetInt64(0), reader.GetInt64(1),
                Confidence(reader, 2) * Specificity(reader.GetInt64(3))));
        }

        return rows.ToLookup(row => row.Bridge, row => (row.To, row.Confidence));
    }

    /// <summary>
    /// How much a middle link says about any one of its own words.
    ///
    /// A link naming one English word and one Hebrew word states that pair, and carrying a
    /// correspondence through it adds no doubt at all. A link naming a phrase states that the
    /// phrase renders the word, and which of its words a foreign one arrives at is a question the
    /// file does not answer — so composing through it is an n-way choice made without evidence, and
    /// is worth a fraction of the claim rather than the whole of it.
    ///
    /// This is not a tuned number. It is the difference the sampling showed: composed links running
    /// through a stated pair are mostly right, and those running through a phrase are mostly not —
    /// "путь" against דֶּרֶךְ and "котел" against קַלַּחַת on the one hand, "сынов" against נֹחַ and
    /// "твои" against שְׁנֵי on the other, where the file had lumped a whole span onto the head of it.
    /// Four in five composed links run through a phrase, so leaving this out meant writing tens of
    /// thousands of wrong pairs at the confidence of the right ones, which is the failure this
    /// project minds most.
    /// </summary>
    private static double Specificity(long englishWords) => englishWords <= 1 ? 1 : 1.0 / englishWords;

    private static double Confidence(NpgsqlDataReader reader, int column) =>
        reader.IsDBNull(column) ? 1 : reader.GetDouble(column);

    /// <summary>
    /// Replaces the aligner links between the two texts with the merged set, in one transaction, so
    /// there is never a moment where a pair is claimed twice by two routes that have not been
    /// reconciled.
    /// </summary>
    private async Task Write(
        NpgsqlConnection connection,
        Database.Entities.Text from,
        Database.Entities.Text to,
        string viaSlug,
        IReadOnlyList<RoutedLink> merged,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await using (var delete = new NpgsqlCommand(
            "DELETE FROM link WHERE from_text_id = @from AND to_text_id = @to AND method = 'aligner'",
            connection, (NpgsqlTransaction)transaction.GetDbTransaction()))
        {
            delete.Parameters.AddWithValue("from", from.Id);
            delete.Parameters.AddWithValue("to", to.Id);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        var firstId = await ReserveLinkIds(connection, merged.Count, cancellationToken);
        var renders = EnumSpelling.Of(LinkRelation.Renders);
        var method = EnumSpelling.Of(LinkMethod.Aligner);
        var fromSide = EnumSpelling.Of(LinkSide.From);
        var toSide = EnumSpelling.Of(LinkSide.To);

        await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
        {
            for (var i = 0; i < merged.Count; i++)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(firstId + i, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(from.Id, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(to.Id, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(renders, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(merged[i].Confidence, NpgsqlDbType.Double, cancellationToken);
                await writer.WriteAsync(Source(merged[i].Route, viaSlug), NpgsqlDbType.Text, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(LinkWordImport, cancellationToken))
        {
            for (var i = 0; i < merged.Count; i++)
            {
                await Row(writer, firstId + i, merged[i].From, fromSide, cancellationToken);
                await Row(writer, firstId + i, merged[i].To, toSide, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static string Source(Route route, string viaSlug) => route switch
    {
        Route.Direct => "SIL.Machine, aligned directly",
        Route.Composed => $"SIL.Machine, composed through {viaSlug}",
        _ => $"SIL.Machine, aligned directly and composed through {viaSlug}",
    };

    private static async Task Row(
        NpgsqlBinaryImporter writer,
        long linkId,
        long wordId,
        string side,
        CancellationToken cancellationToken)
    {
        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(linkId, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(wordId, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(side, NpgsqlDbType.Text, cancellationToken);
    }

    private static async Task<long> ReserveLinkIds(
        NpgsqlConnection connection,
        int count,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT setval(pg_get_serial_sequence('link', 'id'), " +
            "coalesce((SELECT max(id) FROM link), 0) + @count) - @count + 1", connection);
        command.Parameters.AddWithValue("count", count);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task<Database.Entities.Text> Text(string slug, CancellationToken cancellationToken) =>
        await db.Texts.SingleOrDefaultAsync(t => t.Slug == slug, cancellationToken)
        ?? throw new InvalidOperationException($"There is no text \"{slug}\".");
}
