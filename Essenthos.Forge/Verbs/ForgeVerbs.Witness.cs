using System.Globalization;
using Essenthos.Core.Loading.Links.Evidentia;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;

namespace Essenthos.Core.Verbs;

internal static partial class ForgeVerbs
{
    private static async Task<int> EvidentiaWitnessVerb(ForgeRun forge, string[] args)
    {
        var pair = args[1];
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        var wordsStore = new ProductStore(Option(args, "--words") ?? store.ResourcesPath);
        var (from, _) = MappingProducts.Split(pair);
        var language = Loading.Links.Evidentia.EvidentiaSlavicLanguages.For(wordsStore, Indexes.IndexContext.Declared(store, from).Language)
            ?? throw new ArgumentException($"{from} is in no language the attached-word rules speak (ukr, rus).");
        var books = (Option(args, "--books") ?? throw new ArgumentException("evidentia-witness needs --books."))
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(part => part.Contains('-')
                ? Enumerable.Range(int.Parse(part.Split('-')[0], CultureInfo.InvariantCulture),
                    int.Parse(part.Split('-')[1], CultureInfo.InvariantCulture) - int.Parse(part.Split('-')[0], CultureInfo.InvariantCulture) + 1)
                : [int.Parse(part, CultureInfo.InvariantCulture)])
            .ToHashSet();
        using var scope = forge.Scope();
        var witness = new EvidentiaWitness(store, scope.ServiceProvider.GetRequiredService<UdpipeAnnotator>(), line => forge.Logger.LogInformation("{Line}", line));
        var report = await witness.Run(pair, language, books,
            Option(args, "--gold") == "strong" ? EvidentiaWitness.Key.Strong : EvidentiaWitness.Key.Stated, args.Contains("--held"), Option(args, "--heads") == "gold", Option(args, "--out"), Parallelism(args, 2), CancellationToken.None);
        Console.Write(report);
        return 0;
    }

    /// <summary><c>evidentia-second-witness</c>: the aligner beside names-first and a stored run's decisions, from files.</summary>
    private static Task<int> EvidentiaSecondWitnessVerb(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        var books = (Option(args, "--books") ?? throw new ArgumentException("evidentia-second-witness needs --books."))
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(part => part.Contains('-')
                ? Enumerable.Range(int.Parse(part.Split('-')[0], CultureInfo.InvariantCulture),
                    int.Parse(part.Split('-')[1], CultureInfo.InvariantCulture) - int.Parse(part.Split('-')[0], CultureInfo.InvariantCulture) + 1)
                : [int.Parse(part, CultureInfo.InvariantCulture)])
            .ToHashSet();
        var decisions = Option(args, "--decisions") is { } path ? EvidentiaSecondWitness.ReadDecisions(path) : null;
        Console.Write(new EvidentiaSecondWitness(store, line => forge.Logger.LogInformation("{Line}", line))
            .Run(args[1], books, decisions, Option(args, "--out")));
        return Task.FromResult(0);
    }
}
