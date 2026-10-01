using System.Globalization;
using Essenthos.Core.Database;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Verbs;

/// <summary>
/// The encyclopedia's steps one at a time, for a corpus already loaded: each is what the load does at its
/// own step, so a change reaches the corpus without the corpus being read again.
/// </summary>
internal static partial class ForgeVerbs
{
    /// <summary>Where the places are, for a corpus loaded before the gazetteer's points were.</summary>
    private static async Task<int> Locate(ForgeRun forge, string[] args)
    {
        using var locateScope = forge.Scope();
        var located = await locateScope.ServiceProvider.GetRequiredService<OpenBibleLocationLoader>()
            .Load(Path.Combine(forge.Resources, "OpenBible"));
        Console.WriteLine(located);
        return 0;
    }

    /// <summary>Dillmann's lexicon of Ge'ez, for a corpus loaded before it was.</summary>
    private static async Task<int> Dillmann(ForgeRun forge, string[] args)
    {
        using var dillmannScope = forge.Scope();
        Console.WriteLine(await dillmannScope.ServiceProvider.GetRequiredService<GeezLexiconLoader>()
            .Load(Path.Combine(forge.Resources, "Dillmann")));
        return 0;
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
    /// The pictures of people and places, drawn again from the images folder and its manifests, so a new
    /// portrait or a corrected credit arrives without reading the corpus again.
    /// </summary>
    private static async Task<int> Images(ForgeRun forge, string[] args)
    {
        using var imagesScope = forge.Scope();
        Console.WriteLine(await imagesScope.ServiceProvider.GetRequiredService<EntityImageLoader>().Load(forge.Resources));
        return 0;
    }

    /// <summary>How each text spells each name, counted again from the words that name it.</summary>
    private static async Task<int> Spell(ForgeRun forge, string[] args)
    {
        using var spellScope = forge.Scope();
        Console.WriteLine(await spellScope.ServiceProvider.GetRequiredService<EntityRenderingLoader>().Load());
        return 0;
    }

    /// <summary>The phrases the lexicon quotes under each entry, counted again from the links.</summary>
    private static async Task<int> Cards(ForgeRun forge, string[] args)
    {
        using var cardsScope = forge.Scope();
        Console.WriteLine(await cardsScope.ServiceProvider.GetRequiredService<StrongRenderingLoader>().Load());
        return 0;
    }

    /// <summary>The commandments, for a corpus loaded before they were.</summary>
    private static async Task<int> Commandments(ForgeRun forge, string[] args)
    {
        using var commandmentScope = forge.Scope();
        Console.WriteLine(await commandmentScope.ServiceProvider.GetRequiredService<CommandmentLoader>()
            .Load(Path.Combine([AppContext.BaseDirectory, .. CommandmentLoader.FilePath])));
        return 0;
    }

    /// <summary>The periods of the lands from PeriodO, for a corpus loaded before they were.</summary>
    private static async Task<int> Lands(ForgeRun forge, string[] args)
    {
        using var landsScope = forge.Scope();
        Console.WriteLine(await landsScope.ServiceProvider.GetRequiredService<PeriodOLoader>().Load(forge.Resources));
        return 0;
    }

    /// <summary>
    /// What the event files state that a corpus loaded before they were read does not hold, Ussher's
    /// years from his Annals among it.
    /// </summary>
    private static async Task<int> Restate(ForgeRun forge, string[] args)
    {
        using var restateScope = forge.Scope();
        Console.WriteLine(await restateScope.ServiceProvider.GetRequiredService<EventRestatementLoader>().Load(
            Path.Combine(forge.Resources, "BibleData2026"),
            Path.Combine(AppContext.BaseDirectory, "Resources", "WorldHistory")));
        return 0;
    }

    /// <summary>The names a clause puts into a case, from name-form files written after the corpus was loaded.</summary>
    private static async Task<int> NameForms(ForgeRun forge, string[] args)
    {
        using var nameFormScope = forge.Scope();
        Console.WriteLine(await nameFormScope.ServiceProvider.GetRequiredService<EntityNameFormLoader>().Load(forge.Resources));
        return 0;
    }

    /// <summary>The kings, the rulers of the nations and the prophets of their days, for a corpus loaded before they were.</summary>
    private static async Task<int> Reigns(ForgeRun forge, string[] args)
    {
        using var reignScope = forge.Scope();
        Console.WriteLine(await reignScope.ServiceProvider.GetRequiredService<ReignLoader>().Load());
        return 0;
    }

    /// <summary>
    /// The rulings on records the encyclopedia holds, for a corpus loaded before the credit of a record a
    /// ruling re-heads moved to the ruling; then the lines in every reader's language, which follow the
    /// credit.
    /// </summary>
    private static async Task<int> OwnRecords(ForgeRun forge, string[] args)
    {
        using var ownScope = forge.Scope();
        Console.WriteLine(await ownScope.ServiceProvider.GetRequiredService<OwnRecordLoader>().Load(forge.Resources));
        Console.WriteLine(await ownScope.ServiceProvider.GetRequiredService<DistinguisherLoader>().Load());
        return 0;
    }

    /// <summary>The lines this corpus wrote under its own records, in every reader's language.</summary>
    private static async Task<int> Distinguishers(ForgeRun forge, string[] args)
    {
        using var distinguisherScope = forge.Scope();
        Console.WriteLine(await distinguisherScope.ServiceProvider.GetRequiredService<DistinguisherLoader>().Load());
        return 0;
    }

    /// <summary>Which listed verses name their entity and which only concern it, told apart again.</summary>
    private static async Task<int> Naming(ForgeRun forge, string[] args)
    {
        using var namingScope = forge.Scope();
        await TellTheNamingVerses(namingScope);
        return 0;
    }

    private static async Task TellTheNamingVerses(IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var changed = await db.Database.ExecuteSqlRawAsync(DatasetLoader.NamingVerses);
        Console.WriteLine($"{changed} listed verses changed between naming their entity and only concerning it");
    }

    /// <summary>
    /// The cross references and the detected parallels, for a corpus loaded before they were. --measure
    /// prints the parallel passages the originals give, and writes nothing.
    /// </summary>
    private static async Task<int> CrossReferences(ForgeRun forge, string[] args)
    {
        using var referenceScope = forge.Scope();
        var references = referenceScope.ServiceProvider
            .GetRequiredService<Loading.CrossReferences.CrossReferenceLoader>();
        if (args.Contains("--measure"))
        {
            foreach (var line in await references.MeasureParallels())
            {
                Console.WriteLine(line);
            }

            return 0;
        }

        foreach (var outcome in await references.Load(forge.Resources))
        {
            Console.WriteLine(outcome);
        }

        return 0;
    }

    private static async Task<int> Topics(ForgeRun forge, string[] args)
    {
        using var topicScope = forge.Scope();
        Console.WriteLine(await topicScope.ServiceProvider.GetRequiredService<NaveTopicLoader>()
            .Load(Path.Combine(forge.Resources, "BibleData2026")));
        return 0;
    }

    /// <summary>
    /// The person register on a corpus already built: the verses a dataset filed under the wrong man put
    /// back, and the bearers of those names matched again with the verses where they now stand. Run it
    /// before fold-records, which folds what it leaves twice.
    /// </summary>
    private static async Task<int> Persons(ForgeRun forge, string[] args)
    {
        using var personScope = forge.Scope();
        Console.WriteLine(await personScope.ServiceProvider.GetRequiredService<PersonRegisterLoader>().Load(forge.Resources));
        return 0;
    }

    /// <summary>The records a dataset wrote twice for one person, folded into one.</summary>
    private static async Task<int> FoldRecords(ForgeRun forge, string[] args)
    {
        using var foldScope = forge.Scope();
        Console.WriteLine(await foldScope.ServiceProvider.GetRequiredService<DuplicateRecordLoader>().Load());
        return 0;
    }

    /// <summary>
    /// The words naming every person, place, people, title, thing and appointed time in every text, read
    /// off the words the verses naming it share rather than off a link, measured kind by kind against the
    /// texts whose annotations a stated link carried. <c>--texts</c> limits the texts, <c>--precision</c> sets
    /// what each kind's bar must reach on the measured texts, <c>--bar verses,score,margin</c> sets one bar
    /// for all, <c>--out</c> writes each text's findings. Writes nothing without --apply, which annotates the
    /// words no annotation names and lists the disagreements for review; --replace takes back an earlier
    /// run's first. The load runs it once on a cold corpus, so it is not in the recipe.
    /// </summary>
    private static async Task<int> NameConsensus(ForgeRun forge, string[] args)
    {
        using var consensusScope = forge.Scope();
        var consensusBar = Option(args, "--bar")?.Split(',') is [var barVerses, var barScore, var barMargin]
            ? new ConsensusBar(int.Parse(barVerses), double.Parse(barScore, CultureInfo.InvariantCulture),
                double.Parse(barMargin, CultureInfo.InvariantCulture))
            : null;
        forge.Logger.LogInformation("\n{Report}", await consensusScope.ServiceProvider.GetRequiredService<NameConsensusPass>().Run(
            Option(args, "--texts")?.Split(',').Select(Identifier).ToHashSet(),
            Option(args, "--precision") is { } consensusPrecision
                ? double.Parse(consensusPrecision, CultureInfo.InvariantCulture)
                : NameConsensusPass.Precision,
            Option(args, "--out"),
            consensusBar,
            args.Contains("--apply"),
            args.Contains("--replace"),
            forge.Resources));
        return 0;
    }

    /// <summary>
    /// A name several records bear, printed where nothing settled which of them it is, settled by the one
    /// of them the rest of its book names, and the verses read off the words again. Reports the rule's
    /// held-out precision first; writes once, and lists what the dataset or a carried word disputes.
    /// </summary>
    private static async Task<int> ContextBearers(ForgeRun forge, string[] args)
    {
        using var bearerScope = forge.Scope();
        forge.Logger.LogInformation("{Outcome}", await bearerScope.ServiceProvider.GetRequiredService<ContextBearerLoader>()
            .Load(forge.Resources));
        forge.Logger.LogInformation("{Outcome}", await bearerScope.ServiceProvider.GetRequiredService<OwnReferenceLoader>()
            .Load());
        return 0;
    }

    /// <summary>
    /// A Greek name no record is held under the number of, named as the one record whose own Greek spelling
    /// it is, carried across the links, and the verses read off the words again.
    /// </summary>
    private static async Task<int> SpelledNames(ForgeRun forge, string[] args)
    {
        using var spelledScope = forge.Scope();
        forge.Logger.LogInformation("{Outcome}", await spelledScope.ServiceProvider.GetRequiredService<SpelledNameLoader>()
            .Load());
        forge.Logger.LogInformation("{Outcome}", await spelledScope.ServiceProvider.GetRequiredService<OwnReferenceLoader>()
            .Load());
        return 0;
    }

    /// <summary>
    /// The records a dataset filed under a word that is no name, withdrawn where nothing of ours stands on
    /// them; a listed record that carries something of ours is kept and named.
    /// </summary>
    private static async Task<int> WithdrawnRecords(ForgeRun forge, string[] args)
    {
        using var withdrawnScope = forge.Scope();
        forge.Logger.LogInformation("{Outcome}", await withdrawnScope.ServiceProvider.GetRequiredService<WithdrawnRecordLoader>()
            .Load());
        return 0;
    }

    /// <summary>
    /// The titles the file holds: a title it has gained is written with its bearers and named at its words,
    /// the words a fixed title's rule newly reaches are given its bearer, the occurrences the rulings read
    /// are given theirs or left to the title alone, and the verses are read off the words again.
    /// </summary>
    private static async Task<int> Titles(ForgeRun forge, string[] args)
    {
        using var titlesScope = forge.Scope();
        forge.Logger.LogInformation("{Outcome}", await titlesScope.ServiceProvider.GetRequiredService<TitleLoader>()
            .Load());
        forge.Logger.LogInformation("{Outcome}", await titlesScope.ServiceProvider.GetRequiredService<FixedTitleLoader>()
            .Load());
        foreach (var read in await titlesScope.ServiceProvider.GetRequiredService<TitleReadingLoader>().LoadAll())
        {
            forge.Logger.LogInformation("{Outcome}", read);
        }

        forge.Logger.LogInformation("{Outcome}", await titlesScope.ServiceProvider.GetRequiredService<OwnReferenceLoader>()
            .Load());
        return 0;
    }

    /// <summary>
    /// A title the text fixes to one bearer — the devil, the Christ, the Son of Man — written on the original
    /// words of its shape nothing names yet, carried across the links, and the verses read off the words again.
    /// </summary>
    private static async Task<int> FixedTitles(ForgeRun forge, string[] args)
    {
        using var titleScope = forge.Scope();
        forge.Logger.LogInformation("{Outcome}", await titleScope.ServiceProvider.GetRequiredService<FixedTitleLoader>()
            .Load());
        forge.Logger.LogInformation("{Outcome}", await titleScope.ServiceProvider.GetRequiredService<OwnReferenceLoader>()
            .Load());
        return 0;
    }

    /// <summary>
    /// The verses a dataset lists for a record that two readings of the verse agree speak of it: the
    /// reference of the verse under this project's name, the word that stands for the record annotated where
    /// it is a noun or a name, the verses read off the words again, and the verses that name told from those
    /// that do not.
    /// </summary>
    private static async Task<int> VerseReadings(ForgeRun forge, string[] args)
    {
        using var verseScope = forge.Scope();
        forge.Logger.LogInformation("{Outcome}", await verseScope.ServiceProvider.GetRequiredService<VerseReadingLoader>()
            .Load());
        forge.Logger.LogInformation("{Outcome}", await verseScope.ServiceProvider.GetRequiredService<OwnReferenceLoader>()
            .Load());
        await TellTheNamingVerses(verseScope);
        return 0;
    }

    /// <summary>
    /// The word by which a verse speaks of a person the dataset lists there without printing the name, as two
    /// readings of the passage agreed on it, written where no word names anybody, carried across the links,
    /// and the verses read off the words again.
    /// </summary>
    private static async Task<int> PassageReadings(ForgeRun forge, string[] args)
    {
        using var readingScope = forge.Scope();
        forge.Logger.LogInformation("{Outcome}", await readingScope.ServiceProvider.GetRequiredService<PassageReadingLoader>()
            .Load(forge.Resources));
        forge.Logger.LogInformation("{Outcome}", await readingScope.ServiceProvider.GetRequiredService<OwnReferenceLoader>()
            .Load());
        return 0;
    }

    /// <summary>
    /// The records a dataset supplied, made this project's own, folded or withdrawn: the words the rulings
    /// give them, the words one reading of a passage finds, the names a word was wrongly given taken off,
    /// the verses read off the words, the clauses that describe them and the relationships read off those,
    /// the folds and the withdrawals, the verses their relationships were read from where nothing else
    /// lists one, their own lines, and the verses that name told from those that concern.
    /// </summary>
    private static async Task<int> DatasetRecords(ForgeRun forge, string[] args)
    {
        using var recordsScope = forge.Scope();
        var records = recordsScope.ServiceProvider;
        var logger = forge.Logger;
        var resources = forge.Resources;
        logger.LogInformation("{Outcome}", await records.GetRequiredService<OwnRecordLoader>().Load(resources));
        logger.LogInformation("{Outcome}", await records.GetRequiredService<PassageReadingLoader>().Load(resources));
        logger.LogInformation("{Outcome}", await records.GetRequiredService<MisplacedAnnotationLoader>().Load());
        logger.LogInformation("{Outcome}", await records.GetRequiredService<OwnReferenceLoader>().Load());
        logger.LogInformation("{Outcome}", await records.GetRequiredService<EntityDescriptorLoader>().Load(resources));
        logger.LogInformation("{Outcome}", await records.GetRequiredService<OwnRelationshipLoader>().Load());
        logger.LogInformation("{Outcome}", await records.GetRequiredService<EntityNameFormLoader>().Load(resources));
        logger.LogInformation("{Outcome}", await records.GetRequiredService<DuplicateRecordLoader>().Load());
        logger.LogInformation("{Outcome}", await records.GetRequiredService<WithdrawnRecordLoader>().Load());
        logger.LogInformation("{Outcome}", await records.GetRequiredService<RelationshipVerseLoader>().Load());
        logger.LogInformation("{Outcome}", await records.GetRequiredService<DistinguisherLoader>().Load());
        await TellTheNamingVerses(recordsScope);
        return 0;
    }
}
