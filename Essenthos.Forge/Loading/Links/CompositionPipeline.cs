using System.Diagnostics;
using System.Text;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Reached">Pairs the composed routes propose, before merging.</param>
/// <param name="Agreed">Pairs readings that share no evidence both reached, which is the measure of whether any works.</param>
/// <param name="Added">Pairs only the middle texts reached — the coverage composing buys.</param>
/// <param name="Written">Pairs written as links of their own, because nothing else names those words.</param>
/// <param name="Corroborated">
/// Pairs a source has already stated, which become a second claim on the link that states them
/// rather than a second link beside it.
/// </param>
/// <param name="Admitted">Which answers were written, per combination of readings, and from what confidence.</param>
internal sealed record CompositionOutcome(
    string From,
    string Via,
    string To,
    int Direct,
    int Reached,
    int Agreed,
    int Added,
    int Written,
    int Corroborated,
    string Admitted,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        $"{From} to {To} through {Via}: {Written} links from {Direct} direct and {Reached} composed — " +
            $"{Agreed} reached by more than one reading, {Added} only through {Via}, " +
            $"{Corroborated} agreeing with a link a source already states; {Admitted}";

}

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

    /// <param name="viaSlugs">
    /// The middle texts, one or two. A second is a second route that shares no evidence with the
    /// first, so the two agreeing is worth as much as either agreeing with the direct alignment.
    /// </param>
    /// <param name="unmeasured">
    /// Write every answer from the threshold where nothing, in this pair or in another text of its
    /// language, states the target to measure against. Otherwise such a run is refused.
    /// </param>
    public async Task<CompositionOutcome> Run(
        string fromSlug,
        IReadOnlyList<string> viaSlugs,
        string toSlug,
        double minimumConfidence,
        double precision = Admission.DefaultPrecision,
        bool unmeasured = false,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var from = await Text(fromSlug, cancellationToken);
        var to = await Text(toSlug, cancellationToken);

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var proposed = await Propose(connection, fromSlug, viaSlugs, toSlug, false, null, cancellationToken);
        var statements = await Statements(connection, from.Id, to.Id, cancellationToken);
        var (admission, _) = await Admit(
            connection, from, viaSlugs, to, proposed, statements, minimumConfidence, precision, false, null,
            cancellationToken);
        if (admission.Floors.Count == 0 && !unmeasured)
        {
            throw new InvalidOperationException(
                $"Nothing states {toSlug} for {fromSlug}, or for any other text in its language, so there is " +
                "no measure of which of its answers are right. Run it with --dry-run to see what it would add, " +
                "and add --unmeasured to write every answer from the threshold regardless.");
        }

        var merged = Merge(proposed, admission);
        var (fresh, corroborated) = await Write(connection, from, to, [.. viaSlugs], merged, cancellationToken);

        var outcome = new CompositionOutcome(
            fromSlug, string.Join(" and ", viaSlugs), toSlug,
            proposed.Written.Count + proposed.Reduced.Count,
            proposed.Composed.Sum(route => route.Count),
            merged.Count(link => Routes.Families(link.Route) > 1),
            merged.Count(link => (link.Route & (Route.Written | Route.Reduced)) == 0),
            fresh,
            corroborated,
            admission.Describe([.. viaSlugs]),
            started.Elapsed);

        logger.LogInformation("Composed {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// What a run would write and how far it would carry the text, without writing anything: the
    /// coverage before and after, book by book, and how often the merged answer names the word a
    /// source already states where one does. None of the three readings reads those statements, so
    /// they are a held-out answer key and not the model repeating what it was given.
    /// </summary>
    /// <param name="books">
    /// A trial on a few canonical books, trained on those books alone. The whole text trains on more
    /// and scores at least as well, so a trial's figures are a floor.
    /// </param>
    public async Task<string> Measure(
        string fromSlug,
        IReadOnlyList<string> viaSlugs,
        string toSlug,
        double minimumConfidence,
        IReadOnlySet<int>? books,
        double precision = Admission.DefaultPrecision,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var from = await Text(fromSlug, cancellationToken);
        var to = await Text(toSlug, cancellationToken);

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var proposed = await Propose(connection, fromSlug, viaSlugs, toSlug, true, books, cancellationToken);
        var trained = started.Elapsed;
        var statements = await Statements(connection, from.Id, to.Id, cancellationToken);
        var scope = await CompositionTrial.Scope(connection, from.Id, to.Id, statements, books, cancellationToken);
        var every = Merge(proposed, Admission.Everything(minimumConfidence));
        var (admission, measured) = await Admit(
            connection, from, viaSlugs, to, proposed, statements, minimumConfidence, precision, true, books,
            cancellationToken);

        var report = new StringBuilder()
            .AppendLine("where each combination of readings named the stated word, from each confidence up:")
            .AppendLine(measured);
        foreach (var (title, merged) in new[]
                 {
                     ("every answer from the ordinary threshold:", every),
                     ($"as a run writes it — {admission.Describe([.. viaSlugs])}:", Merge(proposed, admission)),
                 })
        {
            report.AppendLine(title).Append(CompositionTrial.Score(scope, merged, [.. viaSlugs]).Describe(
                fromSlug, string.Join(" and ", viaSlugs), toSlug, minimumConfidence, books, trained, started.Elapsed));
        }

        return report.ToString();
    }

    /// <param name="Composed">One set of pairs per middle text.</param>
    internal sealed record Proposed(
        IReadOnlyList<(long From, long To, double Confidence)> Written,
        IReadOnlyList<(long From, long To, double Confidence)> Reduced,
        IReadOnlyList<IReadOnlyList<(long From, long To, double Confidence)>> Composed);

    private async Task<Proposed> Propose(
        NpgsqlConnection connection,
        string fromSlug,
        IReadOnlyList<string> viaSlugs,
        string toSlug,
        bool trial,
        IReadOnlySet<int>? books,
        CancellationToken cancellationToken)
    {
        var from = await Text(fromSlug, cancellationToken);
        var to = await Text(toSlug, cancellationToken);
        var joins = await VerseJoins.Load(connection, from.Id, to.Id, cancellationToken);

        var reduced = await aligner.Proposals(
            fromSlug, toSlug, Workspace(fromSlug, toSlug, trial, books), AgreementFloor,
            books: books, cancellationToken: cancellationToken);
        var written = await aligner.Proposals(
            fromSlug, toSlug, Workspace(fromSlug, toSlug, trial, books) + "-written", AgreementFloor, asWritten: true,
            books: books, cancellationToken: cancellationToken);
        var composed = new List<IReadOnlyList<(long From, long To, double Confidence)>>(viaSlugs.Count);
        foreach (var viaSlug in viaSlugs)
        {
            var via = await Text(viaSlug, cancellationToken);
            var first = await aligner.Proposals(
                fromSlug, viaSlug, Workspace(fromSlug, viaSlug, trial, books), AgreementFloor,
                books: books, cancellationToken: cancellationToken);
            composed.Add(Compose(first, await Carried(connection, via.Id, to.Id, cancellationToken), joins));
        }

        return new Proposed(written, reduced, composed);
    }

    /// <summary>
    /// Which answers a run writes: measured on the pair's own statements where it has enough, and
    /// otherwise on the other texts in its language that state the target, each composed the same
    /// way. Also returns what each combination scored from each confidence up, for a dry run to print.
    /// </summary>
    private async Task<(Admission Admission, string Measured)> Admit(
        NpgsqlConnection connection,
        Database.Entities.Text from,
        IReadOnlyList<string> viaSlugs,
        Database.Entities.Text to,
        Proposed proposed,
        IReadOnlyDictionary<long, HashSet<long>> statements,
        double minimumConfidence,
        double precision,
        bool trial,
        IReadOnlySet<int>? books,
        CancellationToken cancellationToken)
    {
        var every = Merge(proposed, Admission.Everything(minimumConfidence));
        var own = Admission.Measure(every, statements, minimumConfidence, precision);
        if (own.Floors.Count > 0)
        {
            return (own, $"  {from.Slug} to {to.Slug}, on its own statements:{Environment.NewLine}" +
                         Admission.Curves(every, statements, [.. viaSlugs]));
        }

        var measured = new List<(string Text, Route Reads, IReadOnlyDictionary<Route, RouteFloor> Floors)>();
        var report = new StringBuilder();
        foreach (var proxy in await Proxies(connection, from, to.Id, cancellationToken))
        {
            var vias = viaSlugs.Where(via => via != proxy.Slug).ToList();
            var proxyProposed = await Propose(connection, proxy.Slug, vias, to.Slug, trial, books, cancellationToken);
            var proxyStatements = await Statements(connection, proxy.Id, to.Id, cancellationToken);
            var proxyEvery = Merge(proxyProposed, Admission.Everything(minimumConfidence));
            var floors = Admission.Measure(proxyEvery, proxyStatements, minimumConfidence, precision);

            var middles = vias.Select(via => Routes.Middles[viaSlugs.ToList().IndexOf(via)]).ToArray();
            measured.Add((
                proxy.Slug,
                middles.Aggregate(Route.Written | Route.Reduced, (reads, middle) => reads | middle),
                floors.Floors.ToDictionary(entry => Translated(entry.Key, middles), entry => entry.Value)));
            report.AppendLine($"  {proxy.Slug} to {to.Slug} through {string.Join(" and ", vias)}, on its statements; " +
                              floors.Describe([.. vias]))
                .AppendLine(Admission.Curves(proxyEvery, proxyStatements, [.. vias]));
        }

        return (Admission.Borrow(measured, [.. viaSlugs], minimumConfidence, precision), report.ToString());
    }

    /// <summary>
    /// A route as another composition numbers it, renumbered for this one: its middle texts are
    /// this pair's, named in another order or with one left out.
    /// </summary>
    /// <param name="middles">For each of the other composition's middle texts, the route it takes here.</param>
    internal static Route Translated(Route route, Route[] middles)
    {
        var translated = route & (Route.Written | Route.Reduced);
        for (var i = 0; i < middles.Length; i++)
        {
            if (route.HasFlag(Routes.Middles[i]))
            {
                translated |= middles[i];
            }
        }

        return translated;
    }

    /// <summary>
    /// The other texts in the source's language whose words a source states against the target,
    /// enough of them to measure on, most first. For English against the originals, the King James
    /// and the Berean.
    /// </summary>
    private static async Task<List<(int Id, string Slug)>> Proxies(
        NpgsqlConnection connection,
        Database.Entities.Text from,
        int toTextId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT t.id, t.slug
            FROM link l
            JOIN text t ON t.id = l.from_text_id
            JOIN link_word f ON f.link_id = l.id AND f.side = 'from'
            WHERE l.to_text_id = @to AND l.method <> 'aligner' AND t.language = @language AND t.id <> @from
            GROUP BY t.id, t.slug
            HAVING count(DISTINCT f.word_id) >= @fewest
            ORDER BY count(DISTINCT f.word_id) DESC, t.slug
            """, connection);
        command.Parameters.AddWithValue("to", toTextId);
        command.Parameters.AddWithValue("language", from.Language);
        command.Parameters.AddWithValue("from", from.Id);
        command.Parameters.AddWithValue("fewest", Admission.FewestToMeasure);
        command.CommandTimeout = 600;

        var proxies = new List<(int, string)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            proxies.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        return proxies;
    }

    /// <summary>
    /// The readings made into one answer per word.
    ///
    /// Each route already keeps its own best answer per word; the merge can put two different best
    /// answers back on one word, and one of them is again a runner-up. Which one survives is decided
    /// by how many readings found it before it is decided by confidence, and that order matters.
    /// Matthew 1:4 has the Synodal's second "Аминадав" scored 1.000 against δέ by the written
    /// reading alone, while the stems and the English both say Ἀμιναδάβ less loudly. Taking the
    /// loudest answer takes the wrong one — and agreement between readings that share no evidence is
    /// the whole reason there are three.
    /// </summary>
    /// <param name="admission">Which answers may be written at all, before one is chosen per word.</param>
    internal static List<RoutedLink> Merge(Proposed proposed, Admission admission) =>
        Routes.MergeFamilies(proposed.Written, proposed.Reduced, [.. proposed.Composed])
            .Where(admission.Admits)
            .GroupBy(link => link.From)
            .SelectMany(group =>
            {
                var agreed = group.Max(link => Routes.Families(link.Route));
                var contenders = group.Where(link => Routes.Families(link.Route) == agreed).ToList();
                var best = contenders.Max(link => link.Confidence);
                return contenders.Where(link => link.Confidence >= best);
            })
            .ToList();

    /// <summary>
    /// Where a pair's model is kept. A trial trains models of its own, which a run must never find
    /// and reuse: a model is reused whenever its workspace exists, and a trial's may be over a few
    /// books, or over tokens an earlier build reduced differently.
    /// </summary>
    internal static string Workspace(string from, string to, bool trial = false, IReadOnlySet<int>? books = null) =>
        Path.Combine(Path.GetTempPath(), "essenthos-align",
            $"{from}-{to}" + (trial ? "-trial" : string.Empty) +
            (books is null ? string.Empty : $"-books-{string.Join('-', books.Order())}"));

    /// <summary>
    /// The two hops joined at the middle text's word. One link of the middle text may name several
    /// words on each side -- "it was good" is three English words against one Hebrew one -- so the
    /// join is over words rather than links, and several paths may reach the same pair. Those are
    /// one claim found several times over the same evidence, so the pair takes the best path and
    /// not the sum of them.
    ///
    /// A pair whose verses the frame does not join is not proposed at all: the middle text may be
    /// stated across a verse boundary it draws where these two texts do not.
    /// </summary>
    internal static List<(long From, long To, double Confidence)> Compose(
        IReadOnlyList<(long From, long To, double Confidence)> first,
        ILookup<long, (long To, double Confidence)> second,
        VerseJoins joins)
    {
        var best = new Dictionary<(long, long), double>(400_000);

        foreach (var (from, bridge, carried) in first)
        {
            foreach (var (to, stated) in second[bridge])
            {
                if (!joins.Joins(from, to))
                {
                    continue;
                }

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
                Confidence(reader, 2) * SpecificityOf(reader.GetInt64(3))));
        }

        return rows.ToLookup(row => row.Bridge, row => (row.To, row.Confidence));
    }

    /// <summary>
    /// How often a composed link running through a middle link of this size turns out to be right,
    /// as a share of how often one running through a stated one-to-one pair does.
    ///
    /// A link naming one English word and one Hebrew word states that pair, and carrying a
    /// correspondence through it adds almost no doubt. A link naming a phrase states that the
    /// phrase renders the word, and which of its words a foreign one arrives at is a question the
    /// file does not answer -- so composing through it is worth less than the whole of the claim.
    ///
    /// **How much less is measured, not assumed.** This was 1/n, which is what a pure n-way choice
    /// would cost, and it was between two and three times too harsh. Against the 2,647 links the
    /// Ukrainian interlinear states, the path UBIO -&gt; KJV -&gt; original agrees with the interlinear:
    ///
    /// <code>
    /// English words on the middle link   proposals   agrees
    ///                                1        1364    89.1 %
    ///                                2         622    80.4 %
    ///                                3         455    71.0 %
    ///                                4         126    69.8 %
    ///                           5 or more       80    56.3 %
    /// </code>
    ///
    /// So a three-word phrase is worth 0.80 of a stated pair and not 0.33, and the comment that
    /// stood here -- *those running through a phrase are mostly not right* -- was wrong: at every
    /// size measured they are mostly right. What 1/n cost was Genesis 1:3, where the King James
    /// states that *Let there be* renders יְהִי and the Russian **будет** aligns to *be* at 0.55.
    /// A third of that is 0.18, under the floor, so the word reached nothing at all while **да**
    /// beside it reached the Hebrew by the direct route.
    ///
    /// The sample is the only stated word-level correspondence a Slavic text has, so it is small
    /// and it leans towards the words an interlinear chooses to annotate, which are content words.
    /// Beyond five the counts are in the tens and the buckets are pooled rather than believed
    /// separately. Re-measure it when another stated mapping arrives, by scoring the composed
    /// proposals against the stated pairs, bucketed by the number of English words on the middle
    /// link.
    /// </summary>
    private static readonly double[] Confirmed = [0.891, 0.804, 0.710, 0.698, 0.563];

    internal static double SpecificityOf(long englishWords) =>
        Confirmed[(int)Math.Clamp(englishWords, 1, Confirmed.Length) - 1] / Confirmed[0];

    private static double Confidence(NpgsqlDataReader reader, int column) =>
        reader.IsDBNull(column) ? 1 : reader.GetDouble(column);

    /// <summary>
    /// Replaces the aligner links between the two texts with the merged set, in one transaction, so
    /// there is never a moment where a pair is claimed twice by two routes that have not been
    /// reconciled.
    ///
    /// <para>
    /// A pair a source has already stated is not written again. The routes reach words the
    /// interlinear also annotates — the same word of the Ukrainian against the same word of the
    /// Hebrew — and a second row saying that is not a second fact: it makes a word pair nobody
    /// doubts read as contended, and it double-counts it in every measure. So the guess becomes a
    /// claim on the link that states the pair, which is what <c>link_claim</c> is for, and the
    /// stated row keeps the source's identity because a guess can be rebuilt by running the aligner
    /// again and a statement cannot be rebuilt at all.
    /// </para>
    /// </summary>
    internal async Task<(int Fresh, int Corroborated)> Write(
        NpgsqlConnection connection,
        Database.Entities.Text from,
        Database.Entities.Text to,
        string[] viaSlugs,
        IReadOnlyList<RoutedLink> merged,
        CancellationToken cancellationToken)
    {
        var renders = EnumSpelling.Of(LinkRelation.Renders);
        var stated = await Stated(connection, from.Id, to.Id, renders, cancellationToken);
        var claimed = (await Statements(connection, from.Id, to.Id, cancellationToken)).Keys.ToHashSet();
        var (fresh, agreeing) = Split(merged, stated, claimed, viaSlugs);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await using (var delete = new NpgsqlCommand(
            "DELETE FROM link WHERE from_text_id = @from AND to_text_id = @to AND method = 'aligner'",
            connection, (NpgsqlTransaction)transaction.GetDbTransaction()))
        {
            delete.Parameters.AddWithValue("from", from.Id);
            delete.Parameters.AddWithValue("to", to.Id);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var withdraw = new NpgsqlCommand(WithdrawEarlierAgreement, connection,
                         (NpgsqlTransaction)transaction.GetDbTransaction()))
        {
            withdraw.Parameters.AddWithValue("from", from.Id);
            withdraw.Parameters.AddWithValue("to", to.Id);
            withdraw.Parameters.AddWithValue("aligner", EnumSpelling.Of(LinkMethod.Aligner));
            withdraw.CommandTimeout = 600;
            await withdraw.ExecuteNonQueryAsync(cancellationToken);
        }

        var firstId = await ReserveLinkIds(connection, fresh.Count, cancellationToken);
        var method = EnumSpelling.Of(LinkMethod.Aligner);
        var fromSide = EnumSpelling.Of(LinkSide.From);
        var toSide = EnumSpelling.Of(LinkSide.To);

        await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
        {
            for (var i = 0; i < fresh.Count; i++)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(firstId + i, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(from.Id, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(to.Id, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(renders, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(fresh[i].Confidence, NpgsqlDbType.Double, cancellationToken);
                await writer.WriteAsync(
                    Routes.Describe(fresh[i].Route, viaSlugs), NpgsqlDbType.Text, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(LinkWordImport, cancellationToken))
        {
            for (var i = 0; i < fresh.Count; i++)
            {
                await Row(writer, firstId + i, fresh[i].From, fromSide, cancellationToken);
                await Row(writer, firstId + i, fresh[i].To, toSide, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        // The claim that says this loader is the one asserting these links. Written here rather
        // than left to a backfill: a link with no claim is invisible to the agreement measure, and
        // the measure spent a day reporting the migration instead of the corpus.
        await LinkClaims.Record(connection, transaction, firstId, fresh.Count, cancellationToken);

        await LinkClaims.Corroborate(
            connection, transaction, agreeing, LinkMethod.Aligner, Agreement, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return (fresh.Count, agreeing.Count);
    }

    /// <summary>
    /// The merged pairs divided into the ones that become links of their own and the ones that
    /// become a second claim on a link a source already wrote.
    ///
    /// Each corroboration keeps the confidence and the source of the pair that produced it: the
    /// source names which readings found it, and that is the only thing a claim from this run says
    /// which the stated link does not say for itself.
    ///
    /// <para>
    /// A pair on a word some other method has already answered for, naming a different target
    /// word, is neither: it is dropped. The answer that stands on that word — a printed Strong
    /// number, a hand-made alignment — is the better-founded one, and a guess written beside it
    /// would make the word read as contended between the two. Over Luther's Old Testament and the
    /// Reina-Valera's, the aligner names another word on 10–12% of the stated words it reaches.
    /// </para>
    /// </summary>
    /// <param name="claimed">
    /// Every source word a link that is not the aligner's names against the target, whatever its
    /// shape: a phrase a source states answers for each of its words as much as a pair does.
    /// </param>
    internal static (
        List<RoutedLink> Fresh,
        List<(long Link, double Confidence, string Source)> Agreeing) Split(
        IReadOnlyList<RoutedLink> merged,
        IReadOnlyDictionary<(long From, long To), long> stated,
        IReadOnlySet<long> claimed,
        params string[] viaSlugs)
    {
        var fresh = new List<RoutedLink>(merged.Count);
        var agreeing = new List<(long Link, double Confidence, string Source)>();

        foreach (var link in merged)
        {
            if (stated.TryGetValue((link.From, link.To), out var already))
            {
                agreeing.Add((already, link.Confidence, Routes.Describe(link.Route, viaSlugs)));
            }
            else if (!claimed.Contains(link.From))
            {
                fresh.Add(link);
            }
        }

        return (fresh, agreeing);
    }

    /// <summary>
    /// The aligner's answers an earlier run left as claims on links a source states. A run replaces
    /// the aligner's links between two texts, and its agreement with a statement is part of what it
    /// answered, so that is replaced too: left in place, a rerun stacks a second aligner claim on
    /// every stated pair it reaches again, under a differently worded route, and keeps an agreement
    /// the model no longer makes on every pair it does not.
    /// </summary>
    private const string WithdrawEarlierAgreement =
        """
        DELETE FROM link_claim c
        USING link l
        WHERE c.link_id = l.id AND l.from_text_id = @from AND l.to_text_id = @to
          AND l.method <> @aligner AND c.method = @aligner
        """;

    /// <summary>
    /// What the aligner writes when it arrives at a pair somebody has already stated.
    /// </summary>
    private const string Agreement =
        "The aligner reached a pair this source states, so it stands as a second voice for that " +
        "link rather than as a rival link naming the same two words.";

    /// <summary>
    /// The word pairs a source has already stated for these two texts, by the two words they name.
    ///
    /// Only the links naming one word on each side, because that is the only shape a composed link
    /// has and therefore the only one it can duplicate: a stated link naming a phrase says
    /// something no single pair says, and a link with no word on one side is an absence rather than
    /// a correspondence. The aligner's own rows are left out because they are about to be deleted
    /// and rewritten.
    /// </summary>
    private static async Task<Dictionary<(long From, long To), long>> Stated(
        NpgsqlConnection connection,
        int fromTextId,
        int toTextId,
        string relation,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT min(f.word_id), min(t.word_id), l.id
            FROM link l
            JOIN link_word f ON f.link_id = l.id AND f.side = 'from'
            JOIN link_word t ON t.link_id = l.id AND t.side = 'to'
            WHERE l.from_text_id = @from AND l.to_text_id = @to
              AND l.relation = @relation AND l.method <> 'aligner'
            GROUP BY l.id
            HAVING count(*) = 1
            """, connection);
        command.Parameters.AddWithValue("from", fromTextId);
        command.Parameters.AddWithValue("to", toTextId);
        command.Parameters.AddWithValue("relation", relation);

        var stated = new Dictionary<(long, long), long>(20_000);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            stated[(reader.GetInt64(0), reader.GetInt64(1))] = reader.GetInt64(2);
        }

        return stated;
    }

    /// <summary>
    /// The target words each source word is joined to by a link that is not the aligner's: the
    /// words that already have an answer standing on them, and what it is. A source word a source
    /// says renders nothing has an answer too, and it is the empty one.
    /// </summary>
    internal static async Task<Dictionary<long, HashSet<long>>> Statements(
        NpgsqlConnection connection,
        int fromTextId,
        int toTextId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT f.word_id, t.word_id
            FROM link l
            JOIN link_word f ON f.link_id = l.id AND f.side = 'from'
            LEFT JOIN link_word t ON t.link_id = l.id AND t.side = 'to'
            WHERE l.from_text_id = @from AND l.to_text_id = @to AND l.method <> 'aligner'
            """, connection);
        command.Parameters.AddWithValue("from", fromTextId);
        command.Parameters.AddWithValue("to", toTextId);
        command.CommandTimeout = 600;

        var statements = new Dictionary<long, HashSet<long>>(400_000);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var word = reader.GetInt64(0);
            if (!statements.TryGetValue(word, out var targets))
            {
                statements[word] = targets = [];
            }

            if (!reader.IsDBNull(1))
            {
                targets.Add(reader.GetInt64(1));
            }
        }

        return statements;
    }

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
