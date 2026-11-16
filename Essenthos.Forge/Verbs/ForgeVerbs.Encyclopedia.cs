using System.Globalization;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Products;
using Essenthos.Core.Records;

namespace Essenthos.Core.Verbs;

/// <summary>
/// The encyclopedia's files that come from a source, made where their inputs moved, and the verbs that still read a
/// database to measure or to ask the owner. None of them writes a database: the encyclopedia's rows are the record files,
/// and <c>build --into</c> / <c>build --apply</c> load them.
/// </summary>
internal static partial class ForgeVerbs
{
    /// <summary>
    /// <c>occupations [--from &lt;Resources&gt;] [--force]</c>: the occupation record files from <c>person-occupations.json</c>,
    /// the records and the texts' words, frames, finals and Strong indexes, where an input moved. Reads no database.
    /// </summary>
    private static Task<int> Occupations(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        Console.WriteLine(OccupationFiles.Write(store, args.Contains("--force")));
        store.Save();
        return Task.FromResult(0);
    }

    /// <summary>
    /// How many Ge'ez words reach Dillmann's lexicon, read from its files against the loaded corpus;
    /// --sample &lt;file&gt; writes a hundred random matches out to be read by hand. Reads only.
    /// </summary>
    private static async Task<int> DillmannMeasure(ForgeRun forge, string[] args)
    {
        using var measureScope = forge.Scope();
        Console.WriteLine(await measureScope.ServiceProvider.GetRequiredService<GeezLexiconMeasure>()
            .Measure(Path.Combine(forge.Resources, "Dillmann"), Option(args, "--sample"), 100, CancellationToken.None));
        return 0;
    }

    /// <summary>
    /// <c>images [--from &lt;Resources&gt;]</c>: the pictures of the things whose 3D model changed, drawn from the model into
    /// <c>Images/</c>, for the things <c>records/records</c> holds, and the pictures' record files made again from the files
    /// as they now are, so a picture drawn anew is listed under the digest of its new bytes. A database takes them, with
    /// their new version tags, from the next <c>build --apply</c> or daily path; records-derive draws the same way in every build.
    /// </summary>
    private static async Task<int> Images(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        using var imagesScope = forge.Scope();
        Console.WriteLine(await imagesScope.ServiceProvider.GetRequiredService<ModelPictures>().Draw(store.ResourcesPath, store));
        foreach (var (product, rows) in RecordProducers.Images(store, store))
        {
            Console.WriteLine($"{rows,9}  {product}");
        }

        store.Save();
        return 0;
    }

    /// <summary>
    /// The records of the configured database matched to their Wikidata items, and the owner's list of the records the
    /// matching could not tell from a namesake written with the matching's ties beside his answers. records-derive makes
    /// <c>records/facts/wikipedia</c> from that list. Writes no database.
    /// </summary>
    private static async Task<int> Wikipedia(ForgeRun forge, string[] args)
    {
        using var wikipediaScope = forge.Scope();
        Console.WriteLine(await wikipediaScope.ServiceProvider.GetRequiredService<WikipediaLinkLoader>().Load(forge.Resources));
        return 0;
    }

    /// <summary><c>commandments [--from &lt;Resources&gt;] [--force]</c>: Maimonides' count as the files the build loads, where it moved. Reads no database.</summary>
    private static Task<int> Commandments(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        Console.WriteLine(CommandmentFiles.Write(store, args.Contains("--force")));
        store.Save();
        return Task.FromResult(0);
    }

    /// <summary><c>lands [--from &lt;Resources&gt;] [--force]</c>: PeriodO's periods of the lands as the files the build loads, where the dataset moved. Reads no database.</summary>
    private static Task<int> Lands(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        Console.WriteLine(PeriodOFiles.Write(store, args.Contains("--force")));
        store.Save();
        return Task.FromResult(0);
    }

    /// <summary>
    /// <c>cross-references [--from &lt;Resources&gt;] [--force] [--measure]</c>: OpenBible's and the Treasury's cross references
    /// and the parallels found in the originals, as the files the build loads, each where its inputs moved. --measure
    /// prints the parallel passages the originals give, and writes nothing. Reads no database.
    /// </summary>
    private static Task<int> CrossReferences(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        if (args.Contains("--measure"))
        {
            foreach (var line in Loading.CrossReferences.CrossReferenceFiles.MeasureParallels(store, forge.Logger))
            {
                Console.WriteLine(line);
            }

            return Task.FromResult(0);
        }

        foreach (var outcome in Loading.CrossReferences.CrossReferenceFiles.Write(store, args.Contains("--force"), forge.Logger))
        {
            Console.WriteLine(outcome);
        }

        store.Save();
        return Task.FromResult(0);
    }

    /// <summary><c>topics [--from &lt;Resources&gt;] [--force]</c>: Nave's topics as the files the build loads, where the index moved. Reads no database.</summary>
    private static Task<int> Topics(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        Console.WriteLine(NaveTopicFiles.Write(store, args.Contains("--force"), forge.Logger));
        store.Save();
        return Task.FromResult(0);
    }

    /// <summary>
    /// <c>names-consensus [--from &lt;Resources&gt;] [--force] [--parallel n] [--out &lt;folder&gt;] [--precision p] [--bar v,s,m]</c>:
    /// every text's <c>names/consensus/&lt;SLUG&gt;</c>, whom its verses say its words name, where an input moved, measured kind by
    /// kind against the texts whose names a stated link carries (<see cref="NameConsensusPass"/>). <c>--precision</c> sets what
    /// each kind's bar must reach and <c>--bar verses,score,margin</c> one bar for all; either measures only and writes no
    /// product. <c>--out</c> writes each text's findings. Reads no database.
    /// </summary>
    private static Task<int> NamesConsensus(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        var bar = Option(args, "--bar")?.Split(',') is [var barVerses, var barScore, var barMargin]
            ? new ConsensusBar(int.Parse(barVerses, CultureInfo.InvariantCulture), double.Parse(barScore, CultureInfo.InvariantCulture),
                double.Parse(barMargin, CultureInfo.InvariantCulture))
            : null;
        var precision = Option(args, "--precision") is { } asked ? double.Parse(asked, CultureInfo.InvariantCulture) : NameConsensusPass.Precision;
        var outcome = NameConsensusPass.Write(store, args.Contains("--force"), precision, bar, Option(args, "--out"), Parallelism(args, 4),
            line => forge.Logger.LogInformation("names-consensus: {Line}", line));
        forge.Logger.LogInformation("{Report}", outcome.Report);
        store.Save();
        return Task.FromResult(0);
    }
}
