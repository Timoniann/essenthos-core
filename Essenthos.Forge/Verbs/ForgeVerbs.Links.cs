using System.Globalization;
using Essenthos.Core.ClearBible;
using Essenthos.Core.Configuration;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;

namespace Essenthos.Core.Verbs;

internal static partial class ForgeVerbs
{
    /// <summary>
    /// Alignment is computed once per pair of texts, not per request, so it is a run of its own rather than a step of the
    /// build: <c>align</c> writes the pair's <c>aligner</c> method file from the store's files, training the answer where none
    /// stored was made from the two texts' words as they stand (minutes of CPU per pair), which <c>aligner-pairs</c> in the
    /// build never does. A pair the aligner is declared over (<see cref="AlignerSource.Pairs"/>) runs as declared;
    /// <c>--outside BHSA</c> trains on every verse the pair shares and writes only where BHSA has no verse.
    /// </summary>
    private static Task<int> Align(ForgeRun forge, string[] args)
    {
        var (from, to) = (Identifier(args[1]), Identifier(args[2]));
        var declared = AlignerSource.Pairs.FirstOrDefault(pair => pair.From == from && pair.To == to) ?? new AlignedPair(from, to);
        var pair = declared with
        {
            Model = Option(args, "--model") ?? declared.Model,
            Minimum = Option(args, "--min") is { } min ? double.Parse(min, CultureInfo.InvariantCulture) : declared.Minimum,
            Outside = Option(args, "--outside") is { } outside ? Identifier(outside) : declared.Outside,
        };

        // --verses: only the rows of FROM words in these verses are written again, every other row of the file stays (PRB-1681).
        if (Option(args, "--verses") is { } named)
        {
            var verses = named.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(range => Products.VerseRange.Parse(range)).ToList();
            var store = new Products.ProductStore(Option(args, "--from") ?? forge.Resources);
            var outcome = PairSources.WriteVerses(new ProductTexts(store), MappingMethods.Aligner, pair.Name, verses,
                $"forge align {from} {to} --verses {string.Join(",", verses)}", line => forge.Logger.LogInformation("{Line}", line),
                [new AlignerSource([pair]) { Trains = true }]);
            store.Save();
            var written = outcome.Written!;
            forge.Logger.LogInformation(
                "{Pair} aligner: {Rows} rows; {Made} made in the verses ({Kept} shapes the file held there, {New} new, {Gone} gone); {Carried} rows outside them as they were",
                written.Pair, written.Rows, written.Rows - written.Carried, written.Kept, written.New, written.Gone, written.Carried);
            return Task.FromResult(0);
        }

        return WriteMethodFile(forge, args, MappingMethods.Aligner, pair.Name, new AlignerSource([pair]) { Trains = true });
    }

    /// <summary>
    /// The second route to the same word, through a text whose own links to the target are stated.
    /// Russian against Hebrew is one hard hop; Russian against the King James is an easy one, and the
    /// King James against BHSA is not a hop at all. Drawn from the store's files into the pair's <c>composed</c> method file,
    /// training the hops it lacks.
    /// </summary>
    private static async Task<int> Compose(ForgeRun forge, string[] args)
    {
        var (from, to) = (Identifier(args[1]), Identifier(args[3]));
        // One middle text or two, named together: KJV,BSB.
        var vias = args[2].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Identifier).ToList();

        // How often each combination of readings must have named the stated word to be written at all.
        var precision = Option(args, "--precision") is { } bar ? double.Parse(bar, CultureInfo.InvariantCulture) : Admission.DefaultPrecision;
        var minimum = Option(args, "--min") is { } least ? double.Parse(least, CultureInfo.InvariantCulture) : (double?)null;
        var store = new Products.ProductStore(Option(args, "--from") ?? forge.Resources);
        var texts = new ProductTexts(store);

        // A trial: the same three readings and the same merge, scored against what the corpus holds and written nowhere.
        // --books trains on a few canonical books alone, which is the cheap first look; --explain <file> writes every answer
        // each reading gave, for asking why a word was left bare.
        if (args.Contains("--dry-run"))
        {
            var aligner = new AlignerFiles(texts, new AlignmentPipeline(null, new LineLogger<AlignmentPipeline>(line => forge.Logger.LogInformation("{Line}", line))));
            var composer = new CompositionPipeline(texts, aligner, forge.Logger);
            forge.Logger.LogInformation("\n{Report}", await composer.Measure(
                from, vias, to, minimum ?? AlignmentPipeline.DefaultMinimumConfidence,
                Option(args, "--books") is { } books ? books.Split(',').Select(int.Parse).ToHashSet() : null,
                precision, Option(args, "--explain"), args.Contains("--daughter"), Agreeing(args), args.Contains("--refuse-unmeasured")));
            return 0;
        }

        // --verses: only the rows of FROM words in these verses are drawn again, every other row of the file stays; the pair
        // is drawn as its declaration says, so the new rows are made the way the carried ones were.
        if (Option(args, "--verses") is { } named)
        {
            var declared = ComposedSource.Pairs.FirstOrDefault(p => p.From == from && p.To == to && p.Vias.SequenceEqual(vias))
                ?? throw new ArgumentException($"compose --verses draws a declared composition, and none goes from {from} through {string.Join(",", vias)} to {to}.");
            var verses = named.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(range => Products.VerseRange.Parse(range)).ToList();
            var outcome = PairSources.WriteVerses(texts, MappingMethods.Composed, declared.Name, verses,
                $"forge compose {from} {string.Join(",", vias)} {to} --verses {string.Join(",", verses)}",
                line => forge.Logger.LogInformation("{Line}", line), [new ComposedSource([declared]) { Trains = true }]);
            store.Save();
            var written = outcome.Written!;
            forge.Logger.LogInformation(
                "{Pair} composed: {Rows} rows; {Made} made in the verses ({Kept} shapes the file held there, {New} new, {Gone} gone); {Carried} rows outside them as they were",
                written.Pair, written.Rows, written.Rows - written.Carried, written.Kept, written.New, written.Gone, written.Carried);
            return 0;
        }

        // --refuse-unmeasured: what one family of readings found is refused where nothing measured that combination (NOT-0228),
        // instead of written from the threshold. Off by default until the owner has seen what it removes (PRB-1607).
        var pair = new ComposedPair(from, vias, to, minimum, precision, args.Contains("--unmeasured"), args.Contains("--daughter"), Agreeing(args),
            args.Contains("--refuse-unmeasured"));
        return await WriteMethodFile(forge, args, MappingMethods.Composed, pair.Name, new ComposedSource([pair]) { Trains = true });
    }

    /// <summary>One pair's method file written by one source that may train its model, whatever the file was made from.</summary>
    private static Task<int> WriteMethodFile(ForgeRun forge, string[] args, string method, string pair, IPairSource source)
    {
        var store = new Products.ProductStore(Option(args, "--from") ?? forge.Resources);
        var outcome = PairSources.Write(new ProductTexts(store), method, pair, force: true,
            line => forge.Logger.LogInformation("{Line}", line), [source]);
        store.Save();
        forge.Logger.LogInformation("{Outcome}", outcome.ToString());
        return Task.FromResult(outcome.Written is null ? 1 : 0);
    }

    /// <summary>
    /// What a threshold costs, on the one pair where a source says what the right answer is. It reuses the
    /// alignment in the workspace, so a sweep is seconds once the model has been run. <c>--suppletion</c>
    /// scores the Slavic texts with the closed-class table switched on, which is how what that table is
    /// worth stays a measurement rather than an opinion. It gets its own workspace because the reduction
    /// changes the tokens the model trains on, and reusing the other one would score the wrong run.
    /// </summary>
    private static async Task<int> Score(ForgeRun forge, string[] args)
    {
        var scorer = Measures(forge, args);
        var scoreOne = Identifier(args[1]);
        var scoreTwo = Identifier(args[2]);
        forge.Logger.LogInformation("\n{Report}", await scorer.Measure(
            scoreOne,
            scoreTwo,
            $"{scoreOne}-{scoreTwo}{(args.Contains("--surface") ? "-surface" : string.Empty)}" +
            $"{(args.Contains("--suppletion") ? "-suppletion" : string.Empty)}",
            args.Contains("--min")
                ? [.. args[Array.IndexOf(args, "--min") + 1].Split(',')
                    .Select(t => double.Parse(t, CultureInfo.InvariantCulture))]
                : [0.25, 0.40],
            args.Contains("--model") ? args[Array.IndexOf(args, "--model") + 1] : "ibm4",
            args.Contains("--surface"),
            args.Contains("--stated"),
            args.Contains("--suppletion"),
            args.Contains("--pairs") ? args[Array.IndexOf(args, "--pairs") + 1] : null));
        return 0;
    }

    /// <summary>
    /// Unlike <c>score</c>, this is an out-of-sample test: only 80% of the stated and Strong one-to-one
    /// pairs reach SIL.Machine as its partial-alignment corpus, and a deterministic fifth of verses stays
    /// out of both that file and the source-stated score. The result is therefore an improvement measure,
    /// not the model repeating the key it was handed.
    /// </summary>
    private static async Task<int> ScoreAnchors(ForgeRun forge, string[] args)
    {
        var scorer = Measures(forge, args);
        var anchorOne = Identifier(args[1]);
        var anchorTwo = Identifier(args[2]);
        var anchorModel = args.Contains("--model") ? args[Array.IndexOf(args, "--model") + 1] : "ibm4";
        var anchorFold = args.Contains("--fold") ? int.Parse(args[Array.IndexOf(args, "--fold") + 1]) : 0;
        forge.Logger.LogInformation("\n{Report}", await scorer.MeasureAnchors(
            anchorOne,
            anchorTwo,
            $"{anchorOne}-{anchorTwo}-held-out-strong-anchors-{anchorModel}-fold-{anchorFold}",
            args.Contains("--min")
                ? [.. args[Array.IndexOf(args, "--min") + 1].Split(',')
                    .Select(t => double.Parse(t, CultureInfo.InvariantCulture))]
                : [0.25, 0.40],
            anchorModel,
            anchorFold));
        return 0;
    }

    /// <summary>
    /// What the target text's own syntax is worth as a check on the model, before it is believed: every
    /// proposal the model made, bucketed by how it sits among its neighbours' answers, against what a
    /// source states. The last column is the weight the rescorer uses, so revising it is a reading.
    /// </summary>
    private static async Task<int> Syntax(ForgeRun forge, string[] args)
    {
        var prior = Measures(forge, args);
        var syntaxOne = Identifier(args[1]);
        var syntaxTwo = Identifier(args[2]);
        forge.Logger.LogInformation("\n{Report}", await prior.Diagnose(
            syntaxOne,
            syntaxTwo,
            $"{syntaxOne}-{syntaxTwo}",
            args.Contains("--model") ? args[Array.IndexOf(args, "--model") + 1] : "ibm4",
            args.Contains("--stated")));
        return 0;
    }

    /// <summary>
    /// The measures of the aligner over the store's files (<c>--from</c>, or the configured Resources), training the answers they
    /// lack: <c>score</c>, <c>score-anchors</c> and <c>syntax</c> read no database (PRB-1589).
    /// </summary>
    private static AlignerMeasures Measures(ForgeRun forge, string[] args) =>
        new(new AlignerFiles(new ProductTexts(new Products.ProductStore(Option(args, "--from") ?? forge.Resources)),
            new AlignmentPipeline(null, new LineLogger<AlignmentPipeline>(line => forge.Logger.LogInformation("{Line}", line)))));

    private static async Task<int> ReinaValeraStrongScore(ForgeRun forge, string[] args)
    {
        using var scope = forge.Scope();
        foreach (var score in await scope.ServiceProvider.GetRequiredService<ReinaValeraStrongLinkLoader>()
                     .Score(Path.Combine([forge.Resources, .. ReinaValeraStrongLinkLoader.EditionFolder])))
        {
            forge.Logger.LogInformation("{Score}", score);
        }

        return 0;
    }

    /// <summary>
    /// The names of a translation placed on the original's words naming the same person, place or
    /// people, measured against the configured database's links and scored against the pair's stated links where it
    /// has any; it writes no database (names-pairs makes the links from files).
    /// </summary>
    private static async Task<int> NamesFirst(ForgeRun forge, string[] args)
    {
        var (namesFrom, namesTo) = (args[1], args[2]);
        using var namesScope = forge.Scope();
        var alone = Option(args, "--alone") is not { } chosen
            ? NamePolicy.Default.Alone
            : chosen.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Aggregate(NameSignal.None, (signals, signal) => signals | signal switch
            {
                "entity" => NameSignal.Entity,
                "letters" => NameSignal.Letters,
                "spelling" => NameSignal.Spelling,
                "none" => NameSignal.None,
                _ => throw new ArgumentException($"--alone takes entity, letters, spelling or none, not {signal}."),
            });
        forge.Logger.LogInformation("\n{Report}", await namesScope.ServiceProvider.GetRequiredService<NamesFirstPass>().Run(
            Identifier(namesFrom),
            Identifier(namesTo),
            Option(args, "--books") is { } namesBooks ? namesBooks.Split(',').Select(int.Parse).ToHashSet() : null,
            Option(args, "--names-from"),
            new NamePolicy(alone, Even: !args.Contains("--uneven"), Repeated: !args.Contains("--single")),
            args.Contains("--held"),
            Option(args, "--out"),
            targetLemmas: TargetLemmas(forge, args, Identifier(namesTo))));
        return 0;
    }

    /// <summary>
    /// <c>--target-lemmas glaux</c>: the Septuagint's words with no lemma read with GLAUx's leading one, whatever share
    /// of its evidence agrees, so every word whose form GLAUx knows has a lemma.
    /// </summary>
    private static Func<string, string?>? TargetLemmas(ForgeRun forge, string[] args, string target)
    {
        if (Option(args, "--target-lemmas") is not { } source)
        {
            return null;
        }

        if (source != "glaux" || target != Glaux.GlauxLemmas.Septuagint)
        {
            throw new ArgumentException(
                $"--target-lemmas takes glaux, for {Glaux.GlauxLemmas.Septuagint} as the target.");
        }

        return Glaux.GlauxLemmas.Leading(Path.Combine(forge.Resources, "Glaux", "xml"));
    }

    /// <summary>
    /// The possessive a Slavic translation writes beside the word that renders a Hebrew word with a
    /// pronominal suffix, that the configured database does not link to the same Hebrew word, counted; it writes nothing
    /// (possessive-pairs makes the links from files).
    /// </summary>
    private static async Task<int> Possessives(ForgeRun forge, string[] args)
    {
        using var possessiveScope = forge.Scope();
        forge.Logger.LogInformation("\n{Report}", await possessiveScope.ServiceProvider.GetRequiredService<PossessivePass>().Run(
            Identifier(args[1]), Identifier(args[2])));
        return 0;
    }

    /// <summary>
    /// The aligner's links that put a pronoun or particle on a Hebrew word another word of the verse,
    /// not beside it, renders, counted in the configured database; it writes nothing.
    /// </summary>
    private static async Task<int> Unshare(ForgeRun forge, string[] args)
    {
        using var unshareScope = forge.Scope();
        forge.Logger.LogInformation("\n{Report}", await unshareScope.ServiceProvider.GetRequiredService<SharedWordPass>().Run(
            Identifier(args[1]), Identifier(args[2])));
        return 0;
    }
}
