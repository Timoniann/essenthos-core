using System.Globalization;
using System.Text;
using System.Text.Json;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Loading.Links.Evidentia;

namespace Essenthos.Core.Verbs;

/// <summary>
/// Every verb the Forge answers to, in the order <c>forge help</c> lists them. The dispatch, the help and
/// the recipe all read this one list, so a verb cannot be runnable and unlisted, or recorded under a name
/// the recipe does not know.
/// </summary>
internal static partial class ForgeVerbs
{
    private static bool Always(string[] args) => true;

    private static bool Applying(string[] args) => args.Contains("--apply");

    // The texts a run changed the links of, which it counts again as it ends (ForgeVerb.Relinks).
    private static Relinked Every(string[] args) => Relinked.Every;

    private static Relinked Pair(string[] args) => Relinked.Of(args[1], args[2]);

    private static Relinked AppliedPair(string[] args) => Applying(args) ? Pair(args) : Relinked.None;

    public static IReadOnlyList<ForgeVerb> All { get; } =
    [
        new("help", "", "This list.", Help),

        // The load, the corpus measures and the releases.
        new("load", "[--from <step>] [--steps]",
            "Load the corpus: every witness, the lexicon, the frame, the links, the encyclopedia and the verification pass.",
            Load),
        new("maintain", "[<table> ...]", "VACUUM (ANALYZE) of the tables that need it, or of the ones named.", Maintain),
        new("verify", "[--floor <share>]",
            "Measure the corpus, record what it finds for /v1/health, and fail on anything broken.", Verify),
        new("release", "[--allow-dirty] [--allow-unrecorded] [--dry-run]",
            "Verify this machine's corpus and dump it into .releases/ as the next release.", Release),
        new("publish", "--to <target> [--release <name>] [--without-rehearsal] [--dry-run]",
            "Restore a release on a target, verify it there and swap it in.", Publish),
        new("rollback", "--to <target> [--dry-run]", "Swap a target's live corpus with the previous release.", Rollback),
        new("releases", "[--on <target>]", "The releases this machine has built, and where each was published.", Releases),
        new("recipe", "[--run]", "List the recorded Forge runs the load replays, or run the ones the corpus lacks.", RecipeVerb,
            Relinks: args => args.Contains("--run") ? Relinked.Every : Relinked.None),
        new("reload", "<text>", "Read one text again from its source without the whole load.", Reload,
            Least: 1, Most: 1, Records: Always, Relinks: Every),
        new("correct", "", "Correct the King James, the Synodal and Ohienko against their editions in a loaded corpus.",
            Correct, Records: Always, Relinks: Every),
        new("marks", "", "Write what the editions print about their own words as word groups.", Marks),
        new("relations", "", "Make what each text was translated or revised from match the list kept by hand.", Relations),
        new("carry", "", "Carry every annotation a pass carried into another text again over the links as they stand.", Carry),

        // Links between texts.
        new("align", "<from> <to> [--min <confidence>] [--model <model>] [--replace] [--outside <text>]",
            "Align two texts with SIL's statistical aligner and write the result as aligner links.", Align,
            Least: 2, Records: Always, Relinks: Pair),
        new("compose", "<from> <via[,via]> <to> [--min <c>] [--precision <p>] [--agreeing <n>] [--daughter] [--unmeasured] [--dry-run]",
            "Compose a text's links to an original through aligned intermediates.", Compose,
            Least: 3, Records: Always, Relinks: args => Relinked.Of(args[1], args[3])),
        new("score", "<from> <to> [--min <c,...>] [--model <m>] [--surface] [--stated] [--suppletion] [--pairs <file>]",
            "What an aligner threshold costs, against the pair's stated links. Writes nothing.", Score, Least: 2),
        new("score-anchors", "<from> <to> [--min <c,...>] [--model <m>] [--fold <n>]",
            "The aligner scored out of sample with held-out Strong anchors. Writes nothing.", ScoreAnchors, Least: 2),
        new("syntax", "<from> <to> [--model <m>] [--stated]",
            "What the target's syntax is worth as a check on the aligner. Writes nothing.", Syntax, Least: 2),
        new("strong", "<from> <to>", "Link a Strong-tagged translation to a witness that carries the numbers too.",
            Strong, Least: 2, Records: Always, Relinks: Pair),
        new("kjv-greek", "<witness>", "Match the tagged King James to one of its five Greek witnesses.",
            KingJamesGreek, Least: 1, Most: 1, Relinks: args => Relinked.Of(Bible4uTextSource.KingJames, args[1])),
        new("synodal-strong", "[<witness> ...]", "Link the Synodal by Bob Jones University's Strong numbering.",
            SynodalStrong, Records: Always, Relinks: Every),
        new("union-strong", "[<witness> ...]", "Link the Chinese Union Version by FHL's Strong numbers.",
            UnionStrong, Records: Always, Relinks: Every),
        new("crosswire-strong", "[<module> ...]", "Link the texts CrossWire's Strong-numbered modules number.",
            CrossWireStrong, Records: Always, Relinks: Every),
        new("ohb-cuv", "[--replace]", "Link the Chinese Union Version's Old Testament to BHSA by the Open Hebrew Bible's mapping.",
            OhbCuv, Records: Always, Relinks: Every),
        new("object-marker", "[<text> ...] [--apply]", "Match again the numbered links to the Hebrew the object marker made.",
            ObjectMarker, Relinks: args => Applying(args) ? Relinked.Every : Relinked.None),
        new("interlinear-join", "<text> [--replace]", "Measure a Door43 interlinear's join, and with --replace write it again.",
            InterlinearJoin, Least: 1, Records: args => args.Contains("--replace"),
            Relinks: args => args.Contains("--replace") ? Relinked.Of(args[1]) : Relinked.None),
        new("redraw", "berean <witness> | clearbible <translation>", "Withdraw and draw again one stated mapping's links.",
            Redraw, Least: 2, Most: 2, Relinks: args => args[1] switch
            {
                "berean" => Relinked.Of(BereanTextSource.Slug, args[2]),
                "clearbible" => Relinked.Of(args[2]),
                _ => Relinked.None,
            }),
        new("clearbible", "", "Clear Bible's hand-made alignments, set by set.", ClearBibleVerb, Relinks: Every),
        new("names", "<from> <to> [--chapters b:c,...] [--books b,...] [--apply]",
            "Settle the names of each verse by spelling and order over links already written.", Names,
            Least: 2, Records: Applying, Relinks: AppliedPair),
        new("possessives", "<from> <to> [--apply]",
            "Link the possessive a Slavic text writes beside the word rendering a suffixed Hebrew word.", Possessives,
            Least: 2, Records: Applying, Relinks: AppliedPair),
        new("unshare", "<from> <to> [--apply]",
            "Withdraw the aligner's links that put a pronoun or particle on a word another word renders.", Unshare,
            Least: 2, Records: Applying, Relinks: AppliedPair),

        // EVIDENTIA.
        new("evidentia-preview", "<from> <to> <book> <chapter> [--verse <v>] [--without-source-strong] [--without-known-renderings]",
            "Read one chapter through EVIDENTIA's evidence graph. Writes nothing.", EvidentiaPreview, Least: 4),
        new("evidentia-measure", "<from> <to> <book> <chapter> [options]",
            "Measure EVIDENTIA on one chapter against an answer key. Writes nothing.", EvidentiaMeasure, Least: 4),
        new("evidentia-measure-book", "<from> <to> <book> [--from-chapter <c>] [--to-chapter <c>] [options]",
            "Measure EVIDENTIA on one book against an answer key. Writes nothing.", EvidentiaMeasureBook, Least: 3),
        new("evidentia-run", "<from> <to> <book[:chapters]> ... [--parallel <n>] [options]",
            "Store a run: a decision for every word of the books given.", EvidentiaRun, Least: 2),
        new("evidentia-runs", "", "The stored runs.", EvidentiaRuns),
        new("evidentia-queue", "<run> [--tier <t>] [--book <b>] [--chapter <c>] [--verse <v>] [--take <n>] [--kind <k,...>]",
            "What a stored run still waits on. Reads only.", EvidentiaQueue, Least: 1),
        new("evidentia-approve", "<decision> ... [--reviewer <name>] [--note <text>]", "Approve decisions of a stored run.",
            EvidentiaVerdict),
        new("evidentia-reject", "<decision> ... [--reviewer <name>] [--note <text>]", "Reject decisions of a stored run.",
            EvidentiaVerdict),
        new("evidentia-correct", "<decision> <word> [--reviewer <name>] [--note <text>]",
            "Correct a decision to another word.", EvidentiaCorrect, Least: 2),
        new("evidentia-accept-tier", "<run> [--tier <t>] [filters] [--reviewer <name>] [--note <text>]",
            "Accept one tier of a stored run unread.", EvidentiaAcceptTier, Least: 1),
        new("evidentia-apply", "<run> [--write]", "Write a run's accepted verdicts as links and claims; without --write, say what it would.",
            EvidentiaApply, Least: 1,
            Relinks: args => args.Contains("--write") && int.TryParse(args[1], out var run) ? Relinked.Run(run) : Relinked.None),
        new("evidentia-export", "[<run> ...]", "Write the verdicts to the ledger under Resources/Essenthos/evidentia.",
            EvidentiaExport),
        new("evidentia-replay", "", "Put the ledger's verdicts back into the corpus.", EvidentiaReplay, Relinks: Every),
        new("evidentia-problems", "<run,...> [--take <n>] [--min-words <n>] [--flagged]",
            "The verses a run did worst on.", EvidentiaProblems, Least: 1),
        new("evidentia-rerun", "<run> [--take <n>] [--min-words <n>]", "Run a stored run's worst verses again.",
            EvidentiaRerun, Least: 1),
        new("evidentia-compare", "<before> <after>", "Compare two stored runs.", EvidentiaCompare, Least: 2),

        // The encyclopedia's steps one at a time, for a corpus already loaded.
        new("locate", "", "Put the places on the map from OpenBible.", Locate),
        new("dillmann", "", "Load Dillmann's lexicon of Ge'ez.", Dillmann),
        new("dillmann-measure", "[--sample <file>]", "How many Ge'ez words reach Dillmann's lexicon. Reads only.",
            DillmannMeasure),
        new("images", "", "Load the pictures of people and places again.", Images),
        new("spell", "", "Count how each text spells each name.", Spell),
        new("cards", "", "Count how each text renders each Strong number: the lexicon's phrases and the entry page's reach.", Cards),
        new("commandments", "", "Load the 613 commandments.", Commandments),
        new("lands", "", "Load the periods of the lands from PeriodO.", Lands),
        new("name-forms", "", "Load the names the descriptions put into a case.", NameForms),
        new("reigns", "", "Load the kings, the rulers of the nations and the prophets of their days.", Reigns),
        new("own-records", "", "Apply the rulings on records, then write their lines in every language.", OwnRecords),
        new("distinguishers", "", "Write our own lines under our records in every language.", Distinguishers),
        new("naming", "", "Tell the listed verses that name their entity from those that concern it.", Naming),
        new("cross-references", "[--measure]", "Load the cross references and the detected parallels.", CrossReferences),
        new("topics", "", "Load Nave's topics.", Topics),
        new("persons", "", "Match the person register again.", Persons),
        new("fold-records", "", "Fold the records a dataset wrote twice for one person.", FoldRecords),
        new("name-consensus", "[--texts <t,...>] [--precision <p>] [--bar v,s,m] [--out <folder>] [--apply] [--replace]",
            "Name words by what the verses naming each entity share.", NameConsensus),
        new("context-bearers", "", "Settle a shared name by the bearer the rest of its book names.", ContextBearers),
        new("spelled-names", "", "Name a Greek name by the one record whose spelling it is.", SpelledNames),
        new("withdrawn-records", "", "Withdraw the records filed under a word that is no name.", WithdrawnRecords),
        new("titles", "", "Write the titles, their bearers and whose they are where they stand.", Titles),
        new("fixed-titles", "", "Write the titles the text fixes to one bearer.", FixedTitles),
        new("verse-readings", "", "Keep the verses two readings agree speak of a record.", VerseReadings),
        new("passage-readings", "", "Name the words two readings of a passage agree stand for a person.", PassageReadings),
        new("dataset-records", "", "Make the records a dataset supplied ours, fold them or withdraw them.", DatasetRecords),
    ];

    public static ForgeVerb? Find(string name) =>
        All.FirstOrDefault(verb => string.Equals(verb.Name, name, StringComparison.Ordinal));

    private static Task<int> Help(ForgeRun forge, string[] args)
    {
        var width = All.Max(verb => verb.Name.Length);
        var help = new StringBuilder();
        foreach (var verb in All)
        {
            help.Append("  ").Append(verb.Name.PadRight(width)).Append("  ").AppendLine(verb.Help);
            if (verb.Arguments.Length > 0)
            {
                help.Append("  ").Append(new string(' ', width)).Append("    ").AppendLine(verb.Usage);
            }
        }

        Console.Write(help);
        return Task.FromResult(0);
    }

    // A text identifier as the corpus spells it, from one typed at a shell in whatever case came to
    // hand. The pipelines compare it to the column, and the workspace they leave in the temp folder is
    // named after it: without this, `align kjv bhsa` finds no text, and on a file system that tells
    // KJV-BHSA from kjv-bhsa the same pair would train a second model beside the first.
    private static string Identifier(string typed) => typed.ToUpperInvariant();

    // --agreeing 3 writes only what the direct model and both middle texts all found.
    private static int Agreeing(string[] args) => Option(args, "--agreeing") is { } agreeing ? int.Parse(agreeing) : 1;

    private static string? Option(string[] args, string name) =>
        Array.IndexOf(args, name) is var at and >= 0 && at + 1 < args.Length ? args[at + 1] : null;

    private static bool Between(string from, string to, string one, string two) =>
        (from == one && to == two) || (from == two && to == one);

    // The arguments from a position up to the first option.
    private static IEnumerable<string> Positional(string[] arguments, int from) =>
        arguments.Skip(from).TakeWhile(argument => !argument.StartsWith("--", StringComparison.Ordinal));

    private static int? OptionalInt(string[] arguments, string option)
    {
        var index = Array.IndexOf(arguments, option);
        return index >= 0 && index + 1 < arguments.Length ? int.Parse(arguments[index + 1]) : null;
    }

    private static string? OptionalText(string[] arguments, string option)
    {
        var index = Array.IndexOf(arguments, option);
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }

    private static EvidentiaQueueFilter QueueFilter(string[] arguments) => new(
        Tier: OptionalText(arguments, "--tier"),
        Book: OptionalInt(arguments, "--book"),
        Chapter: OptionalInt(arguments, "--chapter"),
        Verse: OptionalInt(arguments, "--verse"),
        Take: OptionalInt(arguments, "--take") ?? EvidentiaReviewQueue.DefaultTake,
        Kinds: OptionalText(arguments, "--kind") is { } kinds
            ? [.. kinds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : null);

    // Contradicted proposals and per-word outcomes go to files rather than to the report, because they
    // are read one at a time and there are thousands of them: a classification pass or a comparison of
    // two runs needs the rows the aggregate counted, and a console report is neither a stable record of
    // them nor wide enough to hold a whole verse.
    private static async Task WriteRows<T>(string[] arguments, string option, IReadOnlyList<T> rows)
    {
        if (OptionalText(arguments, option) is not { } path)
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = false }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // A Door43 interlinear, by the text it aligns.
    private static string InterlinearFolder(string resourcesPath, string slug) => Path.Combine(
        resourcesPath,
        "Door43",
        InterlinearLinkLoader.Interlinear(slug).Folder);

    // A measurement is only worth the isolation it can state, so every one of these is a way of saying
    // which evidence the run was allowed to see and which answer key it was scored against.
    private static EvidentiaMeasurementOptions EvidentiaOptions(string[] arguments, string resourcesPath) => new(
        AllowSourceStrongEvidence: !arguments.Contains("--without-source-strong"),
        AllowKnownRenderingEvidence: !arguments.Contains("--without-known-renderings"),
        LearnRenderingsFrom: OptionalText(arguments, "--learn-from") is { } learnFrom ? Identifier(learnFrom) : null,
        LearnedRenderingMethods: arguments.Contains("--learn-from-strong-numbers")
            ? [LinkMethod.StatedBySource, LinkMethod.StrongNumber]
            : null,
        GoldSource: OptionalText(arguments, "--gold-source"),
        SampleSize: OptionalInt(arguments, "--sample") ?? 0,
        RecordDisagreements: OptionalText(arguments, "--disagreements") is not null,
        RecordWords: OptionalText(arguments, "--words") is not null,
        RecordKeyDoubts: OptionalText(arguments, "--key-doubts") is not null,
        GoldInterlinear: arguments.Contains("--gold-interlinear")
            ? InterlinearFolder(resourcesPath, Identifier(arguments[1]))
            : null,
        LearnAcrossLanguages: arguments.Contains("--learn-across-languages"),
        SourceFromFiles: arguments.Contains("--source-from-files"),
        RouteTexts: OptionalText(arguments, "--routes") is { } routes
            ? [.. routes.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Identifier)]
            : null,
        NeighbourVerseDistance: OptionalInt(arguments, "--neighbour-verses") ?? EvidentiaDefaults.NeighbourVerseDistance,
        EntityAnchors: arguments.Contains("--entity-anchors"),
        EntityNamesFrom: OptionalText(arguments, "--entity-names"),
        Learns: OptionalText(arguments, "--confirmed-out") is not null ? new EvidentiaConfirmedRenderings() : null,
        SecondPass: arguments.Contains("--second-pass"),
        ConfirmedByRuns: OptionalText(arguments, "--confirmed-by") is { } confirmedBy
            ? [.. confirmedBy.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(run => int.Parse(run, CultureInfo.InvariantCulture))]
            : null,
        ConfirmedByFiles: OptionalText(arguments, "--confirmed")?.Split(',', StringSplitOptions.RemoveEmptyEntries),
        AlignerLinks: arguments.Contains("--aligner-links"),
        AlignerPairs: OptionalText(arguments, "--aligner-pairs") is { } alignerPairs
            ? EvidentiaAlignerPairs.Read(alignerPairs.Split(',', StringSplitOptions.RemoveEmptyEntries))
            : null);
}
