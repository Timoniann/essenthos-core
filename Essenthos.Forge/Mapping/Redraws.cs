using Essenthos.Core.Build;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// The model-drawn method files no line of the declared graph makes, which the store build draws again after its producers:
/// every composition (it reads its own pair's final, made from the file it writes) and the aligner files the graph keeps.
///
/// <para>
/// A file is due when its pair's merge lost rows of it on verses whose text moved, or when anything it reads moved since it was
/// drawn: a text's words, verses or frame, a middle text's or proxy's final, a stored answer of a hop, the pair's floors or
/// rulings. Its own pair's final and statuses are left out, or every merge would make it due again, and so are those of the
/// compositions it reads back and forth with (<see cref="Mutual"/>). Drawn again, it trains
/// what its hops lack (<see cref="Loading.Links.AlignmentPipeline.Trains"/>) and is held to the pair's floors by the merge
/// like any other file. A file <c>mapping/redraw-holds</c> names (<see cref="RedrawHold"/>) is never due.
/// </para>
/// </summary>
internal static class Redraws
{
    /// <summary>The kept files the build leaves as they stand though they are due (<see cref="RedrawHold"/>).</summary>
    public const string HoldsProduct = MappingProducts.Folder + "/redraw-holds";

    /// <summary>One file to draw again.</summary>
    /// <param name="Reads">The other pairs whose finals the file reads; it waits while one of them is due itself.</param>
    public sealed record Due(string Pair, string File, string Why, IReadOnlyList<string> Reads)
    {
        public string Name => $"{Pair} {File}";
    }

    private static readonly string[] Files = [MappingMethods.Composed, MappingMethods.Aligner];

    /// <summary>The kept model-drawn files that are due, and why.</summary>
    public static List<Due> Find(ProductTexts texts, ProductGraph graph, IReadOnlyList<IPairSource>? registry = null)
    {
        registry ??= PairSources.All;
        var store = texts.Store;
        var candidates = new List<(string Pair, string File, List<ProductInput> Inputs, List<string> Reads)>();
        foreach (var file in Files)
        {
            var kept = MappingProducts.Pairs(store)
                .Where(pair => store.Exists(MappingProducts.Of(pair, file))
                               && graph.Find(MappingProducts.Of(pair, file))?.Declaration.Maker == ProductMaker.Kept)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var pair in PairSources.Pairs(texts, file, kept))
            {
                var sources = registry.Where(source => source.File == file && source.States(store, pair)).ToList();
                if (sources.Count == 0)
                {
                    continue;
                }

                var current = PairSources.Inputs(store, pair, sources);
                candidates.Add((pair, file, current, FinalsRead(current, pair)));
            }
        }

        var mutual = Mutual(candidates.Where(c => c.File == MappingMethods.Composed).ToDictionary(c => c.Pair, c => c.Reads, StringComparer.Ordinal));
        var held = Holds(store).Select(hold => hold.Name).ToHashSet(StringComparer.Ordinal);
        var due = new List<Due>();
        foreach (var (pair, file, current, reads) in candidates.Where(c => !held.Contains($"{c.Pair} {c.File}")))
        {
            var cycle = file == MappingMethods.Composed ? mutual.GetValueOrDefault(pair) ?? [] : [];
            var why = Lost(store, pair, file) ?? Moved(store, MappingProducts.Of(pair, file), [pair, .. cycle], current);
            if (why is not null)
            {
                due.Add(new Due(pair, file, why, [.. reads.Where(other => !cycle.Contains(other))]));
            }
        }

        return due;
    }

    /// <summary>The holds of <c>mapping/redraw-holds</c>, each naming a file the build draws again and what lifts it.</summary>
    public static List<RedrawHold> Holds(ProductStore store)
    {
        if (!store.Exists(HoldsProduct))
        {
            return [];
        }

        using var reader = store.Open(HoldsProduct, RedrawHold.Schema);
        var holds = reader.ReadAll();
        foreach (var hold in holds)
        {
            MappingProducts.Split(hold.Pair);
            if (!Files.Contains(hold.File))
            {
                throw new InvalidDataException(
                    $"{HoldsProduct}: {hold.Pair} holds '{hold.File}', which the build never draws again; name one of {string.Join(", ", Files)}.");
            }

            if (hold.Until.Trim().Length == 0)
            {
                throw new InvalidDataException($"{HoldsProduct}: the hold of {hold.Name} names nothing that lifts it (column until).");
            }
        }

        var twice = holds.GroupBy(hold => hold.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        return twice is null ? holds : throw new InvalidDataException($"{HoldsProduct}: {twice.Key} is held twice.");
    }

    /// <summary>
    /// For each composition that reads the final of another which reads its own back, directly or through others (the English
    /// texts measured on each other as proxies, SWETE and OTTLEY, UBIO and NPU2022), the other compositions of that cycle.
    /// Each one's final moves when another is drawn, by a few rows the merge and the names give way to, so a move of one of
    /// them never makes another due: they would be drawn again by turns for ever. What moves their words or answers is read
    /// by each of them directly.
    /// </summary>
    internal static Dictionary<string, HashSet<string>> Mutual(IReadOnlyDictionary<string, List<string>> reads)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var cycles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        void Visit(string pair)
        {
            index[pair] = low[pair] = index.Count;
            stack.Push(pair);
            onStack.Add(pair);
            foreach (var other in reads[pair].Where(reads.ContainsKey))
            {
                if (!index.ContainsKey(other))
                {
                    Visit(other);
                    low[pair] = Math.Min(low[pair], low[other]);
                }
                else if (onStack.Contains(other))
                {
                    low[pair] = Math.Min(low[pair], index[other]);
                }
            }

            if (low[pair] != index[pair])
            {
                return;
            }

            var members = new HashSet<string>(StringComparer.Ordinal);
            string member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                members.Add(member);
            }
            while (member != pair);

            if (members.Count > 1)
            {
                foreach (var one in members)
                {
                    cycles[one] = [.. members.Where(other => other != one)];
                }
            }
        }

        foreach (var pair in reads.Keys.Order(StringComparer.Ordinal).Where(pair => !index.ContainsKey(pair)))
        {
            Visit(pair);
        }

        return cycles;
    }

    /// <summary>The due files that read no final of another due pair; all of them where every one waits for another.</summary>
    public static List<Due> Ready(IReadOnlyList<Due> due)
    {
        var pairs = due.Select(d => d.Pair).ToHashSet(StringComparer.Ordinal);
        var ready = due.Where(d => !d.Reads.Any(pairs.Contains)).ToList();
        return ready.Count > 0 ? ready : [.. due];
    }

    /// <summary>Draws each file again from its sources, training what they lack, <paramref name="atOnce"/> pairs at a time.</summary>
    public static List<(Due Due, PairSources.Outcome Outcome)> Draw(ProductTexts texts, IReadOnlyList<Due> ready, int atOnce, Action<string> log)
    {
        var outcomes = new System.Collections.Concurrent.ConcurrentBag<(Due, PairSources.Outcome)>();
        Parallel.ForEach(ready, new ParallelOptions { MaxDegreeOfParallelism = atOnce },
            due => outcomes.Add((due, PairSources.Write(texts, due.File, due.Pair, force: true, log))));
        return [.. outcomes.OrderBy(drawn => drawn.Item1.Name, StringComparer.Ordinal)];
    }

    /// <summary>What the pair's merge says the file's method lost on verses whose text moved, or null.</summary>
    private static string? Lost(ProductStore store, string pair, string file)
    {
        var report = MappingProducts.Of(pair, MappingProducts.Report);
        if (!store.Exists(report))
        {
            return null;
        }

        var regenerates = MappingMethods.Regenerates(file);
        using var reader = store.Open(report, ReportRow.Schema);
        var needs = reader.Rows().FirstOrDefault(row => row.Kind == "needs" && row.By == regenerates);
        return needs is null ? null : $"its merge says: {needs.Note}";
    }

    /// <summary>
    /// Which inputs moved since the file was drawn, but for the finals and statuses of <paramref name="settled"/> (its own pair
    /// and those of its cycle); null when none did.
    /// </summary>
    internal static string? Moved(ProductStore store, string product, IReadOnlyCollection<string> settled, IReadOnlyList<ProductInput> current)
    {
        var path = store.Find(product);
        var entry = path is null ? null : store.Manifest.Get(path);
        if (entry is null || entry.Inputs.Count == 0)
        {
            return "it was never drawn from the store's files (exported, or the manifest names no input)";
        }

        var recorded = entry.Inputs.Where(input => !Own(input.Path, settled)).ToDictionary(input => input.Path, input => input.Sha256, StringComparer.Ordinal);
        var now = current.Where(input => !Own(input.Path, settled)).ToList();
        var moved = now.Where(input => !recorded.TryGetValue(input.Path, out var sha256) || sha256 != input.Sha256).Select(input => input.Path)
            .Concat(recorded.Keys.Except(now.Select(input => input.Path), StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
        return moved.Count == 0
            ? null
            : $"{moved.Count} of what it reads moved since it was drawn ({string.Join(", ", moved.Take(4))}{(moved.Count > 4 ? ", ..." : "")})";
    }

    private static bool Own(string input, IReadOnlyCollection<string> pairs) =>
        pairs.Any(pair => new[] { MappingProducts.Final, MappingProducts.Statuses }.Any(name =>
            input.StartsWith($"{ProductStore.EssenthosFolder}/{MappingProducts.Of(pair, name)}.", StringComparison.Ordinal)));

    private static List<string> FinalsRead(IEnumerable<ProductInput> inputs, string pair)
    {
        var prefix = $"{ProductStore.EssenthosFolder}/{MappingProducts.Folder}/";
        return
        [
            .. inputs.Select(input => input.Path)
                .Where(path => path.StartsWith(prefix, StringComparison.Ordinal) && path.Split('/') is [_, _, _, var name] && name.StartsWith(MappingProducts.Final + ".", StringComparison.Ordinal))
                .Select(path => path.Split('/')[2])
                .Where(other => other != pair)
                .Distinct(StringComparer.Ordinal),
        ];
    }
}

/// <summary>
/// A line of <c>mapping/redraw-holds</c>, kept by hand: a kept model-drawn file the store build leaves as it stands though what it
/// reads moved. A redraw read by hand and found wrong is held on the file it would replace, by the owner's word, until the work
/// named in <see cref="Until"/> fixes it; taking the line out lets the next build draw the file again.
/// </summary>
/// <param name="File">The file held: <c>composed</c> or <c>aligner</c>.</param>
/// <param name="Until">The work that lifts the hold.</param>
/// <param name="Why">Why the redraw is not taken, in words a person can check.</param>
internal sealed record RedrawHold(string Pair, string File, string Until, string Why)
{
    public static readonly ProductSchema<RedrawHold> Schema = new ProductSchema<RedrawHold>()
        .Text("pair", r => r.Pair)
        .Text("file", r => r.File)
        .Text("until", r => r.Until)
        .Text("why", r => r.Why)
        .Reads(row => new RedrawHold(row.Text(0).Trim(), row.Text(1).Trim(), row.Text(2).Trim(), row.Text(3).Trim()));

    public string Name => $"{Pair} {File}";
}
