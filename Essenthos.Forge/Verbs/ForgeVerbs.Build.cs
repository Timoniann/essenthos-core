using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Build;
using Essenthos.Core.Corpus;
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
    /// <c>build [--into &lt;db&gt; [--seed &lt;db&gt;] [--replace]] [--apply] [--owner-rows [--rows &lt;file&gt;]...] [--level skip|essentials|all]
    /// [--parallel n] [--no-products]</c>: the owner's decided rows applied to the store when asked; every product whose inputs
    /// moved is made again by its producer, in the order the declared graph gives, and the graph is held against what each
    /// producer read; then a fresh database is filled from the products (<c>--into</c>), or the seed is cloned into it and takes
    /// what moved since the seed was built (<c>--into --seed</c>, the daily path), or the configured one takes what moved since it
    /// was built (<c>--apply</c>); then verify at the level asked; then <c>build/report-&lt;n&gt;</c>.
    /// </summary>
    private static async Task<int> BuildVerb(ForgeRun forge, string[] args)
    {
        var clock = Stopwatch.StartNew();
        var parallel = Parallelism(args, 16);
        var level = Option(args, "--level") ?? BuildVerify.Essentials;
        var stages = new List<BuildStage>();
        var into = Option(args, "--into");
        var seed = Option(args, "--seed");
        if (into is not null)
        {
            ServingBuild.Refuse(into);
        }

        if (seed is not null && into is null)
        {
            forge.Logger.LogError("--seed names the database cloned into the one --into names; give both");
            return 1;
        }

        var before = new ProductStore(forge.Resources);
        var missing = ProductGraph.Read(before).Missing(before);
        if (missing.Count > 0)
        {
            foreach (var product in missing)
            {
                forge.Logger.LogError("missing: {Product}", product);
            }

            forge.Logger.LogError("The store lacks {Count} product(s) no build producer makes. Copy them back from the backup of Resources/Essenthos "
                                  + "(E:); an exporter run again refuses a store that holds its products, and --force is for a store that has lost them all.",
                missing.Count);
            return 1;
        }

        if (args.Contains("--owner-rows"))
        {
            var deciding = Stopwatch.StartNew();
            string[] rows = ["owner-rows", .. args.Select((arg, i) => (arg, i)).Where(a => a.arg == "--rows" && a.i + 1 < args.Length)
                .SelectMany(a => new[] { "--rows", args[a.i + 1] })];
            var decided = await OwnerRowsVerb(forge, rows);
            stages.Add(new BuildStage("products", "owner rows", deciding.Elapsed, 0, decided == 0 ? "" : $"exit {decided}"));
            if (decided != 0)
            {
                forge.Logger.LogError("owner-rows failed with {Exit}: nothing built", decided);
                return 1;
            }
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

        if (into is not null && seed is not null)
        {
            var cloning = Stopwatch.StartNew();
            await DatabaseClone.Run(forge.DatabaseConnection, store, seed, into, args.Contains("--replace"), forge.Logger, CancellationToken.None);
            stages.Add(new BuildStage("database", $"clone of {seed}", cloning.Elapsed, 0));
            if (await Apply(forge, store, graph, level, stages) is not { } passed)
            {
                return 1;
            }

            verified = passed;
        }
        else if (into is not null)
        {
            var serving = new ServingBuild(forge.DatabaseConnection, store, forge.Scope().ServiceProvider, forge.Logger);
            await serving.Create(args.Contains("--replace"), CancellationToken.None);
            await serving.Fill(parallel, CancellationToken.None);

            // The whole verification reads nothing build/served holds, so it runs while the foreign keys are validated; the
            // essentials hold the database to build/served and find every key validated, so they wait for both. build/served is
            // written only once every key holds.
            var verify = Stopwatch.StartNew();
            var verifying = level == BuildVerify.Essentials
                ? null
                : BuildVerify.Run(forge.Scope().ServiceProvider, forge.DatabaseConnection, store, level, null, null, null, CorpusCheck.RenderedFloor,
                    forge.Logger, analyzed: true);
            await serving.Validating;
            var keys = await ForeignKeys(forge.DatabaseConnection);
            ServedState.Write(store, graph, serving.Database, serving.Rows, keys, $"forge build --into {serving.Database}");
            stages.AddRange(serving.Stages);
            verified = verifying is not null
                ? await verifying
                : await BuildVerify.Run(forge.Scope().ServiceProvider, forge.DatabaseConnection, store, level, null, null, null, CorpusCheck.RenderedFloor,
                    forge.Logger, analyzed: true);
            stages.Add(new BuildStage("verify", level, verify.Elapsed, 0, verified ? "passed" : "FAILED"));
        }
        else if (args.Contains("--apply"))
        {
            if (await Apply(forge, store, graph, level, stages) is not { } passed)
            {
                return 1;
            }

            verified = passed;
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

    /// <summary>
    /// What moved since the configured database was served, applied to it, then verified at <paramref name="level"/> when anything
    /// did; null when the apply refused.
    /// </summary>
    private static async Task<bool?> Apply(ForgeRun forge, ProductStore store, ProductGraph graph, string level, List<BuildStage> stages)
    {
        var apply = new BuildApply(forge.DatabaseConnection, store, graph, forge.Scope().ServiceProvider, forge.Logger);
        var applied = await apply.Run(CancellationToken.None);
        stages.AddRange(apply.Stages);
        if (applied is null)
        {
            return null;
        }

        if (applied.Changed.Count == 0)
        {
            return true;
        }

        var verify = Stopwatch.StartNew();
        var verified = await BuildVerify.Run(forge.Scope().ServiceProvider, forge.DatabaseConnection, store, level, null, null, applied.Pairs,
            CorpusCheck.RenderedFloor, forge.Logger);
        stages.Add(new BuildStage("verify", level, verify.Elapsed, 0, verified ? "passed" : "FAILED"));
        return verified;
    }

    /// <summary>
    /// <c>clone &lt;from&gt; &lt;to&gt; [--replace]</c>: a served database of the configured server copied whole into another, with its
    /// <c>build/served</c> state, so <c>build --apply</c> on the copy takes only what moved. Before a swap, the next database is
    /// cloned into the seed the next day's <c>build --into &lt;next&gt; --seed &lt;seed&gt;</c> starts from.
    /// </summary>
    private static async Task<int> CloneVerb(ForgeRun forge, string[] args)
    {
        await DatabaseClone.Run(forge.DatabaseConnection, new ProductStore(forge.Resources), args[1], args[2], args.Contains("--replace"), forge.Logger,
            CancellationToken.None);
        return 0;
    }

    /// <summary>Every build producer of the declared graph run once, in order, then the graph held against what was read.</summary>
    private static async Task<bool> BuildProducts(ForgeRun forge, List<BuildStage> stages)
    {
        var graph = ProductGraph.Read(new ProductStore(forge.Resources));
        foreach (var gone in graph.Forget(new ProductStore(forge.Resources)))
        {
            forge.Logger.LogInformation("products: {Product} is declared by no line and gone from the store; the manifest forgets it", gone);
        }

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

        var checkedStore = new ProductStore(forge.Resources);
        foreach (var outdated in graph.ForgetOutdatedServed(checkedStore))
        {
            forge.Logger.LogWarning("{Product} was made under an older line of {Graph}: the serving build makes it again", outdated, ProductGraph.Product);
        }

        var undeclared = graph.Check(checkedStore.Manifest);
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
            foreach (var row in reader.Rows().Where(r => r.Kind == MappingMerge.NoProducer || (r.Kind == "needs" && r.By.StartsWith("forge ", StringComparison.Ordinal))))
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

    /// <summary>
    /// <c>panes [&lt;text,...&gt;] [--force] [--parallel n]</c>: the panes of the configured database, read again where a
    /// chapter's inputs moved (every chapter of the texts named with <c>--force</c>), copied from the products in the configured
    /// Resources where they did not.
    /// </summary>
    private static async Task<int> PanesVerb(ForgeRun forge, string[] args)
    {
        ServingBuild.Refuse(new NpgsqlConnectionStringBuilder(forge.DatabaseConnection).Database ?? "");
        var clock = Stopwatch.StartNew();
        var panes = new PaneBuild(forge.DatabaseConnection, new ProductStore(forge.Resources), forge.Scope().ServiceProvider, forge.Logger);
        var outcomes = await panes.Run(Slugs(args), args.Contains("--force"), Parallelism(args, 16), CancellationToken.None);
        foreach (var outcome in outcomes)
        {
            Console.WriteLine(outcome.Made
                ? $"{outcome.Text,-14} {outcome.Panes,6} panes {outcome.Read,6} read {outcome.Copied,6} copied {outcome.Words,10} words {outcome.Bytes,12} bytes {outcome.Compressed,11} compressed {outcome.Elapsed.TotalSeconds,7:0.0} s"
                : $"{outcome.Text,-14} {outcome.Panes,6} panes  current");
        }

        var made = outcomes.Where(o => o.Made).ToList();
        Console.WriteLine($"{made.Count} texts changed, {outcomes.Count - made.Count} current: {made.Sum(o => o.Read)} panes read, {made.Sum(o => o.Copied)} rows copied "
                          + $"or deleted, {made.Sum(o => o.Words)} words, {made.Sum(o => o.Bytes)} bytes of JSON, {made.Sum(o => o.Compressed)} compressed, "
                          + $"in {clock.Elapsed.TotalSeconds:0.0} s");
        return 0;
    }

    /// <summary>
    /// <c>record-words [&lt;text,...&gt;] [--force]</c>: what a record page reads of the words of the configured database (the
    /// spellings of every text, the positions in the King James), for the texts whose inputs moved (every text named with
    /// <c>--force</c>), with their products in the configured Resources.
    /// </summary>
    private static async Task<int> RecordWordsVerb(ForgeRun forge, string[] args)
    {
        ServingBuild.Refuse(new NpgsqlConnectionStringBuilder(forge.DatabaseConnection).Database ?? "");
        var clock = Stopwatch.StartNew();
        var store = new ProductStore(forge.Resources);
        var services = forge.Scope().ServiceProvider;
        var texts = Slugs(args);
        var force = args.Contains("--force");
        var outcomes = await new RecordSpellingBuild(forge.DatabaseConnection, store, services, forge.Logger).Run(texts, force, CancellationToken.None);
        foreach (var outcome in outcomes)
        {
            Console.WriteLine($"{outcome.Text,-14} {outcome.Rows,8} spellings {(outcome.Made ? $"{outcome.Elapsed.TotalSeconds,7:0.0} s" : " current")}");
        }

        if ((texts is null || texts.Contains(RecordPositions.Text))
            && await new RecordPositionBuild(forge.DatabaseConnection, store, services, forge.Logger).Run(force, CancellationToken.None) is { } positions)
        {
            Console.WriteLine($"{positions.Text,-14} {positions.Rows,8} positions {(positions.Made ? $"{positions.Elapsed.TotalSeconds,7:0.0} s" : " current")}");
        }

        var made = outcomes.Where(o => o.Made).ToList();
        Console.WriteLine($"{made.Count} texts' spellings counted, {outcomes.Count - made.Count} current: {made.Sum(o => o.Rows)} rows, in {clock.Elapsed.TotalSeconds:0.0} s");
        return 0;
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
