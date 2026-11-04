using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Build;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using Essenthos.Core.Verification;
using Npgsql;

namespace Essenthos.Core.Verbs;

/// <summary>The build from products (DOC-0224 §13–14): the declared graph, a fresh serving database, an apply, verify levels.</summary>
internal static partial class ForgeVerbs
{
    /// <summary>
    /// The connection a run works on: <c>build --into &lt;db&gt;</c> works on that database of the configured server, so every
    /// service the build resolves (the frame, the spellings, verify) reads and writes the database it builds.
    /// </summary>
    public static string Target(string[] args, string connection) =>
        args is ["build", ..] && Option(args, "--into") is { } into
            ? new NpgsqlConnectionStringBuilder(connection) { Database = into }.ConnectionString
            : connection;

    /// <summary>
    /// <c>build [--into &lt;db&gt; [--replace]] [--apply] [--level skip|essentials|all] [--parallel n] [--no-products]</c>: every product
    /// whose inputs moved is made again by its producer, in the order the declared graph gives, and the graph is held against
    /// what each producer read; then a fresh database is filled from the products (<c>--into</c>), or the configured one
    /// takes what moved since it was built (<c>--apply</c>); then verify at the level asked; then <c>build/report-&lt;n&gt;</c>.
    /// </summary>
    private static async Task<int> BuildVerb(ForgeRun forge, string[] args)
    {
        var clock = Stopwatch.StartNew();
        var parallel = Parallelism(args, 16);
        var level = Option(args, "--level") ?? BuildVerify.Essentials;
        var stages = new List<BuildStage>();
        var into = Option(args, "--into");
        if (into is not null)
        {
            ServingBuild.Refuse(into);
        }

        if (!args.Contains("--no-products"))
        {
            var made = await BuildProducts(forge, stages);
            if (!made)
            {
                return 1;
            }
        }

        var store = new ProductStore(forge.Resources);
        var graph = ProductGraph.Read(store);
        stages.AddRange(MappingGaps(store));
        var verified = true;
        if (level != BuildVerify.Skip)
        {
            var checking = Stopwatch.StartNew();
            var checks = FileChecks.Run(store, parallel);
            foreach (var check in checks)
            {
                forge.Logger.Log(check.Found == 0 ? LogLevel.Information : LogLevel.Error, "file check: {Found} {Name}{Examples}", check.Found, check.Name,
                    check.Found == 0 ? "" : ": " + string.Join("; ", check.Examples));
            }

            stages.Add(new BuildStage("products", "file checks", checking.Elapsed, checks.Sum(c => c.Found),
                string.Join("; ", checks.Where(c => c.Found > 0).Select(c => $"{c.Found} {c.Name}"))));
            if (checks.Any(c => c.Found > 0))
            {
                WriteReport(store, stages, into);
                return 1;
            }
        }

        if (into is not null)
        {
            var serving = new ServingBuild(forge.DatabaseConnection, store, forge.Scope().ServiceProvider, forge.Logger);
            await serving.Create(args.Contains("--replace"), CancellationToken.None);
            await serving.Fill(parallel, CancellationToken.None);
            var keys = await ForeignKeys(forge.DatabaseConnection);
            ServedState.Write(store, graph, serving.Database, serving.Rows, keys, $"forge build --into {serving.Database}");
            stages.AddRange(serving.Stages);
            var verify = Stopwatch.StartNew();
            verified = await BuildVerify.Run(forge.Scope().ServiceProvider, forge.DatabaseConnection, store, level, null, null, null, CorpusCheck.RenderedFloor,
                forge.Logger);
            stages.Add(new BuildStage("verify", level, verify.Elapsed, 0, verified ? "passed" : "FAILED"));
        }
        else if (args.Contains("--apply"))
        {
            var apply = new BuildApply(forge.DatabaseConnection, store, graph, forge.Scope().ServiceProvider, forge.Logger);
            var applied = await apply.Run(CancellationToken.None);
            stages.AddRange(apply.Stages);
            if (applied is null)
            {
                return 1;
            }

            if (applied.Changed.Count > 0)
            {
                var verify = Stopwatch.StartNew();
                verified = await BuildVerify.Run(forge.Scope().ServiceProvider, forge.DatabaseConnection, store, level, null, null, applied.Pairs,
                    CorpusCheck.RenderedFloor, forge.Logger);
                stages.Add(new BuildStage("verify", level, verify.Elapsed, 0, verified ? "passed" : "FAILED"));
            }
        }

        stages.Add(new BuildStage("build", "whole", clock.Elapsed, 0, verified ? "" : "verify failed"));
        var report = WriteReport(store, stages, into ?? (args.Contains("--apply") ? new NpgsqlConnectionStringBuilder(forge.DatabaseConnection).Database : null));
        foreach (var stage in stages)
        {
            Console.WriteLine($"{stage.Stage,-9} {stage.What,-32} {stage.Elapsed.TotalSeconds,8:0.0} s {stage.Rows,12} {stage.Note}");
        }

        Console.WriteLine($"report: {report}");
        return verified ? 0 : 1;
    }

    /// <summary>Every build producer of the declared graph run once, in order, then the graph held against what was read.</summary>
    private static async Task<bool> BuildProducts(ForgeRun forge, List<BuildStage> stages)
    {
        var graph = ProductGraph.Read(new ProductStore(forge.Resources));
        foreach (var producer in graph.Order())
        {
            var arguments = producer.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var verb = Find(arguments[0]) ?? throw new InvalidDataException($"{ProductGraph.Product} names a producer no verb answers to: {producer}");
            var clock = Stopwatch.StartNew();
            forge.Logger.LogInformation("products: {Producer}", producer);
            var exit = await verb.Run(forge, arguments);
            stages.Add(new BuildStage("products", producer, clock.Elapsed, 0, exit == 0 ? "" : $"exit {exit}"));
            if (exit != 0)
            {
                forge.Logger.LogError("products: {Producer} failed with {Exit}", producer, exit);
                return false;
            }
        }

        var undeclared = graph.Check(new ProductStore(forge.Resources).Manifest);
        foreach (var read in undeclared)
        {
            forge.Logger.LogError("undeclared: {Read}", read);
        }

        stages.Add(new BuildStage("products", "declared graph", TimeSpan.Zero, undeclared.Count, undeclared.Count == 0 ? "every read declared" : "UNDECLARED READS"));
        return undeclared.Count == 0;
    }

    /// <summary>
    /// Every pair and method whose rows a changed verse left stale with nothing to make them again, from the pairs' merge
    /// reports, one stage each, then their total: a verse a correction unlinks is never a silent gap.
    /// </summary>
    private static List<BuildStage> MappingGaps(ProductStore store)
    {
        var stages = new List<BuildStage>();
        foreach (var pair in MappingProducts.Pairs(store))
        {
            var product = MappingProducts.Of(pair, MappingProducts.Report);
            if (!store.Exists(product))
            {
                continue;
            }

            using var reader = store.Open(product, ReportRow.Schema);
            foreach (var row in reader.Rows().Where(r => r.Kind is MappingMerge.NoProducer or "needs"))
            {
                stages.Add(new BuildStage("mapping", $"{pair} {row.Method}", TimeSpan.Zero, row.Rows,
                    row.Kind == MappingMerge.NoProducer ? $"no producer: {row.By}" : $"needs {row.By}"));
            }
        }

        stages.Add(new BuildStage("mapping", "rows lost to changed verses", TimeSpan.Zero, stages.Sum(s => s.Rows),
            stages.Count == 0 ? "none: every stale row has a producer that made it again" : $"{stages.Count} pair methods wait for a producer"));
        return stages;
    }

    private static async Task<int> ForeignKeys(string connection)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_constraint WHERE contype = 'f' AND connamespace = 'public'::regnamespace", db);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static string WriteReport(ProductStore store, List<BuildStage> stages, string? database)
    {
        var folder = Path.Combine(store.ResourcesPath, ProductStore.EssenthosFolder, "build");
        Directory.CreateDirectory(folder);
        var next = Directory.EnumerateFiles(folder, "report-*.tsv")
            .Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f)["report-".Length..], out var n) ? n : 0)
            .DefaultIfEmpty(0).Max() + 1;
        var product = $"build/report-{next}";
        using var writer = store.Create(product, ProductKind.Hand, BuildStage.Schema, ProductHeader.DescribeProducer("forge build"), [],
            header => header.Notes.Add(new("database", database ?? "none: products only")));
        foreach (var stage in stages)
        {
            writer.Write(stage);
        }

        writer.Complete();
        store.Save();
        return store.RelativePath(product, ProductKind.Hand);
    }

    /// <summary><c>tables [&lt;table,...&gt;] [--from &lt;Resources&gt;] [--force]</c>: the served tables made from files, each when its sources moved.</summary>
    private static Task<int> TablesVerb(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        var only = args.Skip(1).TakeWhile(arg => !arg.StartsWith("--", StringComparison.Ordinal))
            .SelectMany(arg => arg.Split(',', StringSplitOptions.RemoveEmptyEntries)).ToHashSet(StringComparer.Ordinal);
        foreach (var made in TableProducts.Write(store, only.Count == 0 ? null : only, args.Contains("--force")))
        {
            Console.WriteLine(made.Current
                ? $"tables/{made.Table}  current"
                : $"{made.Rows,10}  tables/{made.Table}  {made.Elapsed.TotalSeconds:0.0} s{(made.Note is null ? "" : "  " + made.Note)}");
        }

        store.Save();
        return Task.FromResult(0);
    }

    /// <summary><c>carried-export [--into &lt;Resources&gt;] [--all]</c>: the served tables no producer makes yet (every one with --all), from the configured database.</summary>
    private static async Task<int> CarriedExport(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(Option(args, "--into") ?? forge.Resources);
        foreach (var outcome in await CarriedTables.Export(forge.DatabaseConnection, store, CancellationToken.None, args.Contains("--all")))
        {
            Console.WriteLine($"{outcome.Exported,10}  carried/{outcome.Table}  {outcome.Elapsed.TotalSeconds:0.0} s");
        }

        return 0;
    }

    /// <summary>
    /// <c>verify [--level skip|essentials|all] [--only m,…] [--texts A,…] [--floor share]</c>; with no level, the whole
    /// verification as it always was.
    /// </summary>
    private static async Task<int> VerifyVerb(ForgeRun forge, string[] args)
    {
        static IReadOnlySet<string>? Set(string? listed) =>
            listed?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);

        var floor = Option(args, "--floor") is { } given ? double.Parse(given, CultureInfo.InvariantCulture) : CorpusCheck.RenderedFloor;
        var store = string.IsNullOrEmpty(forge.Resources) ? null : new ProductStore(forge.Resources);
        var passed = await BuildVerify.Run(forge.Scope().ServiceProvider, forge.DatabaseConnection, store,
            Option(args, "--level") ?? BuildVerify.All, Set(Option(args, "--only")), Set(Option(args, "--texts")), null, floor, forge.Logger);
        return passed ? 0 : 1;
    }
}
