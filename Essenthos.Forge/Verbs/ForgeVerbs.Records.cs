using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Verbs;

/// <summary>Our records, facts, languages and external datasets as files (TSK-1447, DOC-0226).</summary>
internal static partial class ForgeVerbs
{
    /// <summary>
    /// Writes the record files from the database the Forge is pointed at, into <c>--into &lt;Resources&gt;</c>
    /// (default the Forge's own Resources/). Re-runnable: the export of cut-over day is this verb again.
    /// </summary>
    private static async Task<int> RecordsExport(ForgeRun forge, string[] args)
    {
        var resources = Option(args, "--into") ?? forge.Resources;
        var oursOnly = new Dictionary<string, RecordFiles.OwnValues>(StringComparer.Ordinal);
        if (!await RecordFiles.Loaded(forge.DatabaseConnection, CancellationToken.None))
        {
            using var scope = forge.Scope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplied = await db.Entities.Where(e => e.Source.StartsWith("BibleData")).Select(e => e.Slug).ToListAsync();
            foreach (var (slug, own) in await OursOnlyRecords.Among(db, supplied, CancellationToken.None))
            {
                oursOnly[slug] = new RecordFiles.OwnValues(own.Sex, own.Tribe, own.Line);
            }
        }

        var store = new ProductStore(resources);
        foreach (var (product, rows) in await RecordFiles.Export(forge.DatabaseConnection, store, oursOnly, CancellationToken.None))
        {
            Console.WriteLine($"{rows,9}  {product}");
        }

        Console.WriteLine($"{oursOnly.Count} records a dataset supplied written as the reader sees them.");
        return 0;
    }

    /// <summary>Puts the record files of <c>--from &lt;Resources&gt;</c> into the database the Forge is pointed at.</summary>
    private static async Task<int> RecordsLoad(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        foreach (var (product, rows) in await RecordFiles.Load(forge.DatabaseConnection, store, CancellationToken.None))
        {
            Console.WriteLine(rows < 0 ? $"{"-",9}  {product}: not loaded, what it stands on is not in this database" : $"{rows,9}  {product}");
        }

        foreach (var (dataset, confirmed, rows, records) in await RecordFiles.DatasetVerses(forge.DatabaseConnection, CancellationToken.None))
        {
            Console.WriteLine($"{rows,9}  {dataset} verse references on {records} records " +
                              (confirmed ? "a word of ours confirms (the record's own verse is served)" : "no word of ours confirms: not served"));
        }

        return 0;
    }

    /// <summary>
    /// Makes the derived record files whose inputs are all files, reading the hand files of
    /// <c>--from &lt;Resources&gt;</c> and writing into <c>--into &lt;Resources&gt;</c> (both default to the
    /// Forge's own); the gazetteer is read from OpenBible/ beside the hand files. Reads no database.
    /// </summary>
    private static Task<int> RecordsDerive(ForgeRun forge, string[] args)
    {
        var from = new ProductStore(Option(args, "--from") ?? forge.Resources);
        var into = Option(args, "--into") is { } path ? new ProductStore(path) : from;
        foreach (var (product, rows) in RecordProducers.Derive(from, into, Path.Combine(from.ResourcesPath, "OpenBible")))
        {
            Console.WriteLine($"{rows,9}  {product}");
        }

        into.Save();
        return Task.FromResult(0);
    }

    /// <summary>Writes <c>records/canon</c> from the names projection and the frames of <c>--from &lt;Resources&gt;</c>; reads no database.</summary>
    private static Task<int> RecordsCanon(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        var outcome = RecordCanons.Write(store, args.Contains("--force"));
        Console.WriteLine(outcome.Written
            ? $"{outcome.Rows,9}  {RecordCanons.Product} ({outcome.Records} records; {outcome.Names} names read, {outcome.Unplaced} in a book no canon names)"
            : $"{RecordCanons.Product} is current");
        store.Save();
        return Task.FromResult(0);
    }

    /// <summary>
    /// Applies the owner's decisions waiting on the files step to the record files of <c>--from &lt;Resources&gt;</c>
    /// (default the Forge's own), where his log is too; reads no database.
    /// </summary>
    private static Task<int> OwnerApply(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        var dryRun = args.Contains("--dry-run");
        OwnerDecisions.Outcome outcome;
        try
        {
            outcome = OwnerDecisions.Apply(store, dryRun, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException)
        {
            Console.Error.WriteLine(exception.Message);
            return Task.FromResult(1);
        }

        Console.WriteLine($"{outcome.Waiting} lines of the owner's log waited on the files step: {outcome.Records} known-for decisions.");
        Console.WriteLine($"{outcome.Added,9}  written into the files{(dryRun ? " (dry run: nothing written)" : string.Empty)}");
        Console.WriteLine($"{outcome.Removed,9}  taken out of them");
        Console.WriteLine($"{outcome.Already,9}  already as decided");
        foreach (var slug in outcome.Held)
        {
            Console.WriteLine($"  {slug}: KnownFor.json holds it, and a console answer does not override that file; left as it is");
        }

        foreach (var unplaced in outcome.Unplaced)
        {
            Console.WriteLine($"  {unplaced}; nothing written");
        }

        foreach (var product in outcome.Written)
        {
            Console.WriteLine($"  wrote {product}");
        }

        foreach (var line in outcome.Unhandled)
        {
            Console.WriteLine($"  line {line}: this step does not apply that section; it keeps waiting");
        }

        Console.WriteLine(outcome.Recorded
            ? $"Appended the apply line for '{OwnerChanges.Files}' to {OwnerChanges.FileName}."
            : $"No apply line appended{(outcome.Waiting == 0 ? ": nothing waited" : string.Empty)}.");
        return Task.FromResult(outcome.Unhandled.Count > 0 ? 1 : 0);
    }

    /// <summary>
    /// Applies the decided rows (<c>--rows &lt;file&gt;</c>, repeatable, else every file the Forge ships in <see cref="OwnerRows.Folder"/>)
    /// to the names, dataset and record files of <c>--from &lt;Resources&gt;</c> (default the Forge's own); reads no database. Exit 1
    /// when a row is refused or a file is missing, and then nothing is written.
    /// </summary>
    private static Task<int> OwnerRowsVerb(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        var dryRun = args.Contains("--dry-run");
        var files = args.Select((arg, i) => (arg, i)).Where(a => a.arg == "--rows" && a.i + 1 < args.Length).Select(a => args[a.i + 1]).ToList();
        var rows = files.Count > 0 ? OwnerRows.Read(files) : OwnerRows.Shipped();
        if (rows.Count == 0)
        {
            Console.Error.WriteLine($"No decided rows: none in {Path.Combine(AppContext.BaseDirectory, OwnerRows.Folder)} and no --rows given.");
            return Task.FromResult(1);
        }

        OwnerRows.Outcome outcome;
        try
        {
            outcome = OwnerRows.Apply(store, rows, dryRun, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException)
        {
            Console.Error.WriteLine(exception.Message);
            return Task.FromResult(1);
        }

        Console.WriteLine($"{outcome.Rows} decided rows read{(dryRun ? " (dry run: nothing written)" : string.Empty)}.");
        Console.WriteLine($"{outcome.Applied,9}  change a file");
        Console.WriteLine($"{outcome.Already,9}  already as decided");
        foreach (var (product, count) in outcome.Changed.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {count,7}  {product}");
        }

        foreach (var refused in outcome.Refused)
        {
            Console.WriteLine($"  refused: {refused}");
        }

        foreach (var product in outcome.Written)
        {
            Console.WriteLine($"  wrote {product}");
        }

        if (outcome.Refused.Count > 0)
        {
            Console.WriteLine($"{outcome.Refused.Count} rows refused: nothing written.");
        }

        return Task.FromResult(outcome.Refused.Count > 0 ? 1 : 0);
    }

    /// <summary>
    /// Compares two databases table by table by natural key: <c>records-compare &lt;port/db&gt; &lt;port/db&gt; --out &lt;file&gt;</c>,
    /// each on the Forge's server credentials.
    /// </summary>
    private static async Task<int> RecordsCompare(ForgeRun forge, string[] args)
    {
        string On(string at)
        {
            var builder = new NpgsqlConnectionStringBuilder(forge.DatabaseConnection);
            var slash = at.IndexOf('/');
            builder.Port = int.Parse(at[..slash], System.Globalization.CultureInfo.InvariantCulture);
            builder.Database = at[(slash + 1)..];
            return builder.ConnectionString;
        }

        var tables = RecordFiles.All.SelectMany(f => f.Tables).Distinct()
            .Concat(["entity_known_for_text", "entity_known_for_reference", "occupation_label", "person_occupation", "occupation_witness",
                     "occupation_word", "reign_statement", "reign_length", "reign_event", "prophet_field", "throne_name", "ruler_verdict_witness",
                     "ruler_verdict_passage", "stated_age", "entity_verse", "entity_image_caption", "event_date", "chronology", "period",
                     "commandment_reference", "topic_reference", "period_definition", "place_candidate"])
            .Distinct().ToList();
        var results = await RecordComparison.Compare(On(args[1]), On(args[2]), tables, CancellationToken.None);
        var report = RecordComparison.Report(results, 20);
        if (Option(args, "--out") is { } path)
        {
            await File.WriteAllTextAsync(path, report);
        }

        Console.WriteLine(report.Split("\n## ")[0]);
        return 0;
    }
}
