using System.Globalization;
using Essenthos.Core.ClearBible;
using Essenthos.Core.Configuration;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Links;

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
        Console.WriteLine($"{await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(db.Database, DatasetLoader.NamingVerses)} verse naming flags refreshed");
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
            Path.Combine(Path.GetTempPath(), "essenthos-align", $"{alignOne}-{alignTwo}"),
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
            Agreeing(args)));
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
            Path.Combine(Path.GetTempPath(), "essenthos-align",
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
            Path.Combine(Path.GetTempPath(), "essenthos-align",
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
            Path.Combine(Path.GetTempPath(), "essenthos-align", $"{syntaxOne}-{syntaxTwo}"),
            args.Contains("--model") ? args[Array.IndexOf(args, "--model") + 1] : "ibm4",
            args.Contains("--stated")));
        return 0;
    }

    private static async Task<int> KingJamesGreek(ForgeRun forge, string[] args)
    {
        var witness = Identifier(args[1]);
        if (!DatasetLoader.GreekWitnesses.Contains(witness))
        {
            throw new InvalidOperationException(
                $"\"{witness}\" is not a King James Greek witness. Choose {string.Join(", ", DatasetLoader.GreekWitnesses)}.");
        }

        using var scope = forge.Scope();
        var outcome = await scope.ServiceProvider.GetRequiredService<NewTestamentLinkLoader>().Load(
            ResourcePaths.File(forge.Resources, "Zefania", "SF_2009-01-20_ENG_KJV_(KJV+).xml"), witness);
        forge.Logger.LogInformation("{Outcome}", outcome);
        if (!outcome.AlreadyLoaded)
        {
            forge.Logger.LogInformation("{Outcome}", await scope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());
            await forge.Tidy();
        }

        return 0;
    }

    /// <summary>
    /// A translation that arrived carrying its own Strong numbers, matched to a witness that carries
    /// them too — the one route Luther 1912 has to the originals that is not our own inference. It is a
    /// batch run for the same reason <c>align</c> is: which pairs are worth drawing is a judgement about the
    /// texts, and a text tagged in one series says nothing about the other.
    /// </summary>
    private static async Task<int> Strong(ForgeRun forge, string[] args)
    {
        using var strongScope = forge.Scope();
        var tagged = strongScope.ServiceProvider.GetRequiredService<TaggedTextLinkLoader>();
        var strongOutcome = await tagged.Load(Identifier(args[1]), Identifier(args[2]));
        forge.Logger.LogInformation("{Outcome}", strongOutcome);

        // The verse links for the pair just written. The load does this for pairs the alignment
        // commands leave behind, and a command that cannot be followed by a load has to do it itself:
        // without them every word link of a new pair reads as crossing a verse boundary nothing backs,
        // which is an integrity check the corpus keeps at zero. A pair already linked had its verse
        // links written by the run that linked it, and asking again costs half a minute.
        if (!strongOutcome.AlreadyLoaded)
        {
            forge.Logger.LogInformation(
                "{Outcome}", await strongScope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());
        }

        return 0;
    }

    /// <summary>
    /// The Synodal by Bob Jones University's Strong numbering, read from the edition and never stored:
    /// the links are written and the numbers are gone when the command returns. With no witness named it
    /// runs all five the numbering reaches.
    /// </summary>
    private static async Task<int> SynodalStrong(ForgeRun forge, string[] args)
    {
        using var synodalScope = forge.Scope();
        var editionPath = ResourcePaths.File(
            forge.Resources,
            SynodalStrongLinkLoader.EditionFile);
        string[] witnesses = args.Length > 1 ? [.. args[1..].Select(Identifier)] : SynodalStrongLinkLoader.Witnesses;

        await synodalScope.ServiceProvider.GetRequiredService<SynodalStrongLinkLoader>().Load(editionPath, witnesses);
        forge.Logger.LogInformation(
            "{Outcome}", await synodalScope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());

        // The links just written and the guesses just removed decide which Synodal words an annotation
        // reaches, and nothing on a restart asks that again.
        await synodalScope.ServiceProvider.GetRequiredService<AnnotationCarrier>().Carry();
        return 0;
    }

    /// <summary>
    /// The Chinese Union Version by the Strong numbers FHL put on it, read from the module and never
    /// stored, the way the Synodal's are. With no witness named it runs the three the numbering reaches.
    /// </summary>
    private static async Task<int> UnionStrong(ForgeRun forge, string[] args)
    {
        using var unionScope = forge.Scope();
        var modules = TaggedModules(forge.Resources);
        string[] unionWitnesses = args.Length > 1 ? [.. args[1..].Select(Identifier)] : UnionStrongLinkLoader.Witnesses;

        foreach (var outcome in await unionScope.ServiceProvider.GetRequiredService<UnionStrongLinkLoader>()
                     .Load(modules, unionWitnesses))
        {
            forge.Logger.LogInformation("{Outcome}", outcome);
        }

        forge.Logger.LogInformation(
            "{Outcome}", await unionScope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());
        await unionScope.ServiceProvider.GetRequiredService<AnnotationCarrier>().Carry();
        return 0;
    }

    private static async Task<int> ReinaValeraStrong(ForgeRun forge, string[] args)
    {
        using var scope = forge.Scope();
        string[] witnesses = args.Length > 1 ? [.. args[1..].Select(Identifier)] : ReinaValeraStrongLinkLoader.Witnesses;
        foreach (var outcome in await scope.ServiceProvider.GetRequiredService<ReinaValeraStrongLinkLoader>()
                     .Load(Path.Combine([forge.Resources, .. ReinaValeraStrongLinkLoader.EditionFolder]), witnesses))
        {
            forge.Logger.LogInformation("{Outcome}", outcome);
        }

        forge.Logger.LogInformation("{Outcome}", await scope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());
        await scope.ServiceProvider.GetRequiredService<AnnotationCarrier>().Carry();
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

    /// <summary>The CrossWire modules FHL tagged with Strong numbers, by text, each with its folder.</summary>
    private static Dictionary<string, string> TaggedModules(string resources) =>
        SwordTextSource.Texts.Values
            .Where(text => text.Segmentation == SwordSegmentation.Tagged)
            .ToDictionary(text => text.Definition.Slug, text => Path.Combine(resources, text.Folder));

    /// <summary>
    /// The Strong numbers on the other CrossWire modules, read and never stored as FHL's are: the
    /// Segond's laid onto the Segond loaded from eBible, and Darby's French, the Schlachter and the
    /// Revised Literal Translation onto the texts loaded from their own modules. The modules to run may
    /// follow the verb; with none named, every numbering runs against the witnesses it is declared with.
    /// </summary>
    private static async Task<int> CrossWireStrong(ForgeRun forge, string[] args)
    {
        using var crosswireScope = forge.Scope();
        await crosswireScope.ServiceProvider.GetRequiredService<CrossWireStrongLinkLoader>()
            .Load(forge.Resources, CrossWireStrongLinkLoader.Named(args[1..]));

        forge.Logger.LogInformation(
            "{Outcome}", await crosswireScope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());
        await crosswireScope.ServiceProvider.GetRequiredService<AnnotationCarrier>().Carry();
        await forge.Tidy();
        return 0;
    }

    /// <summary>
    /// The Chinese Union Version's Old Testament linked to BHSA by the Open Hebrew Bible's mapping,
    /// which names the BHS word each of FHL's spans renders. Where a link of FHL's numbers names the same
    /// words the mapping adds its claim to it. With --replace the numbers' links to BHSA are removed
    /// first and matched again after, so they leave to the mapping the words it states.
    /// </summary>
    private static async Task<int> OhbCuv(ForgeRun forge, string[] args)
    {
        using var ohbScope = forge.Scope();
        var ohbReplace = args.Contains("--replace");
        await ohbScope.ServiceProvider.GetRequiredService<OpenHebrewCuvLinkLoader>().Load(forge.Resources, ohbReplace);
        if (ohbReplace)
        {
            await ohbScope.ServiceProvider.GetRequiredService<UnionStrongLinkLoader>().Load(
                TaggedModules(forge.Resources),
                [BhsaTextSource.Slug]);
        }

        forge.Logger.LogInformation(
            "{Outcome}", await ohbScope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());
        await ohbScope.ServiceProvider.GetRequiredService<AnnotationCarrier>().Carry();
        await forge.Tidy();
        return 0;
    }

    /// <summary>
    /// The numbered links to the Hebrew matched again where the object marker made them, for a corpus
    /// whose numberings were laid before a bare marker stopped being a target (ObjectMarker). Every
    /// numbering is read as its own load reads it. Reports and writes nothing without --apply; the texts
    /// to repair may follow the verb, and every text a numbering links to the Hebrew is repaired without.
    /// Not in the recipe: a load draws these links by the same rule and has nothing to repair.
    /// </summary>
    private static async Task<int> ObjectMarker(ForgeRun forge, string[] args)
    {
        using var markerScope = forge.Scope();
        var markerTexts = args[1..].Where(argument => !argument.StartsWith("--", StringComparison.Ordinal))
            .Select(Identifier)
            .ToHashSet();
        var markerApply = args.Contains("--apply");

        forge.Logger.LogInformation("\n{Report}", await markerScope.ServiceProvider.GetRequiredService<ObjectMarkerRepair>().Run(
            ResourcePaths.File(forge.Resources, SynodalStrongLinkLoader.EditionFile),
            TaggedModules(forge.Resources),
            markerTexts.Count == 0 ? null : markerTexts,
            markerApply));
        if (markerApply)
        {
            forge.Logger.LogInformation(
                "{Outcome}", await markerScope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());
        }

        return 0;
    }

    /// <summary>
    /// The Door43 join a benchmark's Slavic answer key rests on, re-made from the files without writing
    /// anything: which verses, spans and words arrived, why the rest did not, and whether the links the
    /// corpus holds are still this join. With --replace the join is then written over the stored rows,
    /// which the load never does for a text it has already linked.
    /// </summary>
    private static async Task<int> InterlinearJoin(ForgeRun forge, string[] args)
    {
        var interlinearSlug = Identifier(args[1]);
        using var interlinearScope = forge.Scope();
        var interlinearLoader = interlinearScope.ServiceProvider.GetRequiredService<InterlinearLinkLoader>();
        var interlinearFolder = InterlinearFolder(forge.Resources, interlinearSlug);
        forge.Logger.LogInformation("\n{Report}", await interlinearLoader.Measure(interlinearFolder, interlinearSlug));

        if (args.Contains("--replace"))
        {
            forge.Logger.LogInformation("{Outcome}", await interlinearLoader.Replace(
                interlinearFolder, interlinearSlug, InterlinearLinkLoader.Interlinear(interlinearSlug).Source));

            // A stated link across a verse boundary the frame does not join is a verse pair the source
            // states, and a command that cannot be followed by a load has to write it itself.
            forge.Logger.LogInformation(
                "{Outcome}", await interlinearScope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());
            forge.Logger.LogInformation("\n{Report}", await interlinearLoader.Measure(interlinearFolder, interlinearSlug));
            await forge.Tidy();
        }

        return 0;
    }

    /// <summary>
    /// A stated mapping drawn again from its file, after a change to how the file is read. The rows that
    /// file wrote are withdrawn and loaded afresh, and the verse links and carried annotations are brought
    /// up to the links as they then stand. <c>redraw berean BHSA</c> for the tables' Hebrew half,
    /// <c>redraw clearbible BSB</c> for Clear Bible's sets on one translation. Redrawing the Berean against
    /// NESTLE1904 withdraws Clear Bible's claims on those links too, so the Clear Bible set goes after it.
    /// </summary>
    private static async Task<int> Redraw(ForgeRun forge, string[] args)
    {
        var redrawSource = args[1];
        using var redrawScope = forge.Scope();
        var slug = Identifier(args[2]);

        switch (redrawSource)
        {
            case "berean":
                var berean = redrawScope.ServiceProvider.GetRequiredService<BereanLinkLoader>();
                forge.Logger.LogInformation(
                    "Withdrew {Links} links the Berean tables wrote against {Witness}",
                    await berean.Withdraw(slug), slug);
                forge.Logger.LogInformation(
                    "{Outcome}",
                    await berean.Load(
                        ResourcePaths.File(forge.Resources, "Berean", "bsb_tables.tsv"), slug, LinkRulings.Read(forge.Resources)));
                break;

            case "clearbible":
                var clearBible = redrawScope.ServiceProvider.GetRequiredService<ClearBibleLinkLoader>();
                foreach (var set in ClearBibleSet.All().Where(set => set.From == slug))
                {
                    await clearBible.Withdraw(set);
                    forge.Logger.LogInformation(
                        "{Outcome}",
                        await clearBible.Load(Path.Combine(forge.Resources, "ClearBible"), set, LinkRulings.Read(forge.Resources)));
                }

                break;

            default:
                forge.Logger.LogError(
                    "Nothing is known to redraw from \"{Source}\". Name berean with a witness, as in `redraw berean "
                    + "BHSA`, or clearbible with a translation, as in `redraw clearbible BSB`", redrawSource);
                return 1;
        }

        // The Berean tables join the Berean to a witness; Clear Bible's sets join a translation to either original.
        await forge.Replay(redrawScope, redrawSource == "berean"
            ? (from, to) => Between(from, to, BereanTextSource.Slug, slug)
            : (from, to) => from == slug || to == slug);
        forge.Logger.LogInformation(
            "{Outcome}", await redrawScope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());
        await redrawScope.ServiceProvider.GetRequiredService<AnnotationCarrier>().Carry();
        await forge.Tidy();
        return 0;
    }

    /// <summary>
    /// Clear Bible's hand-made alignments, so a corpus already loaded gets them without a load.
    /// Idempotent per set, like the load's step it shares a loader with.
    /// </summary>
    private static async Task<int> ClearBibleVerb(ForgeRun forge, string[] args)
    {
        using var clearScope = forge.Scope();
        var clearBible = clearScope.ServiceProvider.GetRequiredService<ClearBibleLinkLoader>();
        var folder = Path.Combine(forge.Resources, "ClearBible");

        foreach (var set in ClearBibleSet.All())
        {
            forge.Logger.LogInformation("{Outcome}", await clearBible.Load(folder, set, LinkRulings.Read(forge.Resources)));
        }

        forge.Logger.LogInformation(
            "{Outcome}", await clearScope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());
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
