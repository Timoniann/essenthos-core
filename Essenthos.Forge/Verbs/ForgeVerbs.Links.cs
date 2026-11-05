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
    private static async Task<int> RejectedRenderingVerb(ForgeRun forge, string[] args)
    {
        using var scope = forge.Scope();
        var db = scope.ServiceProvider.GetRequiredService<Essenthos.Core.Database.AppDbContext>();
        Console.WriteLine(await RejectedRenderings.Withdraw(db));
        Console.WriteLine(await scope.ServiceProvider.GetRequiredService<OwnRecordLoader>().Load(forge.Resources));
        Console.WriteLine(await scope.ServiceProvider.GetRequiredService<OwnReferenceLoader>().Load());
        Console.WriteLine($"{await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(db.Database, VerseNaming.Sql)} verse naming flags refreshed");
        return 0;
    }

    /// <summary>
    /// Alignment is computed once per pair of texts, not per request, so it is a batch run rather than
    /// part of the load. <c>--outside BHSA</c> trains on every verse the pair shares and writes only where
    /// BHSA has no verse: the King James's Apocrypha against the Greek it was translated from, and not its
    /// Old Testament.
    /// </summary>
    private static async Task<int> Align(ForgeRun forge, string[] args)
    {
        using var alignScope = forge.Scope();
        var pipeline = alignScope.ServiceProvider.GetRequiredService<AlignmentPipeline>();
        var confidence = Array.IndexOf(args, "--min");
        var alignOne = Identifier(args[1]);
        var alignTwo = Identifier(args[2]);
        forge.Logger.LogInformation("{Outcome}", await pipeline.Run(
            alignOne,
            alignTwo,
            AlignmentStore.Workspace($"{alignOne}-{alignTwo}"),
            confidence >= 0 && confidence + 1 < args.Length
                ? double.Parse(args[confidence + 1], CultureInfo.InvariantCulture)
                : null,
            args.Contains("--model") ? args[Array.IndexOf(args, "--model") + 1] : "ibm4",
            replace: args.Contains("--replace"),
            outsideSlug: Option(args, "--outside") is { } outside ? Identifier(outside) : null));
        await forge.Replay(alignScope, (from, to) => Between(from, to, alignOne, alignTwo));
        await forge.Tidy();
        return 0;
    }

    /// <summary>
    /// The second route to the same word, through a text whose own links to the target are stated.
    /// Russian against Hebrew is one hard hop; Russian against the King James is an easy one, and the
    /// King James against BHSA is not a hop at all.
    /// </summary>
    private static async Task<int> Compose(ForgeRun forge, string[] args)
    {
        var (composeFrom, composeVia, composeTo) = (args[1], args[2], args[3]);
        using var composeScope = forge.Scope();
        var composer = composeScope.ServiceProvider.GetRequiredService<CompositionPipeline>();
        // One middle text or two, named together: KJV,BSB.
        var composeVias = composeVia.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Identifier).ToList();
        var least = Array.IndexOf(args, "--min");

        // How often each combination of readings must have named the stated word to be written at all.
        var composePrecision = Option(args, "--precision") is { } bar
            ? double.Parse(bar, CultureInfo.InvariantCulture)
            : Admission.DefaultPrecision;
        var composeMinimum = least >= 0 && least + 1 < args.Length
            ? double.Parse(args[least + 1], CultureInfo.InvariantCulture)
            : AlignmentPipeline.DefaultMinimumConfidence;

        // A trial: the same three readings and the same merge, scored against what the corpus holds and
        // written nowhere. --books trains on a few canonical books alone, which is the cheap first look;
        // --explain <file> writes every answer each reading gave, for asking why a word was left bare.
        if (args.Contains("--dry-run"))
        {
            forge.Logger.LogInformation("\n{Report}", await composer.Measure(
                Identifier(composeFrom),
                composeVias,
                Identifier(composeTo),
                composeMinimum,
                Option(args, "--books") is { } composeBooks
                    ? composeBooks.Split(',').Select(int.Parse).ToHashSet()
                    : null,
                composePrecision,
                Option(args, "--explain"),
                args.Contains("--daughter"),
                Agreeing(args)));
            return 0;
        }

        forge.Logger.LogInformation("{Outcome}", await composer.Run(
            Identifier(composeFrom),
            composeVias,
            Identifier(composeTo),
            least >= 0 ? composeMinimum : null,
            composePrecision,
            args.Contains("--unmeasured"),
            args.Contains("--daughter"),
            Agreeing(args),
            args.Contains("--refresh")));
        await forge.Replay(composeScope, (from, to) => Between(from, to, Identifier(composeFrom), Identifier(composeTo)));
        await forge.Tidy();
        return 0;
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
        using var scoreScope = forge.Scope();
        var scorer = scoreScope.ServiceProvider.GetRequiredService<AlignmentPipeline>();
        var scoreOne = Identifier(args[1]);
        var scoreTwo = Identifier(args[2]);
        forge.Logger.LogInformation("\n{Report}", await scorer.Measure(
            scoreOne,
            scoreTwo,
            AlignmentStore.Workspace(
                $"{scoreOne}-{scoreTwo}{(args.Contains("--surface") ? "-surface" : string.Empty)}" +
                $"{(args.Contains("--suppletion") ? "-suppletion" : string.Empty)}"),
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
        using var anchorScope = forge.Scope();
        var scorer = anchorScope.ServiceProvider.GetRequiredService<AlignmentPipeline>();
        var anchorOne = Identifier(args[1]);
        var anchorTwo = Identifier(args[2]);
        var anchorModel = args.Contains("--model") ? args[Array.IndexOf(args, "--model") + 1] : "ibm4";
        var anchorFold = args.Contains("--fold") ? int.Parse(args[Array.IndexOf(args, "--fold") + 1]) : 0;
        forge.Logger.LogInformation("\n{Report}", await scorer.MeasureAnchors(
            anchorOne,
            anchorTwo,
            AlignmentStore.Workspace(
                $"{anchorOne}-{anchorTwo}-held-out-strong-anchors-{anchorModel}-fold-{anchorFold}"),
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
        using var syntaxScope = forge.Scope();
        var prior = syntaxScope.ServiceProvider.GetRequiredService<AlignmentPipeline>();
        var syntaxOne = Identifier(args[1]);
        var syntaxTwo = Identifier(args[2]);
        forge.Logger.LogInformation("\n{Report}", await prior.Diagnose(
            syntaxOne,
            syntaxTwo,
            AlignmentStore.Workspace($"{syntaxOne}-{syntaxTwo}"),
            args.Contains("--model") ? args[Array.IndexOf(args, "--model") + 1] : "ibm4",
            args.Contains("--stated")));
        return 0;
    }

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
    /// The names of each verse settled by spelling and order over links already written, so a pair need
    /// not be aligned again for it. Reports and writes nothing without --apply. --chapters 1:46,40:1
    /// reports those chapters apart, with examples; --books 1,13 reads those canonical books only.
    /// </summary>
    private static async Task<int> Names(ForgeRun forge, string[] args)
    {
        var (namesFrom, namesTo) = (args[1], args[2]);
        using var namesScope = forge.Scope();
        var chapters = (Option(args, "--chapters") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(chapter => chapter.Split(':'))
            .Select(parts => (int.Parse(parts[0]), int.Parse(parts[1])))
            .ToHashSet();
        forge.Logger.LogInformation("\n{Report}", await namesScope.ServiceProvider.GetRequiredService<NameListPass>().Run(
            Identifier(namesFrom),
            Identifier(namesTo),
            chapters,
            Option(args, "--books") is { } namesBooks ? namesBooks.Split(',').Select(int.Parse).ToHashSet() : null,
            args.Contains("--apply")));
        if (args.Contains("--apply"))
        {
            await forge.Replay(namesScope, (from, to) => Between(from, to, Identifier(namesFrom), Identifier(namesTo)));
        }

        return 0;
    }

    /// <summary>
    /// The names of a translation placed on the original's words naming the same person, place or
    /// people. Reports, and scores against the pair's stated links where it has any; writes nothing
    /// without --apply.
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
            args.Contains("--apply"),
            targetLemmas: TargetLemmas(forge, args, Identifier(namesTo))));
        if (args.Contains("--apply"))
        {
            await forge.Replay(namesScope, (from, to) => Between(from, to, Identifier(namesFrom), Identifier(namesTo)));
        }

        return 0;
    }

    /// <summary>
    /// <c>--target-lemmas glaux</c>: the Septuagint's words with no lemma read with GLAUx's leading one, whatever share
    /// of its evidence agrees, so every word whose form GLAUx knows has a lemma. A measurement; refused with --apply.
    /// </summary>
    private static Func<string, string?>? TargetLemmas(ForgeRun forge, string[] args, string target)
    {
        if (Option(args, "--target-lemmas") is not { } source)
        {
            return null;
        }

        if (source != "glaux" || target != Glaux.GlauxLemmaLoader.Septuagint || args.Contains("--apply"))
        {
            throw new ArgumentException(
                $"--target-lemmas takes glaux, for {Glaux.GlauxLemmaLoader.Septuagint} as the target, and only without --apply.");
        }

        return Glaux.GlauxLemmaLoader.Leading(Path.Combine(forge.Resources, "Glaux", "xml"));
    }

    /// <summary>
    /// The possessive a Slavic translation writes beside the word that renders a Hebrew word with a
    /// pronominal suffix, linked to the same Hebrew word by a rule. Reports and writes nothing without --apply.
    /// </summary>
    private static async Task<int> Possessives(ForgeRun forge, string[] args)
    {
        using var possessiveScope = forge.Scope();
        forge.Logger.LogInformation("\n{Report}", await possessiveScope.ServiceProvider.GetRequiredService<PossessivePass>().Run(
            Identifier(args[1]), Identifier(args[2]), args.Contains("--apply")));
        return 0;
    }

    /// <summary>
    /// The aligner's links that put a pronoun or particle on a Hebrew word another word of the verse,
    /// not beside it, renders, withdrawn. Reports and writes nothing without --apply.
    /// </summary>
    private static async Task<int> Unshare(ForgeRun forge, string[] args)
    {
        using var unshareScope = forge.Scope();
        forge.Logger.LogInformation("\n{Report}", await unshareScope.ServiceProvider.GetRequiredService<SharedWordPass>().Run(
            Identifier(args[1]), Identifier(args[2]), args.Contains("--apply")));
        return 0;
    }
}
