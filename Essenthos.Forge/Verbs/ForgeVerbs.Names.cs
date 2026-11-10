using System.Diagnostics;
using Essenthos.Core.Names;
using Essenthos.Core.Products;
using Npgsql;

namespace Essenthos.Core.Verbs;

/// <summary>The names layer as files (TSK-1448, DOC-0224 §7): settled names kept by hand, projected onto every text, loaded.</summary>
internal static partial class ForgeVerbs
{
    private static IReadOnlyList<string> NamedTexts(NamesProjection projection, string[] args) =>
        Option(args, "--texts") is { } listed && listed != "all"
            ? [.. listed.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(projection.Texts.Contains)]
            : [.. projection.Texts];

    /// <summary>Projects the names onto every text, or the ones named, where an input moved: <c>names-project [--texts A,B] [--from] [--force] [--parallel n]</c>.</summary>
    private static Task<int> NamesVerb(ForgeRun forge, string[] args)
    {
        var clock = Stopwatch.StartNew();
        var projection = new NamesProjection(new ProductStore(Option(args, "--from") ?? forge.Resources));
        var texts = NamedTexts(projection, args);
        var outcomes = projection.WriteAll(texts, args.Contains("--force"), Parallelism(args, 16));
        foreach (var outcome in outcomes)
        {
            Console.WriteLine($"{outcome.Text,-14} {(outcome.Made ? "made" : "current"),-8} {outcome.Rows,9} names {outcome.Elapsed.TotalSeconds,7:0.00} s");
        }

        Console.WriteLine($"{outcomes.Count(o => o.Made)} made, {outcomes.Count(o => !o.Made)} current, {outcomes.Sum(o => o.Rows)} names, " +
                          $"slowest {outcomes.DefaultIfEmpty().Max(o => o?.Elapsed ?? TimeSpan.Zero).TotalSeconds:0.00} s; {texts.Count} texts in {clock.Elapsed}");
        return Task.FromResult(0);
    }

    /// <summary>
    /// Puts the projection into the word_entity of the database the Forge is pointed at (never live, never the snapshot), then
    /// reads it back by key against the products: <c>names-load [--texts A,B] [--from]</c>.
    /// </summary>
    private static async Task<int> NamesLoadVerb(ForgeRun forge, string[] args)
    {
        var database = new NpgsqlConnectionStringBuilder(forge.DatabaseConnection).Database;
        if (database is "essenthos_core" or "essenthos_core_rework_src")
        {
            Console.Error.WriteLine($"names-load writes word_entity; it is not run against {database}.");
            return 2;
        }

        var projection = new NamesProjection(new ProductStore(Option(args, "--from") ?? forge.Resources));
        var texts = NamedTexts(projection, args).Where(t => projection.Store.Exists(NameProducts.Projected(t))).ToList();
        var outcome = await NamesLoad.Run(forge.DatabaseConnection, projection.Store, texts, CancellationToken.None);
        Console.WriteLine($"{outcome.Rows} names of {outcome.Texts} texts loaded in {outcome.Load}, indexes in {outcome.Indexes}; " +
                          $"{outcome.UnknownRecords} on a record the database lacks, {outcome.UnknownWords} on a word without an id");
        var back = outcome.ReadBack!;
        Console.WriteLine($"read back by key: {back.Rows} rows; {back.MissingFromTable} of the products missing and {back.ExtraInTable} extra");
        return 0;
    }
}
