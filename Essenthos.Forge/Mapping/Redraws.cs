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
/// rulings. Its own pair's final and statuses are left out, or every merge would make it due again. Drawn again, it trains
/// what its hops lack (<see cref="Loading.Links.AlignmentPipeline.Trains"/>) and is held to the pair's floors by the merge
/// like any other file.
/// </para>
/// </summary>
internal static class Redraws
{
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
        var due = new List<Due>();
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
                var why = Lost(store, pair, file) ?? Moved(store, MappingProducts.Of(pair, file), pair, current);
                if (why is not null)
                {
                    due.Add(new Due(pair, file, why, FinalsRead(current, pair)));
                }
            }
        }

        return due;
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

    /// <summary>Which inputs moved since the file was drawn, but for its own pair's final and statuses; null when none did.</summary>
    internal static string? Moved(ProductStore store, string product, string pair, IReadOnlyList<ProductInput> current)
    {
        var path = store.Find(product);
        var entry = path is null ? null : store.Manifest.Get(path);
        if (entry is null || entry.Inputs.Count == 0)
        {
            return "it was never drawn from the store's files (exported, or the manifest names no input)";
        }

        var recorded = entry.Inputs.Where(input => !Own(input.Path, pair)).ToDictionary(input => input.Path, input => input.Sha256, StringComparer.Ordinal);
        var now = current.Where(input => !Own(input.Path, pair)).ToList();
        var moved = now.Where(input => !recorded.TryGetValue(input.Path, out var sha256) || sha256 != input.Sha256).Select(input => input.Path)
            .Concat(recorded.Keys.Except(now.Select(input => input.Path), StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
        return moved.Count == 0
            ? null
            : $"{moved.Count} of what it reads moved since it was drawn ({string.Join(", ", moved.Take(4))}{(moved.Count > 4 ? ", ..." : "")})";
    }

    private static bool Own(string input, string pair) =>
        new[] { MappingProducts.Final, MappingProducts.Statuses }.Any(name =>
            input.StartsWith($"{ProductStore.EssenthosFolder}/{MappingProducts.Of(pair, name)}.", StringComparison.Ordinal));

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
