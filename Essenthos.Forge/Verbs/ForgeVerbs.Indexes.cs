using System.Diagnostics;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Products;
using Npgsql;

namespace Essenthos.Core.Verbs;

/// <summary>The per-text indexes as files (TSK-1459, DOC-0224 §10): made from the words, the finals and the names, then loaded.</summary>
internal static partial class ForgeVerbs
{
    private static IReadOnlyList<string> IndexedTexts(IndexContext context, string[] args) =>
        Option(args, "--texts") is { } listed && listed != "all"
            ? [.. listed.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(context.Texts.ContainsKey)]
            : [.. context.Texts.Keys.Order(StringComparer.Ordinal)];

    private static IReadOnlySet<string>? IndexNames(string[] args) =>
        Option(args, "--only") is { } only ? only.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal) : null;

    /// <summary>Makes the indexes of the texts named, or of every text, where an input moved: <c>indexes [--texts A,B] [--only strong,forms] [--force]</c>.</summary>
    private static Task<int> IndexesVerb(ForgeRun forge, string[] args)
    {
        var clock = Stopwatch.StartNew();
        var context = new IndexContext(new ProductStore(Option(args, "--from") ?? forge.Resources));
        var texts = IndexedTexts(context, args);
        var outcomes = TextIndexes.Make(context, texts, IndexNames(args), args.Contains("--force"), Parallelism(args, 16));
        foreach (var outcome in outcomes)
        {
            Console.WriteLine($"{outcome.Text,-14} {outcome.Index,-12} {(outcome.Made ? "made" : "current"),-8} {outcome.Rows,9} rows {outcome.Elapsed.TotalSeconds,7:0.00} s  {outcome.Note}");
        }

        foreach (var index in outcomes.GroupBy(o => o.Index))
        {
            Console.WriteLine($"{index.Key}: {index.Count(o => o.Made)} made, {index.Count(o => !o.Made)} current, {index.Sum(o => o.Rows)} rows, " +
                              $"slowest {index.Max(o => o.Elapsed).TotalSeconds:0.00} s, summed {index.Sum(o => o.Elapsed.TotalSeconds):0.0} s");
        }

        Console.WriteLine($"{texts.Count} texts in {clock.Elapsed}");
        return Task.FromResult(0);
    }

    /// <summary>Puts the indexes into the database the Forge is pointed at (never live): <c>indexes-load [--texts A,B] [--only strong,forms,spellings]</c>.</summary>
    private static async Task<int> IndexesLoadVerb(ForgeRun forge, string[] args)
    {
        var database = new NpgsqlConnectionStringBuilder(forge.DatabaseConnection).Database;
        if (database is "essenthos_core" or "essenthos_core_rework_src")
        {
            Console.Error.WriteLine($"indexes-load writes the serving tables; it is not run against {database}.");
            return 2;
        }

        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        var context = new IndexContext(store);
        var texts = IndexedTexts(context, args);
        var only = IndexNames(args);
        if (only is null || only.Contains(IndexProducts.Strong))
        {
            Console.WriteLine(await IndexLoad.Strong(forge.DatabaseConnection, store, texts, CancellationToken.None));
        }

        if (only is null || only.Contains(IndexProducts.Forms))
        {
            Console.WriteLine(await IndexLoad.Forms(forge.DatabaseConnection, store, texts, CancellationToken.None));
        }

        if (only is null || only.Contains(IndexProducts.Spellings))
        {
            using var scope = forge.Scope();
            Console.WriteLine(await IndexLoad.Spellings(forge.DatabaseConnection, store,
                scope.ServiceProvider.GetRequiredService<EntityRenderingLoader>(), CancellationToken.None));
        }

        return 0;
    }

    /// <summary>Writes the database's names as <c>names/exported/&lt;SLUG&gt;</c>, for the spellings index until the names layer writes them.</summary>
    private static async Task<int> NamesExportVerb(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(Option(args, "--into") ?? forge.Resources);
        var builder = new NpgsqlConnectionStringBuilder(forge.DatabaseConnection);
        var outcome = await NamesExport.Run(forge.DatabaseConnection, store, $"{builder.Host}:{builder.Port}/{builder.Database}", CancellationToken.None);
        Console.WriteLine($"{outcome.Names} names of {outcome.Texts} texts in {outcome.Elapsed}");
        return 0;
    }
}
