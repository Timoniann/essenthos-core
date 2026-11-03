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

        return 0;
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
