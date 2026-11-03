using System.Collections.Concurrent;
using System.Diagnostics;
using Essenthos.Core.Products;
using Essenthos.Core.Conversion;
using Npgsql;

namespace Essenthos.Core.Verbs;

/// <summary>The verbs of the texts as converted products: convert, export, and the one-time ids and corrections.</summary>
internal static partial class ForgeVerbs
{
    private static IReadOnlyList<string> Slugs(string argument) =>
        argument.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? [.. TextCatalogue.All.Select(text => text.Slug)]
            : [.. argument.Split(',').Select(slug => TextCatalogue.Find(slug).Slug)];

    private static int Parallel(string[] args) =>
        int.TryParse(Option(args, "--parallel"), out var parallel) && parallel > 0 ? parallel : 1;

    private static async Task<int> ConvertVerb(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(forge.Resources);
        var slugs = Slugs(args[1]);
        var force = args.Contains("--force");
        var started = Stopwatch.StartNew();
        var failed = new ConcurrentBag<string>();
        await System.Threading.Tasks.Parallel.ForEachAsync(slugs, new ParallelOptions { MaxDegreeOfParallelism = Parallel(args) }, (slug, _) =>
        {
            try
            {
                forge.Logger.LogInformation("{Outcome}", TextConverter.Convert(store, slug, force));
            }
            catch (Exception exception)
            {
                failed.Add(slug);
                forge.Logger.LogError("{Slug}: {Message}", slug, exception.ToString());
            }

            return ValueTask.CompletedTask;
        });

        store.Save();
        forge.Logger.LogInformation("{Count} texts in {Seconds:0.0} s{Failed}", slugs.Count, started.Elapsed.TotalSeconds,
            failed.IsEmpty ? string.Empty : $"; failed: {string.Join(", ", failed.Order())}");
        return failed.IsEmpty ? 0 : 1;
    }

    private static async Task<int> TextsVerb(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(forge.Resources);
        switch (args[1])
        {
            case "ids":
                return await TextIds(forge, store, Slugs(args[2]));
            case "compare":
                return await CompareTexts(forge, store, Slugs(args[2]), Option(args, "--out"), Parallel(args));
            case "corrections":
                foreach (var slug in Slugs(args[2]))
                {
                    forge.Logger.LogInformation("{Outcome}", CorrectionSeeds.Write(store, slug));
                }

                store.Save();
                return 0;
            default:
                forge.Logger.LogError("forge texts ids|compare|corrections <text|all>");
                return 1;
        }
    }

    /// <summary>The ids every word of the texts holds in the configured database, written as each text's ids product.</summary>
    private static async Task<int> TextIds(ForgeRun forge, ProductStore store, IReadOnlyList<string> slugs)
    {
        var database = new NpgsqlConnectionStringBuilder(forge.DatabaseConnection).Database;
        foreach (var slug in slugs)
        {
            await using var connection = new NpgsqlConnection(forge.DatabaseConnection);
            await connection.OpenAsync();
            var ids = await TextSnapshot.ReadIds(connection, slug);
            using var writer = store.Create(TextWordId.ProductOf(slug), ProductKind.Machine, TextWordId.Schema(slug),
                ProductHeader.DescribeProducer($"forge texts ids {slug}"), [],
                header => header.Notes.Add(new("from", $"the word table of {database}")));
            writer.WriteAll(ids);
            forge.Logger.LogInformation("{Slug}: {Count} ids from {Database}", slug, ids.Count, database);
        }

        store.Save();
        return 0;
    }

    /// <summary>Each text's products held against the configured database by key; the differences are written per text.</summary>
    private static async Task<int> CompareTexts(ForgeRun forge, ProductStore store, IReadOnlyList<string> slugs, string? output, int parallel)
    {
        var folder = output ?? Path.Combine(forge.Resources, "Essenthos", "build", "comparison", "texts");
        var total = 0L;
        await System.Threading.Tasks.Parallel.ForEachAsync(slugs, new ParallelOptions { MaxDegreeOfParallelism = parallel }, async (slug, _) =>
        {
            await using var connection = new NpgsqlConnection(forge.DatabaseConnection);
            await connection.OpenAsync();
            var corpus = await TextSnapshot.Read(connection, slug);
            List<TextWord> words;
            using (var reader = store.Open(TextWord.ProductOf(slug), TextWord.Schema(slug)))
            {
                words = reader.ReadAll();
            }

            List<VerseManifestRow> verses;
            using (var reader = store.Open(VerseManifestRow.ProductOf(slug), VerseManifestRow.Schema))
            {
                verses = reader.ReadAll();
            }

            List<TextNote> notes;
            using (var reader = store.Open(TextNote.ProductOf(slug), TextNote.Schema))
            {
                notes = reader.ReadAll();
            }

            List<TextBook> books;
            using (var reader = store.Open(TextBook.ProductOf(slug), TextBook.Schema))
            {
                books = reader.ReadAll();
            }

            var differences = TextComparison.Compare(corpus, words, verses, notes, books);
            TextComparison.Write(Path.Combine(folder, $"{slug}.tsv"), differences);
            Interlocked.Add(ref total, differences.Count);
            forge.Logger.LogInformation("{Summary}", TextComparison.Summary(slug, differences));
        });

        forge.Logger.LogInformation("{Total} differences in {Count} texts; listed under {Folder}", total, slugs.Count, folder);
        return 0;
    }

    /// <summary><c>forge export &lt;text&gt; --as tsv|csv|jsonl</c>: a text's words in a common shape.</summary>
    private static Task<int> ExportText(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(forge.Resources);
        var slug = TextCatalogue.Find(args[1]).Slug;
        var shape = Option(args, "--as") ?? "tsv";
        var target = Option(args, "--to") ?? Path.Combine(Directory.GetCurrentDirectory(), $"{slug}-words.{shape}");
        var rows = TextExport.Write(store, slug, shape, target);
        forge.Logger.LogInformation("{Slug}: {Rows} words written to {Target} as {Shape}", slug, rows, target, shape);
        return Task.FromResult(0);
    }
}
