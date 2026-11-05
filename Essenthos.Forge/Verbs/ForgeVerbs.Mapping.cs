using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using Npgsql;

namespace Essenthos.Core.Verbs;

/// <summary>The mapping as files: per pair and method, merged into a final, loaded and compared by key (TSK-1445, DOC-0224 §6).</summary>
internal static partial class ForgeVerbs
{
    private static IReadOnlySet<string>? Pairs(string[] args) =>
        Option(args, "--pairs") is { } pairs ? pairs.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal) : null;

    private static int Parallelism(string[] args, int fallback) =>
        Option(args, "--parallel") is { } n ? int.Parse(n, CultureInfo.InvariantCulture) : fallback;

    /// <summary>Writes every pair's method files from the database the Forge is pointed at, into <c>--into &lt;Resources&gt;</c>.</summary>
    private static async Task<int> MappingExportVerb(ForgeRun forge, string[] args)
    {
        var clock = Stopwatch.StartNew();
        var store = new ProductStore(Option(args, "--into") ?? forge.Resources);
        var pairs = await MappingExport.Run(forge.DatabaseConnection, store, Pairs(args), Parallelism(args, 3), forge.Logger, CancellationToken.None);
        foreach (var pair in pairs)
        {
            Console.WriteLine($"{pair.Pair,-24} {pair.Links,9} links  {string.Join("  ", pair.Rows.Select(r => $"{r.Key} {r.Value}"))}{(pair.Rulings > 0 ? $"  rulings {pair.Rulings}" : "")}");
        }

        Console.WriteLine($"{pairs.Count} pairs, {pairs.Sum(p => p.Links)} links, {pairs.Sum(p => p.Rows.Values.Sum())} rows in {clock.Elapsed}");
        return 0;
    }

    /// <summary>
    /// Writes a method's file of each pair named, or of every pair a source of the method speaks about, from those sources
    /// (<see cref="PairSources"/>), where an input moved or with <c>--force</c>.
    /// </summary>
    private static Task<int> SourcePairs(ForgeRun forge, string[] args, string method)
    {
        var clock = Stopwatch.StartNew();
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        var texts = new ProductTexts(store);
        var named = Positional(args, 1).Where(arg => arg.Contains('-')).ToHashSet(StringComparer.Ordinal);
        var pairs = PairSources.Pairs(texts, method, named.Count > 0 ? named : null);
        foreach (var pair in named.Where(p => !pairs.Contains(p)))
        {
            forge.Logger.LogWarning("{Pair}: no {Method} source this producer reads speaks about it, or the store lacks a text's words or frame", pair, method);
        }

        var results = new System.Collections.Concurrent.ConcurrentBag<PairSources.Outcome>();
        System.Threading.Tasks.Parallel.ForEach(pairs, new ParallelOptions { MaxDegreeOfParallelism = Parallelism(args, 12) }, pair =>
        {
            var outcome = PairSources.Write(texts, method, pair, args.Contains("--force"), line => forge.Logger.LogInformation("{Line}", line));
            results.Add(outcome);
            forge.Logger.LogInformation("{Outcome}", outcome.Current ? $"{pair} {method}: current" : outcome.Written!.ToString());
        });

        store.Save();
        Console.WriteLine($"{method}: {results.Count(r => !r.Current)} pairs written, {results.Count(r => r.Current)} current, in {clock.Elapsed}");
        return Task.FromResult(named.Count > 0 && named.Any(p => !pairs.Contains(p)) ? 1 : 0);
    }

    /// <summary>Compares the method files of another Resources folder with this one's by key, per pair, method and source.</summary>
    private static async Task<int> MappingDiff(ForgeRun forge, string[] args)
    {
        var before = new ProductStore(args[1]);
        var after = new ProductStore(Option(args, "--from") ?? forge.Resources);
        var pairs = Pairs(args);
        var methods = Option(args, "--methods")?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var work = MappingProducts.Pairs(after)
            .Where(pair => pairs is null || pairs.Contains(pair))
            .SelectMany(pair => MappingProducts.Methods(after, pair).Where(m => methods is null || methods.Contains(m)).Select(m => (pair, m)))
            .ToList();
        var outcomes = new System.Collections.Concurrent.ConcurrentBag<MethodFileComparison.Outcome>();
        System.Threading.Tasks.Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = Parallelism(args, 4) },
            item => outcomes.Add(MethodFileComparison.Compare(before, after, item.pair, item.m)));
        var groups = outcomes.SelectMany(o => o.Groups).OrderBy(g => g.Pair, StringComparer.Ordinal).ThenBy(g => g.Method, StringComparer.Ordinal)
            .ThenBy(g => g.Source, StringComparer.Ordinal).ToList();
        var summary = MethodFileComparison.Summary(groups);
        Console.Write(summary);
        if (Option(args, "--out") is { } folder)
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "summary.tsv"), summary);
            await File.WriteAllLinesAsync(Path.Combine(folder, "examples.tsv"),
                outcomes.SelectMany(o => o.Examples).Order(StringComparer.Ordinal));
        }

        Console.WriteLine($"{groups.Count(g => g.Same)} of {groups.Count} groups the same by key; rows equal {groups.Sum(g => g.Equal + g.IdOnly)}, "
                          + $"changed {groups.Sum(g => g.Changed)}, only before {groups.Sum(g => g.OnlyBefore)}, only after {groups.Sum(g => g.OnlyAfter)}");
        return 0;
    }

    /// <summary>Writes the verse links of the pairs named, or of every pair whose inputs moved.</summary>
    private static Task<int> VerseLinksVerb(ForgeRun forge, string[] args)
    {
        var clock = Stopwatch.StartNew();
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        var only = args.Skip(1).TakeWhile(arg => !arg.StartsWith("--", StringComparison.Ordinal))
            .SelectMany(arg => arg.Split(',', StringSplitOptions.RemoveEmptyEntries)).ToHashSet(StringComparer.Ordinal);
        var outcomes = VerseLinkFiles.Write(store, only.Count == 0 ? null : only, args.Contains("--force"), Parallelism(args, 8));
        foreach (var outcome in outcomes.Where(o => o.Written))
        {
            Console.WriteLine($"{outcome.Pair,-24} {outcome.Links,9} verse links");
        }

        Console.WriteLine($"{outcomes.Count(o => o.Written)} pairs written ({outcomes.Where(o => o.Written).Sum(o => o.Links)} verse links), "
                          + $"{outcomes.Count(o => !o.Written)} current, in {clock.Elapsed}");
        return Task.FromResult(0);
    }

    /// <summary>Merges one pair's method files, or every pair's, into its final and report.</summary>
    private static Task<int> Merge(ForgeRun forge, string[] args)
    {
        var clock = Stopwatch.StartNew();
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        var pairs = args.Contains("--all") ? MappingProducts.Pairs(store).Where(p => MappingProducts.Methods(store, p).Count > 0).ToList() : [args[1]];
        var results = new System.Collections.Concurrent.ConcurrentBag<MappingFiles.Merged>();
        System.Threading.Tasks.Parallel.ForEach(pairs, new ParallelOptions { MaxDegreeOfParallelism = Parallelism(args, 6) }, pair =>
        {
            var merged = MappingFiles.Merge(store, pair, args.Contains("--force"));
            results.Add(merged);
            forge.Logger.LogInformation("{Pair}: {Outcome} at {Elapsed}", pair, merged.Skipped ? "current" : $"{merged.Links} links, {merged.Claims} claims", clock.Elapsed);
        });

        store.Save();
        foreach (var merged in results.Where(r => !r.Skipped).OrderBy(r => r.Pair, StringComparer.Ordinal))
        {
            long Rows(string kind) => merged.Report.Where(r => r.Kind == kind).Sum(r => r.Rows);
            Console.WriteLine($"{merged.Pair,-24} {merged.Links,9} links {merged.Claims,9} claims {Rows("withdrawn"),7} withdrawn"
                              + $" {Rows("superseded"),7} superseded {Rows("refinements"),6} refinements {Rows("orphans"),5} orphans {Rows("bare-words"),5} bare {Rows("bare-words-left"),5} left"
                              + string.Concat(merged.Report.Where(r => r.Kind == "needs").Select(r => $"  needs {r.By}")));
        }

        Console.WriteLine($"{results.Count(r => !r.Skipped)} pairs merged, {results.Count(r => r.Skipped)} current, in {clock.Elapsed}");
        return Task.FromResult(0);
    }

    /// <summary>Puts every pair's final into the database the Forge is pointed at (never live).</summary>
    private static async Task<int> MappingLoadVerb(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        var outcome = await MappingLoad.Run(forge.DatabaseConnection, store, forge.Logger, CancellationToken.None);
        Console.WriteLine($"{outcome.Pairs} pairs: {outcome.Links} links, {outcome.Words} link words, {outcome.Claims} claims, " +
                          $"{outcome.Provenances} sources, {outcome.Unresolved} links with a key the database lacks, in {outcome.Elapsed}");
        return outcome.Unresolved == 0 ? 0 : 1;
    }

    /// <summary>
    /// Compares two databases' links by key: <c>mapping-compare &lt;port/db&gt; &lt;port/db&gt; --out &lt;folder&gt;</c>, each on the
    /// Forge's server credentials. Writes <c>summary.tsv</c> and <c>examples.md</c>.
    /// </summary>
    private static async Task<int> MappingCompare(ForgeRun forge, string[] args)
    {
        string On(string at)
        {
            var builder = new NpgsqlConnectionStringBuilder(forge.DatabaseConnection);
            var slash = at.IndexOf('/');
            builder.Port = int.Parse(at[..slash], CultureInfo.InvariantCulture);
            builder.Database = at[(slash + 1)..];
            return builder.ConnectionString;
        }

        var clock = Stopwatch.StartNew();
        var comparison = await MappingComparison.Run(On(args[1]), On(args[2]), Parallelism(args, 2), Pairs(args), forge.Logger, CancellationToken.None);
        var summary = comparison.Summary();
        if (Option(args, "--out") is { } folder)
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "summary.tsv"), summary);
            await File.WriteAllTextAsync(Path.Combine(folder, "examples.md"), comparison.Examples());
        }

        foreach (var (table, (before, after)) in comparison.Counts)
        {
            Console.WriteLine($"{table}: {before} -> {after}");
        }

        foreach (var kind in comparison.Groups.GroupBy(g => (g.Table, g.Kind)))
        {
            Console.WriteLine($"{kind.Key.Table} {Comparison.KeyedComparison.Spell(kind.Key.Kind)}: {kind.Sum(g => g.Rows)} rows in {kind.Count()} groups");
        }

        Console.WriteLine($"compared in {clock.Elapsed}");
        return 0;
    }

    /// <summary>
    /// A composition that reads its links from the mapping's finals under <c>--files &lt;Resources&gt;</c> and writes its
    /// answers to the pair's <c>composed</c> method file there; the words and the frame still come from the database
    /// the Forge is pointed at, which it never writes. Not recorded in the recipe: nothing in the database changes.
    /// </summary>
    private static async Task<int> ComposeFile(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(Option(args, "--files") ?? forge.Resources);
        using var scope = forge.Scope();
        var composer = scope.ServiceProvider.GetRequiredService<CompositionPipeline>();
        await using (var db = new NpgsqlConnection(forge.DatabaseConnection))
        {
            await db.OpenAsync();
            composer.Files = await MappingLinks.Open(store, db, CancellationToken.None);
        }

        var minimum = Option(args, "--min") is { } min ? double.Parse(min, CultureInfo.InvariantCulture) : (double?)null;
        var precision = Option(args, "--precision") is { } bar ? double.Parse(bar, CultureInfo.InvariantCulture) : Admission.DefaultPrecision;
        forge.Logger.LogInformation("{Outcome}", await composer.Run(
            Identifier(args[1]),
            [.. args[2].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Identifier)],
            Identifier(args[3]),
            minimum,
            precision,
            args.Contains("--unmeasured"),
            args.Contains("--daughter"),
            Agreeing(args)));
        store.Save();
        return 0;
    }
}
