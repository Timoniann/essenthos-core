using Essenthos.Core.Bhsa;
using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.TextusReceptus;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Loading.Links.Evidentia;
using Essenthos.Core.Verification;
using Microsoft.EntityFrameworkCore;
using Essenthos.Core.Loading.Encyclopedia;

namespace Essenthos.Core.Loading;

/// <summary>
/// Loads every witness the corpus holds, one source at a time. Each one checks whether it is
/// already there and does nothing if it is, so running this twice is how a new text is added rather
/// than a way to duplicate the ones already loaded.
///
/// It is a command rather than something that runs while a server answers. Loading is hours of
/// parsing against a database nobody is reading yet, on the one machine that holds the gigabyte of
/// sources; what a server receives is the result.
///
/// A failure is logged as an error and recorded on <see cref="DatasetStatus"/> rather than being
/// swallowed: a load still working and a load that gave up look identical from the outside, and
/// only one of them is worth waiting for.
/// </summary>
internal sealed class DatasetLoader(
    IServiceProvider services,
    IHostEnvironment environment,
    IConfiguration configuration,
    DatasetStatus status,
    ICanonIndex canon,
    ILogger<DatasetLoader> logger)
{
    /// <summary>
    /// The texts that can be read again on their own, after a change to how their source is read,
    /// without the whole load: the text is deleted with everything that hangs on it, loaded afresh,
    /// placed, folded and joined verse by verse again. Its word links go with it and have to be
    /// aligned again.
    /// </summary>
    private static readonly Dictionary<string, Func<string, TextSource>> Reloadable = new()
    {
        [GeezTextSource.Slug] = resources => GeezTextSource.Read(Path.Combine(resources, GeezTextSource.Folder)),
    };

    public async Task Reload(string slug, CancellationToken cancellationToken)
    {
        if (!Reloadable.TryGetValue(slug, out var read))
        {
            throw new InvalidOperationException(
                $"\"{slug}\" cannot be reloaded on its own; the texts that can are {string.Join(", ", Reloadable.Keys)}. " +
                "Run the whole load for any other.");
        }

        var resources = ResourcePaths.Read(configuration, environment.ContentRootPath);
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(30));
            var deleted = await db.Texts.Where(text => text.Slug == slug).ExecuteDeleteAsync(cancellationToken);
            logger.LogInformation("Deleted {Count} text {Slug} with its words, verses and links", deleted, slug);
        }

        await Load(slug, () => read(resources), cancellationToken);

        var rules = TvtmsReader.Read(ResourcePaths.File(resources, "Versification", "TVTMS.txt"));
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var text = await db.Texts.SingleAsync(t => t.Slug == slug, cancellationToken);
            status.Record(await scope.ServiceProvider.GetRequiredService<CanonicalFrameLoader>()
                .Place(text, rules, cancellationToken));
        }

        await GiveEveryWordASearchableForm(cancellationToken);
        await JoinTheVerses(cancellationToken);
    }

    public async Task Run(CancellationToken stoppingToken)
    {
        try
        {
            var resources = ResourcePaths.Read(configuration, environment.ContentRootPath);
            logger.LogInformation("Loading the dataset from {ResourcesPath}", resources);

            var bhsa = BhsaProject.Load(Path.Combine(resources, "etcbc"));
            await Load("BHSA", () => BhsaTextSource.Build(bhsa), stoppingToken);
            await Load("Nestle 1904", () => NestleTextSource.Read(
                ResourcePaths.File(resources, "Nestle1904", "Nestle1904.xml"),
                ResourcePaths.File(resources, "Nestle1904", "berean-interlinear-glosses.xml")), stoppingToken);

            // Both printed editions come out of one file, so they are one parse and two texts. The
            // extraction is checked against byztxt/greektext-scrivener in the tests rather than here:
            // it is a property of the reader, not of a particular load.
            foreach (var edition in Editions)
            {
                await Load($"the {edition} Textus Receptus", () => TextusReceptusTextSource.Read(
                    Path.Combine(resources, "TextusReceptus"), edition), stoppingToken);
            }

            // The two editions Nestle 1904 was voted out of. They add almost no reading the corpus
            // could not already see; what they add is the reason Nestle reads as it does, because
            // at every place the three differ his text is whichever two of them agreed.
            await Load("Tischendorf's eighth edition", () => TischendorfTextSource.Read(
                Path.Combine(resources, TischendorfFolder)), stoppingToken);
            await Load("Westcott and Hort", () => WestcottHortTextSource.Read(
                Path.Combine(resources, WestcottHortFolder)), stoppingToken);

            // The one Greek witness that is neither critical nor Erasmian. It is loaded from the
            // same shape of file as the two above and carries a Strong number on every word, so it
            // needs no reader of its own beyond an alphabet and no aligner at all.
            await Load("the Byzantine Textform", () => ByzantineTextSource.Read(
                Path.Combine(resources, "Byzantine")), stoppingToken);

            await Load("Brenton's Septuagint", () => SeptuagintTextSource.Read(
                Path.Combine(resources, "Septuagint")), stoppingToken);

            // The second Greek Old Testament, and a diplomatic one: Codex Vaticanus as it stands
            // where Brenton is a text printed to be translated from. They disagree about the verse
            // division of most of the books they share, which is the whole reason to hold both.
            await Load("Swete's Septuagint", () => SweteTextSource.Read(
                Path.Combine(resources, "Swete")), stoppingToken);

            // Codex Alexandrinus, in the one book it is printed whole here: Ottley's Isaiah, a
            // diplomatic text of a different manuscript from the Vaticanus Swete prints.
            await Load("Ottley's Isaiah", () => OttleyTextSource.Read(
                Path.Combine(resources, "Swete")), stoppingToken);

            // The Torah as the Samaritan community transmitted it, which is the first text here
            // that disagrees with BHSA about the Hebrew rather than about a translation of it.
            await Load("the Samaritan Pentateuch", () => SamaritanTextSource.Read(
                Path.Combine(resources, "SamaritanPentateuch")), stoppingToken);

            // The Ethiopic Bible, the first daughter version of the Septuagint here and the only text
            // of Enoch and Jubilees: the church's printed Bible, with Dillmann's and Ludolf's editions
            // where the files hold them in verses.
            await Load(GeezTextSource.Definition.Name, () => GeezTextSource.Read(
                Path.Combine(resources, GeezTextSource.Folder)), stoppingToken);

            // The Berean's own edition, because rebuilding it from the tables is right nine verses
            // in ten and a text that is right nine times in ten is not a text. The tables then say
            // which of its words renders which Greek word.
            await Load("the Berean Standard Bible", () => BereanTextSource.Read(
                ResourcePaths.File(resources, "Berean", "bsb.txt"),
                ResourcePaths.File(resources, "Berean", "bsb_tables.tsv")), stoppingToken);

            foreach (var translation in Bible4uTranslations)
            {
                await Load(translation, () => Bible4uTextSource.Read(
                    ResourcePaths.File(resources, "bible4u", $"{translation}.xml"), translation), stoppingToken);
            }

            // The first complete Ukrainian Bible, and the only Ukrainian text the corpus holds that
            // needs nobody's permission. It reads through the same USFM reader as Brenton.
            await Load("the Kulish Bible", () => KulishTextSource.Read(
                Path.Combine(resources, "Kulish")), stoppingToken);

            // The German and the Spanish, which the interface is to speak and the corpus had no
            // text in. Luther carries Strong numbers on its own words, which is the only route to
            // the originals German has that is not this project's own inference; the Spanish
            // reaches them through an alignment somebody else published, and its own tagging is
            // deliberately not loaded.
            foreach (var (folder, definition) in EbibleTextSource.Definitions)
            {
                await Load(definition.Name, () => EbibleTextSource.Read(
                    Path.Combine(resources, folder)), stoppingToken);
            }

            // The Chinese Union Version in both scripts and the Korean Revised Version, from CrossWire's
            // modules. The Chinese reaches the originals through FHL's Strong numbers, which are read
            // by their own command and never stored; the Korean carries nothing but its words.
            foreach (var text in SwordTextSource.Texts.Values)
            {
                await Load(text.Definition.Name, () => SwordTextSource.Read(
                    Path.Combine(resources, text.Folder)), stoppingToken);
            }

            // The English that is not the King James in other spelling: Tyndale, which the King
            // James is largely a revision of, and five more each made from a different underlying
            // text or by a different method. None of them carries a usable word map, so all six
            // reach the originals through the aligner — Young's best, because Young translated one
            // lexeme by one lexeme, which is what makes it a check on the aligner rather than only
            // another consumer of it.
            foreach (var (folder, definition) in EnglishTextSource.Definitions)
            {
                await Load(definition.Name, () => EnglishTextSource.Read(
                    Path.Combine(resources, folder)), stoppingToken);
            }

            // The one English besides the Berean whose words people tied to the originals, read from the
            // aligned file the ties are drawn from later; and the Portuguese Almeida, whose New Testament
            // Clear Bible's Portuguese set speaks about.
            await Load(UnfoldingWordTextSource.Definition.Name, () => UnfoldingWordTextSource.Read(
                Path.Combine(resources, "Door43", UnfoldingWordTextSource.Folder)), stoppingToken);
            await Load(AlmeidaTextSource.Definition.Name, () => AlmeidaTextSource.Read(
                Path.Combine(resources, AlmeidaTextSource.Folder)), stoppingToken);

            await LoadTheLexicon(resources, stoppingToken);
            await TranslateTheLexicon(resources, stoppingToken);
            await LoadTheSyntax(bhsa, stoppingToken);
            await PlaceInTheFrame(resources, stoppingToken);
            await OpenThePsalmsTheirEditionsOpenWith(resources, stoppingToken);
            await FinishTheVersesOhienkosFileCutShort(resources, stoppingToken);
            await RestoreWhatSwetesTranscriptionLost(resources, stoppingToken);
            await LemmatiseTheSeptuagint(resources, stoppingToken);
            await GlossTheGreek(resources, stoppingToken);
            await ParseTheGreekASecondTime(resources, stoppingToken);
            await AnnotateTheGreek(resources, stoppingToken);
            await LinkTheOldTestament(resources, stoppingToken);
            await LinkTheNewTestament(resources, stoppingToken);
            await LinkTheBerean(resources, stoppingToken);
            await ReadTheHandMadeAlignments(resources, stoppingToken);
            await LinkThePrintedEditions(resources, stoppingToken);
            await LinkTheGreekWitnesses(stoppingToken);
            await LinkTheHebrewWitnesses(stoppingToken);
            await LinkTheTwoSeptuagints(stoppingToken);
            await GiveEveryWordASearchableForm(stoppingToken);
            await JoinTheWordsThatArePrintedTogether(stoppingToken);
            await LinkFromTheInterlinear(resources, stoppingToken);
            await CoverThePsalmTitles(resources, stoppingToken);
            await JoinTheVerses(stoppingToken);
            await ReplayTheVerdictsOnEvidentia(resources, stoppingToken);
            await LoadTheEncyclopedia(resources, stoppingToken);
            await ReadTheStatedKinship(stoppingToken);
            await NameThePeoples(resources, stoppingToken);
            await MakeThePlacesOurs(resources, stoppingToken);
            await TellTheNamesakesApart(resources, stoppingToken);
            await SayWhichWordNamesWhom(stoppingToken);
            await WriteTheRecordsNobodyElseHolds(resources, stoppingToken);
            await ReadTheNamesNothingSettles(resources, stoppingToken);
            await TellTheNamesakePlacesApart(resources, stoppingToken);
            await TellTheGreekNamesakesApart(stoppingToken);
            await NameTheGreekNamesOfHebrewOrigin(stoppingToken);
            await NameWhatTheVerseListLeavesOneBearerFor(resources, stoppingToken);
            await NameWhatTheEncyclopediaHoldsUnderAnotherNumber(stoppingToken);
            await NameTheTribesTheConstructNames(stoppingToken);
            await ReachTheNamesNobodyElseCarries(stoppingToken);
            await WriteTheWordsForGod(stoppingToken);
            await HoldTheTitlesAsTitles(stoppingToken);
            await GiveTheNamesNoDatasetGives(stoppingToken);
            await WriteTheThingsMadeAndTheTimesKept(stoppingToken);
            await WriteWhatTheNarrativesTurnOn(stoppingToken);
            await CiteTheVersesOurOwnWordsName(stoppingToken);
            await GiveThePeoplesTheVersesFiledUnderTheirAncestors(resources, stoppingToken);
            await DescribeTheEntitiesInOurOwnWords(resources, stoppingToken);
            await RelateTheEntitiesOurOwnClausesRelate(stoppingToken);
            await DeclineTheNamesThoseLinesName(resources, stoppingToken);
            await FoldTheRecordsWrittenTwice(stoppingToken);
            await MoveWhatWasReadOffTheMisfiledVerses(stoppingToken);
            await CrossBackTheNamesGivenToEachOther(stoppingToken);
            await CountHowEachTextSpellsEachName(stoppingToken);
            await PutThePlacesOnTheMap(resources, stoppingToken);
            await CountTheCommandments(stoppingToken);
            await FileTheVersesUnderNavesTopics(resources, stoppingToken);
            await PictureThePeopleAndPlaces(resources, stoppingToken);

            // The index answers from what it read the first time it was asked, and until now that
            // was an empty database.
            canon.Forget();

            // Measured after every load and not only after a change, because the corpus is written
            // by several loaders and the question is what they produced together.
            status.Starting("the verification pass");
            await Verify(stoppingToken);

            status.Ready();
            logger.LogInformation("The dataset is loaded");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("The dataset load was cancelled by shutdown");
        }
        catch (Exception exception)
        {
            status.Failed(exception.Message);
            logger.LogError(exception, "The dataset load failed; the API will answer 404 until it is fixed");
        }
    }

    /// <summary>
    /// The three bible4u translations, in the order a reader is most likely to want them.
    /// </summary>
    private static readonly string[] Bible4uTranslations = ["KJV", "RUSV", "UKR"];

    /// <summary>
    /// The two editions Robinson's composite holds. Scrivener is the text the King James was
    /// translated from and the reason its unreached words are unreached; Stephanus is the first
    /// alternative of the same groups and costs nothing more to read.
    /// </summary>
    private static readonly Edition[] Editions = [Edition.Scrivener1894, Edition.Stephanus1550];

    private const string StepBibleFolder = "STEPBible";

    private const string TischendorfFolder = "Tischendorf";

    private const string WestcottHortFolder = "WestcottHort";

    /// <summary>
    /// STEPBible splits the Old Testament across four files because one would be too large for
    /// GitHub. They are one dataset and each row addresses its own verse, so they are read together
    /// and the order does not matter.
    /// </summary>
    private const string TahotVolumes = "TAHOT *.txt";

    /// <summary>
    /// The Greek texts the King James is matched against, in the order they are worth reading: the
    /// one it was translated from, then the tradition that text belongs to, then the one it was not.
    ///
    /// Matching it against all three is how the corpus answers from its own data which text the
    /// translators followed, rather than repeating what everyone says about it. The difference in
    /// what the English reaches in each is the evidence.
    /// </summary>
    private static readonly string[] GreekWitnesses =
    [
        TextusReceptusTextSource.Slug(Edition.Scrivener1894),
        ByzantineTextSource.Slug,
        NestleTextSource.Slug,
    ];

    /// <summary>
    /// BHSA's clauses, phrases and sentences. It reads the same parse the text was loaded from
    /// rather than parsing the files twice — a million groups over three hundred megabytes of
    /// Text-Fabric is not work to repeat for want of passing a reference along.
    /// </summary>
    private async Task LoadTheSyntax(BhsaProject project, CancellationToken cancellationToken)
    {
        status.Starting("BHSA's syntax");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<SyntaxLoader>();
        status.Record(await loader.Load(project, BhsaTextSource.Slug, cancellationToken));
    }

    /// <summary>
    /// Strong's concordance, which belongs to no text and is loaded once. It is what turns the
    /// numbers every text has been carrying into something that can be resolved and checked.
    /// </summary>
    private async Task LoadTheLexicon(string resources, CancellationToken cancellationToken)
    {
        status.Starting("Strong's concordance");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<StrongLexiconLoader>();
        status.Record(await loader.Load(
            ResourcePaths.File(resources, "Strong", "StrongHebrew.xml"),
            ResourcePaths.File(resources, "Strong", "StrongGreek.xml"),
            cancellationToken));
    }

    /// <summary>
    /// The same dictionary in a reader's own language, as a translation run published it. Directly
    /// after the lexicon, because it stands beside those entries and refuses a number they do not
    /// hold; before everything else, because nothing else depends on it.
    /// </summary>
    private async Task TranslateTheLexicon(string resources, CancellationToken cancellationToken)
    {
        status.Starting("Strong's concordance in a reader's language");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<StrongTranslationLoader>();
        status.Record(await loader.Load(resources, cancellationToken));
    }

    /// <summary>
    /// The words Swete printed and the transcription lost, and the letters and spaces it got wrong
    /// that a rule settles, written into a Swete loaded before they were restored. A cold load reads
    /// them from the reader and this finds nothing to do. Before the two Septuagints are linked, so
    /// that on a cold corpus the link sees the restored verse.
    /// </summary>
    private async Task RestoreWhatSwetesTranscriptionLost(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the words Swete's transcription lost");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<SweteRestorationLoader>();
        var outcome = await loader.Load(Path.Combine(resources, "Swete"), cancellationToken);
        if (outcome.Verses > 0)
        {
            status.Record(outcome.ToString());
        }
    }

    /// <summary>
    /// The head of a psalm, where the file a text was loaded from does not print it: the King
    /// James's 116 superscriptions, which Zefania XML has no element for, and the first line of
    /// Ohienko's Psalm 7, which his own printing has and the digitisation here lost.
    ///
    /// After the frame and before the links. It creates no verse, so nothing here needs placing —
    /// but the verse holding a title comes to stand at the title address as well as its own, and
    /// whether the frame holds a title verse there is a question only a placed corpus can answer.
    /// Before the links, because the verse links are derived from the addresses a verse covers.
    ///
    /// Silent where the complete editions have not been fetched, which is every fresh clone: the
    /// psalms then read as they do today rather than the load failing over a folder that is
    /// deliberately not in git.
    /// </summary>
    private async Task OpenThePsalmsTheirEditionsOpenWith(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the psalm openings their editions print");

        var sources = new (string Slug, IReadOnlyList<PsalmOpening> Openings, string Folder, string Note, TextPartSource Source)[]
        {
            (Bible4uTextSource.KingJames,
                LostPsalmOpenings.KingJamesSuperscriptions(
                    Path.Combine(resources, LostPsalmOpenings.KingJamesFolder)),
                LostPsalmOpenings.KingJamesFolder,
                "The 116 psalm superscriptions bible4u's file prints nowhere are taken from eBible's "
                + "eng-kjv2006, the standardised 1769 text, which states Public Domain.",
                LostPsalmOpenings.KingJamesSource),

            (Bible4uTextSource.Ohienko,
                LostPsalmOpenings.OhienkoLostLine(Path.Combine(resources, LostPsalmOpenings.OhienkoFolder)),
                LostPsalmOpenings.OhienkoFolder,
                "Modified: the first line of Psalm 7, which this digitisation dropped and Ohienko printed, "
                + "is restored from the transcription of the 1988 printing on Ukrainian Wikisource, "
                + "CC BY-SA 4.0, with its stress marks removed.",
                LostPsalmOpenings.OhienkoSource),
        };

        foreach (var (slug, openings, folder, note, source) in sources)
        {
            if (openings.Count == 0)
            {
                logger.LogWarning(
                    "The complete edition the {Slug} psalm openings are read from is not in {Folder}, so the "
                    + "psalms it does not print stay as they are. Run scripts/fetch-ebible.ps1 and "
                    + "scripts/fetch-ohienko-wikisource.ps1",
                    slug, Path.Combine(resources, folder));
                continue;
            }

            using var scope = services.CreateScope();
            var loader = scope.ServiceProvider.GetRequiredService<PsalmOpeningLoader>();
            var outcome = await loader.Load(slug, openings, note, source, cancellationToken);
            if (outcome.Psalms > 0 || outcome.Placed > 0)
            {
                status.Record(outcome.ToString());
            }
        }
    }

    /// <summary>
    /// The ends of the seven verses the digitisation Ohienko is loaded from cut short, read from the
    /// transcription of his printing. Beside the psalm openings and for the same reasons: it creates
    /// no verse, and it has to come before the links and the searchable forms so that on a cold
    /// corpus they see the whole verse. Silent where the transcription has not been fetched.
    /// </summary>
    private async Task FinishTheVersesOhienkosFileCutShort(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the verse endings Ohienko's file lost");

        var folder = Path.Combine(resources, LostPsalmOpenings.OhienkoFolder);
        var endings = LostVerseEndings.OhienkoVerses(folder);
        if (endings.Count == 0)
        {
            logger.LogWarning(
                "The transcription the ends of Ohienko's cut-short verses are read from is not in {Folder}, so "
                + "those verses stay as the file has them. Run scripts/fetch-ohienko-wikisource.ps1",
                folder);
            return;
        }

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<VerseEndingLoader>();
        var outcome = await loader.Load(
            Bible4uTextSource.Ohienko, endings, LostVerseEndings.OhienkoNote, LostVerseEndings.OhienkoPart, cancellationToken);
        if (outcome.Verses > 0)
        {
            status.Record(outcome.ToString());
        }
    }

    /// <summary>
    /// Every text is placed in the shared frame after all of them are loaded, so that a text added
    /// later is placed on the next boot without the others being touched.
    /// </summary>
    private async Task PlaceInTheFrame(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the canonical frame");
        var rules = TvtmsReader.Read(ResourcePaths.File(resources, "Versification", "TVTMS.txt"));

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loader = scope.ServiceProvider.GetRequiredService<CanonicalFrameLoader>();

        foreach (var text in await db.Texts.OrderBy(t => t.Slug).ToListAsync(cancellationToken))
        {
            if (!rules.Covers(text.Versification))
            {
                logger.LogWarning(
                    "The text {Slug} follows {Versification} numbering, which the versification data does not " +
                    "cover, so it stays out of the shared frame and cannot be read beside another text",
                    text.Slug, text.Versification);
                continue;
            }

            status.Record(await loader.Place(text, rules, cancellationToken));
        }
    }

    /// <summary>
    /// The Old Testament correspondences, which need both texts placed in the frame first: the file
    /// addresses verses the way the King James numbers them, and BHSA numbers several of them
    /// otherwise.
    /// </summary>
    private async Task LinkTheOldTestament(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the Old Testament links");
        var records = KjvBhsMapping.Read(
            ResourcePaths.File(resources, "mapping", "KJV-OT-mapped-to-BHS-full-mapping.csv"));

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<OldTestamentLinkLoader>();
        status.Record(await loader.Load(records, Tahot(resources), cancellationToken));
    }

    /// <summary>
    /// STEPBible's morpheme segmentation, which says which of the mapping file's morphemes are
    /// prefixes and what each means where it stands. It is fetched rather than committed, so a
    /// checkout without it still loads: the prefixes then fall back to this project's own list of
    /// what each one can render, which is weaker, and the load says so rather than being silently
    /// worse.
    /// </summary>
    private TahotSegmentation? Tahot(string resources)
    {
        var folder = Path.Combine(resources, StepBibleFolder);
        var volumes = Directory.Exists(folder)
            ? Directory.GetFiles(folder, TahotVolumes).Order(StringComparer.Ordinal).ToArray()
            : [];

        if (volumes.Length == 0)
        {
            logger.LogWarning(
                "TAHOT is not in {Folder}, so the Hebrew prefixes are matched from this project's own list of " +
                "what each one can render rather than from what STEPBible states. Run scripts/fetch-stepbible.ps1",
                folder);
            return null;
        }

        var segmentation = TahotSegmentation.Read(volumes);
        logger.LogInformation(
            "Read {Morphemes} morphemes over {Verses} verses from {Volumes} TAHOT volumes",
            segmentation.Morphemes, segmentation.Verses, volumes.Length);
        return segmentation;
    }

    /// <summary>
    /// The New Testament correspondences, which no source states — this is Strong numbers matched
    /// within a verse, and every link it writes says so.
    /// </summary>
    private async Task LinkTheNewTestament(string resources, CancellationToken cancellationToken)
    {
        var zefania = ResourcePaths.File(resources, "Zefania", "SF_2009-01-20_ENG_KJV_(KJV+).xml");

        // Against both Greek witnesses. The King James renders the Textus Receptus, so Scrivener is
        // the text it was translated from and Nestle is the one the corpus could offer it until
        // now; the difference between what it reaches in each is evidence of which text it followed,
        // derived from our own data.
        foreach (var greek in GreekWitnesses)
        {
            status.Starting($"the New Testament links against {greek}");

            using var scope = services.CreateScope();
            var loader = scope.ServiceProvider.GetRequiredService<NewTestamentLinkLoader>();
            status.Record(await loader.Load(zefania, greek, cancellationToken));
        }
    }

    /// <summary>
    /// Measures what the load produced and stores it beside what the last one produced. A failure
    /// here does not fail the load: a corpus that cannot be measured is still a corpus that can be
    /// read, and the health endpoint will say the measurement is missing.
    /// </summary>
    private async Task Verify(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var check = scope.ServiceProvider.GetRequiredService<CorpusCheck>();

        try
        {
            status.Record((await check.Record(cancellationToken)).ToString());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "The verification pass failed; the corpus is loaded and unmeasured");
        }
    }

    /// <summary>
    /// The two printed editions against each other. Nothing is aligned: they come out of one token
    /// stream with a choice at 261 places, so the file itself says which word corresponds to which.
    /// </summary>
    private async Task LinkThePrintedEditions(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the printed editions");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<PrintedEditionLinkLoader>();
        status.Record(await loader.Load(Path.Combine(resources, "TextusReceptus"), cancellationToken));
    }

    /// <summary>
    /// Every other Greek edition against the Textus Receptus, by the Strong numbers all of them
    /// state, and then Nestle's own two ingredients against Nestle.
    ///
    /// Scrivener is the hub, because Stephanus already meets it word for word: a word carries the
    /// witness ids it reaches, so linking to Scrivener puts Scrivener's ids on both sides and joins
    /// every Greek pane at once. It also makes each pairing a measurement — how far the Received
    /// Text stands from the critical text, and how far it stands from the tradition it is usually
    /// said to represent, in words rather than in reputation.
    /// </summary>
    private async Task LinkTheGreekWitnesses(CancellationToken cancellationToken)
    {
        status.Starting("the Greek witnesses to each other");

        var witnesses = new[]
        {
            NestleTextSource.Slug,
            ByzantineTextSource.Slug,
            TischendorfTextSource.Slug,
            WestcottHortTextSource.Slug,
        };

        foreach (var witness in witnesses)
        {
            using var scope = services.CreateScope();
            var loader = scope.ServiceProvider.GetRequiredService<GreekWitnessLinkLoader>();
            status.Record(await loader.Load(
                witness, TextusReceptusTextSource.Slug(Edition.Scrivener1894), cancellationToken));
        }

        // And each of Nestle's two ingredients directly against Nestle, which is the pair the
        // decomposition is read off. Through Scrivener it could only be read as two hops with the
        // Received Text in the middle, and the Received Text disagrees with all three of them.
        foreach (var ingredient in new[] { TischendorfTextSource.Slug, WestcottHortTextSource.Slug })
        {
            using var scope = services.CreateScope();
            var loader = scope.ServiceProvider.GetRequiredService<GreekWitnessLinkLoader>();
            status.Record(await loader.Load(ingredient, NestleTextSource.Slug, cancellationToken));
        }
    }

    /// <summary>
    /// The Samaritan Pentateuch against BHSA. Nobody states this correspondence, so every link it
    /// writes is an inference over the consonants both witnesses print, carries a confidence, and
    /// says which of the two lacks the word wherever one of them does.
    /// </summary>
    private async Task LinkTheHebrewWitnesses(CancellationToken cancellationToken)
    {
        status.Starting("the Hebrew witnesses to each other");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<SamaritanLinkLoader>();
        status.Record(await loader.Load(
            SamaritanTextSource.Slug, BhsaTextSource.Slug, cancellationToken));
    }

    /// <summary>
    /// Swete's Septuagint against Brenton's. Swete is the <c>from</c> because Swete is the text
    /// that arrived reaching nothing, which is the same reason the Samaritan is the <c>from</c>
    /// against BHSA and Nestle is against Scrivener: the newly linked witness names the one that
    /// already stands in the corpus. It also puts Brenton's deuterocanon opposite a Greek text for
    /// the first time — BHSA has no such books, so until now those words stood against nothing.
    /// </summary>
    private async Task LinkTheTwoSeptuagints(CancellationToken cancellationToken)
    {
        status.Starting("the two Septuagints to each other");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<SeptuagintLinkLoader>();
        status.Record(await loader.Load(
            SweteTextSource.Slug, SeptuagintTextSource.Slug, cancellationToken));

        // Alexandrinus against Vaticanus, the two Cambridge diplomatic texts, in Isaiah.
        status.Record(await loader.Load(
            OttleyTextSource.Slug, SweteTextSource.Slug, cancellationToken));
    }

    /// <summary>
    /// The form a word is searched by. Idempotent by the column itself, so a loaded corpus pays
    /// one indexed count and a newly loaded text is folded the once.
    /// </summary>
    private async Task GiveEveryWordASearchableForm(CancellationToken cancellationToken)
    {
        status.Starting("the searchable form of every word");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<WordFoldingLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// The printed word, where several rows make one. A row is a morpheme and Hebrew prints
    /// several of them together, so without this a reader who types what the page shows is told
    /// the corpus does not have it. Runs after the folding it concatenates.
    /// </summary>
    private async Task JoinTheWordsThatArePrintedTogether(CancellationToken cancellationToken)
    {
        status.Starting("the printed form of every word");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<GraphicalWordLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// The stated word-level correspondence the Slavic texts have. Everything else they reach, they
    /// reach through a model; this is people saying which word renders which.
    ///
    /// The Ukrainian covers twelve books and the Russian three, and the Russian three are the whole
    /// of what anyone has aligned of the Synodal. Their worth is not coverage — it is that a model's
    /// answer on a Slavic text can be checked against a person's at all.
    /// </summary>
    private async Task LinkFromTheInterlinear(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the Ukrainian interlinear");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<InterlinearLinkLoader>();
        var ukrainian = InterlinearLinkLoader.Interlinear(Bible4uTextSource.Ohienko);
        status.Record(await loader.Load(
            Path.Combine(resources, "Door43", ukrainian.Folder),
            Bible4uTextSource.Ohienko,
            ukrainian.Source,
            cancellationToken));

        status.Starting("the Russian Synodal alignment");

        var russian = InterlinearLinkLoader.Interlinear(Bible4uTextSource.Synodal);
        status.Record(await loader.Load(
            Path.Combine(resources, "Door43", russian.Folder),
            Bible4uTextSource.Synodal,
            russian.Source,
            cancellationToken));

        status.Starting("the unfoldingWord Literal Text alignment");

        var literal = InterlinearLinkLoader.Interlinear(UnfoldingWordTextSource.Slug);
        status.Record(await loader.Load(
            Path.Combine(resources, "Door43", literal.Folder),
            UnfoldingWordTextSource.Slug,
            literal.Source,
            cancellationToken));
    }

    /// <summary>
    /// The one text that arrived without lemmas. GLAUx is used as a dictionary and its own Greek is
    /// never loaded, which is a licence distinction as much as a technical one: its lemmas are
    /// CC BY-SA and are attributed as such, and none of its own text is republished here.
    /// </summary>
    private async Task LemmatiseTheSeptuagint(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the Septuagint lemmas");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<Essenthos.Core.Glaux.GlauxLemmaLoader>();
        status.Record(await loader.Load(Path.Combine(resources, "Glaux", "xml"), cancellationToken));

        // The number follows from the lemma, so it is proposed in the same breath -- but into
        // word_strong, because Strong never numbered the Greek Old Testament and a number here is
        // our reasoning rather than anybody's testimony.
        var numbers = scope.ServiceProvider.GetRequiredService<Essenthos.Core.Glaux.SeptuagintStrongLoader>();
        status.Record(await numbers.Load(cancellationToken));
    }

    /// <summary>
    /// STEPBible's short Greek glosses. After the lemmas, because the Septuagint reaches them by
    /// its lemma and the load measures how much of it does.
    /// </summary>
    private async Task GlossTheGreek(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the Greek glosses");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<GreekGlossLoader>();
        status.Record(await loader.Load(
            ResourcePaths.File(resources, "STEPBibleLexicons", "TBESG.txt"), cancellationToken));
    }

    /// <summary>
    /// A second opinion on the Greek New Testament's morphology, from MorphGNT. It runs after the
    /// corpus loader and needs nothing from the links, because it joins the two editions by what
    /// they print rather than by anything either of them is linked to.
    /// </summary>
    private async Task ParseTheGreekASecondTime(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the second Greek morphology");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<MorphGntParsingLoader>();
        status.Record(await loader.Load(Path.Combine(resources, "MorphGnt"), cancellationToken));
    }

    /// <summary>
    /// MACULA Greek's annotation of Nestle 1904, which is where the Greek New Testament's words
    /// first get a stated proper-noun class instead of one worked out from a capital letter. It
    /// runs after the corpus loader and needs nothing from the links: it is published over the same
    /// edition, so a word is found by its address and not by anything it is joined to.
    /// </summary>
    private async Task AnnotateTheGreek(string resources, CancellationToken cancellationToken)
    {
        status.Starting("MACULA's annotation of the Greek");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<MaculaAnnotationLoader>();
        status.Record(await loader.Load(Path.Combine(resources, "Macula"), cancellationToken));
    }

    /// <summary>
    /// The second stated word mapping the corpus has, and the first that reaches the New Testament.
    /// It is joined by word order and checked by the Strong number both sides state; a verse the two
    /// divide differently is refused whole rather than aligned partly.
    /// </summary>
    private async Task LinkTheBerean(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the Berean tables");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<Links.BereanLinkLoader>();
        var tables = Path.Combine(resources, "Berean", "bsb_tables.tsv");
        status.Record(await loader.Load(tables, NestleTextSource.Slug, cancellationToken));

        // The same file's Hebrew half, which joins on the letters rather than on the order or the
        // number, because BHSA and the Westminster edition tokenise the same text differently.
        status.Record(await loader.Load(tables, BhsaTextSource.Slug, cancellationToken));
    }

    /// <summary>
    /// The alignments Clear Bible's team made by hand, which are two different things here.
    ///
    /// On the Berean they are a second person's answer to the question the Berean's own tables
    /// answer: mostly it agrees, and where it agrees it adds a claim rather than a link — the first
    /// time this corpus could record that two independent methods reached the same word pair. On
    /// the Reina-Valera, the Segond, the Van Dyck and the Indian Revised Version they are the whole
    /// of what anybody has said, and every record becomes a link: no source states a single Spanish,
    /// French, Arabic or Hindi correspondence otherwise, and those texts would otherwise reach the
    /// originals the way the Slavic texts do, through this project's own model.
    /// </summary>
    private async Task ReadTheHandMadeAlignments(string resources, CancellationToken cancellationToken)
    {
        status.Starting("Clear Bible's hand-made alignments");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<Links.ClearBibleLinkLoader>();
        var clearBible = Path.Combine(resources, "ClearBible");

        foreach (var set in ClearBible.ClearBibleSet.All())
        {
            status.Record(await loader.Load(clearBible, set, cancellationToken));
        }
    }

    /// <summary>
    /// Where a translation prints a psalm's superscription inside its first verse, the second
    /// canonical address that verse stands at.
    ///
    /// Before the verses are joined rather than after, because the address is what the verse links
    /// are derived from: a verse recorded as covering the title row is joined to the Hebrew's title
    /// verse by the next step, and only if that step runs afterwards. The two Slavic files are
    /// re-read for it: what decides which verse holds a title is markup the corpus loader strips
    /// before a word is stored, so the answer is in the file and nowhere else, and two XML parses is
    /// a second and a half against a gigabyte of corpus.
    /// </summary>
    private async Task CoverThePsalmTitles(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the psalm titles");

        foreach (var translation in Bible4uTranslations)
        {
            using var scope = services.CreateScope();
            var loader = scope.ServiceProvider.GetRequiredService<SuperscriptionFrameLoader>();
            var outcome = await loader.Load(
                Bible4uTextSource.Read(
                    ResourcePaths.File(resources, "bible4u", $"{translation}.xml"), translation),
                cancellationToken);

            if (outcome.Verses > 0)
            {
                status.Record(outcome.ToString());
            }
        }

        // The Van Dyck and the Indian Revised Version mark a title as one, and the reader hands it to
        // the psalm's first verse.
        foreach (var (folder, _) in EbibleTextSource.Definitions)
        {
            using var scope = services.CreateScope();
            var outcome = await scope.ServiceProvider.GetRequiredService<SuperscriptionFrameLoader>()
                .Load(EbibleTextSource.Read(Path.Combine(resources, folder)), cancellationToken);

            if (outcome.Verses > 0)
            {
                status.Record(outcome.ToString());
            }
        }

        // The unfoldingWord Literal Text and the Almeida print a title before the first verse, as the
        // English does.
        foreach (var read in (Func<TextSource>[])
                 [
                     () => UnfoldingWordTextSource.Read(Path.Combine(resources, "Door43", UnfoldingWordTextSource.Folder)),
                     () => AlmeidaTextSource.Read(Path.Combine(resources, AlmeidaTextSource.Folder)),
                 ])
        {
            using var scope = services.CreateScope();
            var outcome = await scope.ServiceProvider.GetRequiredService<SuperscriptionFrameLoader>()
                .Load(read(), cancellationToken);

            if (outcome.Verses > 0)
            {
                status.Record(outcome.ToString());
            }
        }

        // The Korean marks a title as one; the Chinese prints it in parentheses at the head of the
        // verse, which its reader takes for the same statement.
        foreach (var text in SwordTextSource.Texts.Values)
        {
            using var scope = services.CreateScope();
            var outcome = await scope.ServiceProvider.GetRequiredService<SuperscriptionFrameLoader>()
                .Load(SwordTextSource.Read(Path.Combine(resources, text.Folder)), cancellationToken);

            if (outcome.Verses > 0)
            {
                status.Record(outcome.ToString());
            }
        }
    }

    /// <summary>
    /// Which verse of one text is which verse of another, for every pair the word links already
    /// cover. It runs last of the linking steps on purpose: the pairs come from the links, and the
    /// alignment commands that create most of them are run outside this pipeline, so a pair aligned
    /// today gets its verse links on the next start. The same run also joins every verse that
    /// covers a second address to what stands there, which is what makes a word link crossing a
    /// verse boundary a correspondence the frame backs rather than a fault.
    /// </summary>
    private async Task JoinTheVerses(CancellationToken cancellationToken)
    {
        status.Starting("the verse links");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<VerseLinkLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// The verdicts given on EVIDENTIA's proposals, from the ledger kept beside the other decisions
    /// of this project: a run the database does not hold is written again and its verdicts applied,
    /// and a verdict whose link was deleted is written again. After every linking step, because a
    /// verdict settles against the links the corpus already has, and before the encyclopedia, so the
    /// annotations are carried over the links the verdicts wrote.
    /// </summary>
    private async Task ReplayTheVerdictsOnEvidentia(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the verdicts on EVIDENTIA's proposals");

        using var scope = services.CreateScope();
        var ledger = scope.ServiceProvider.GetRequiredService<EvidentiaLedger>();
        status.Record((await ledger.Replay(resources, cancellationToken: cancellationToken)).ToString());
    }

    /// <summary>
    /// The people, places and dated events the text names. BibleData is loaded and not the other
    /// candidates because its chronology is the only one that is computed, cited and
    /// self-consistent, and BibleDataLoader records what had to be corrected in it.
    ///
    /// **The computed chronology stops at Artaxerxes.** Its method is arithmetic over the
    /// genealogies and the reign lengths, and those stop where the Old Testament stops. Ussher
    /// reaches past it, by reading the consular lists and Josephus as well as Scripture, and is
    /// loaded second and marked as his — a second witness, not the axis.
    /// </summary>
    private async Task LoadTheEncyclopedia(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the encyclopedia");

        var bibleData = Path.Combine(resources, "BibleData2026");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<BibleDataLoader>();
        status.Record(await loader.Load(bibleData, cancellationToken));

        // The New Testament narrative, which nothing else here dates. Guarded on its own rows
        // rather than on the encyclopedia's, so it reaches a database that already holds one.
        using var annals = services.CreateScope();
        var ussher = annals.ServiceProvider.GetRequiredService<UssherAnnalsLoader>();
        status.Record(await ussher.Load(bibleData, cancellationToken));

        // The places, which the first dataset marks in progress and stops after Exodus. Second,
        // because it joins onto the place entities that dataset already created wherever the two
        // name the same place, and creates one only where it does not.
        using var geography = services.CreateScope();
        var places = geography.ServiceProvider.GetRequiredService<OpenBiblePlaceLoader>();
        status.Record(await places.Load(Path.Combine(resources, "OpenBible"), cancellationToken));

        // What else was happening, and the only layer that reaches the New Testament at all. Read
        // from the output folder rather than through the configured resources path: it is the one
        // dataset small enough to be committed, so it is always there and never waits on a fetch.
        using var third = services.CreateScope();
        var world = third.ServiceProvider.GetRequiredService<WorldHistoryLoader>();
        status.Record(await world.Load(
            Path.Combine(AppContext.BaseDirectory, "Resources", "WorldHistory"),
            cancellationToken));

        // The periods of the lands around it, as the scholars who define them date them. Beside the
        // world's events rather than among them: they are bands under an authority, not moments.
        using var lands = services.CreateScope();
        var periodO = lands.ServiceProvider.GetRequiredService<PeriodOLoader>();
        status.Record(await periodO.Load(resources, cancellationToken));

        // The Septuagint's chronology, computed from its own Greek. Last, because it dates the base
        // reckoning's events and the world's alike, and both have to be there to be dated.
        using var greek = services.CreateScope();
        var septuagint = greek.ServiceProvider.GetRequiredService<SeptuagintReckoningLoader>();
        status.Record(await septuagint.Load(resources, cancellationToken));
    }

    /// <summary>
    /// Which peoples the dictionary already says descend from whom. Last of the encyclopedia's
    /// loads because it reads both halves: the lexicon for the claim and the entities for the page
    /// each claim points at.
    /// </summary>
    private async Task ReadTheStatedKinship(CancellationToken cancellationToken)
    {
        status.Starting("the gentilics Strong states");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<StrongGentilicLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// The nations, the tribes and the clans, which the encyclopedia had no kind for. After the
    /// gentilics because it is made out of them — a record per lexeme Strong derives, with the near
    /// end that table was written without — and before the annotations, because a word carrying a
    /// gentilic names the people and until now named nobody.
    /// </summary>
    private async Task NameThePeoples(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the peoples");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<PeopleLoader>();
        status.Record(await loader.Load(resources, cancellationToken));
    }

    /// <summary>
    /// The places, as a record of ours rather than a list the gazetteer lent us. After the peoples
    /// because it is the same construction — a record per lexeme the dictionary heads — and before
    /// the annotations, because what it writes is the Strong number on a place name, and until now
    /// nine tenths of the places had none and no word could reach them.
    /// </summary>
    private async Task MakeThePlacesOurs(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the place register");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<PlaceRegisterLoader>();
        status.Record(await loader.Load(resources, cancellationToken));
    }

    /// <summary>
    /// The men who share a name, told apart at the grain the lexicon enumerates. After the places
    /// because it is the same construction — a record per bearer the dictionary heads — and before
    /// the annotations, because what it writes is a second page under a name a word already reaches
    /// and the annotation has to see both to choose between them.
    /// </summary>
    private async Task TellTheNamesakesApart(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the person register");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<PersonRegisterLoader>();
        status.Record(await loader.Load(resources, cancellationToken));
    }

    /// <summary>
    /// Which word names which person or place. Last of everything, because it needs three earlier
    /// steps to have finished: BHSA's annotation, the links that carry the answer into every other
    /// text, and the encyclopedia the answer is about.
    /// </summary>
    private async Task SayWhichWordNamesWhom(CancellationToken cancellationToken)
    {
        status.Starting("the entity annotations");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<EntityAnnotationLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// The people a verse names and no dataset holds. Before the readings rather than after them,
    /// because the owner has ruled on five occurrences the readings were refused for, and those
    /// words must already point somewhere when the reading pass declines to answer them.
    /// </summary>
    private async Task WriteTheRecordsNobodyElseHolds(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the records this corpus writes for itself");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<OwnRecordLoader>();
        status.Record(await loader.Load(resources, cancellationToken));
    }

    /// <summary>
    /// Whom the words name that no number settles: the model's readings, as claims carrying the
    /// model, the prompt and the date. Last, because it adds answers only where the resolutions
    /// left none and has to see what they wrote.
    /// </summary>
    private async Task ReadTheNamesNothingSettles(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the model's readings of the contested names");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<SenseReadingLoader>();
        status.Record(await loader.Load(resources, cancellationToken));
    }

    /// <summary>
    /// Which of the places that share a name each word means. After the readings because it is the
    /// same kind of claim and must not displace one, and before the references because what it
    /// writes is the whole of what Jericho's page has to cite.
    /// </summary>
    private async Task TellTheNamesakePlacesApart(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the places that share a name");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<SiteSplitLoader>();
        status.Record(await loader.Load(resources, cancellationToken));
    }

    /// <summary>
    /// Which of the people and places that share a Greek name each word means, where the verse
    /// leaves one. After every pass that names a Greek word, because it answers only where none of
    /// them did, and before the references, which read the words it names.
    /// </summary>
    private async Task TellTheGreekNamesakesApart(CancellationToken cancellationToken)
    {
        status.Starting("the Greek names several people share");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<GreekNamesakeLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// The Old Testament names the Greek writes, which the encyclopedia records under the Hebrew
    /// number. After the Greek namesakes, because it answers only where they did not.
    /// </summary>
    private async Task NameTheGreekNamesOfHebrewOrigin(CancellationToken cancellationToken)
    {
        status.Starting("the Greek names of Hebrew origin");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<HebrewOriginNameLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// The names whose number no record bears, where the verse list and the King James's spelling
    /// name one record. After every pass that joins by number, because it answers only where no
    /// number could.
    /// </summary>
    private async Task NameWhatTheEncyclopediaHoldsUnderAnotherNumber(CancellationToken cancellationToken)
    {
        status.Starting("the names the encyclopedia holds under another number");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<RenderedNameLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// The Hebrew names whose marking or whose namesakes left them blank, where the verse list names
    /// one bearer. After the readings and every pass that names a Hebrew word, because it answers
    /// only where none of them did and a reading of the verse stands above a list.
    /// </summary>
    private async Task NameWhatTheVerseListLeavesOneBearerFor(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the Hebrew names a verse list leaves one bearer for");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<ListedBearerLoader>();
        status.Record(await loader.Load(resources, cancellationToken));
    }

    /// <summary>
    /// The records that rest on a dataset because nobody ever asked about them: a name one record
    /// carries, an entry of Strong's that heads it, and occurrences already loaded. After every
    /// pass that writes a record, because what it asks is whether anything else in this corpus
    /// carries the name, and a record written afterwards would make that answer wrong.
    /// </summary>
    private async Task ReachTheNamesNobodyElseCarries(CancellationToken cancellationToken)
    {
        status.Starting("the names nobody else carries");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<SoleBearerLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// The words the text uses of God and of gods, as entries of their own. After every pass that
    /// writes a person's or a place's record, which are the records names are resolved among, and
    /// before the references, so the page lists its verses on the boot that writes it.
    /// </summary>
    private async Task WriteTheWordsForGod(CancellationToken cancellationToken)
    {
        status.Starting("the words for God");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<TermLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// The names the owner ruled are titles borne by possibly more than one man, held as titles.
    /// After every pass that writes or annotates a person, because those passes resolve names among
    /// persons and these records were persons when their words were named, and before the references
    /// and descriptions, so the page reads what the decision says on the boot that applies it.
    /// </summary>
    private async Task HoldTheTitlesAsTitles(CancellationToken cancellationToken)
    {
        status.Starting("the names that are titles");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<TitleLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// The names a record answers to that no dataset gives it — heaven's plural. After the titles,
    /// which can withdraw a record, and before the index counts and orders what it holds.
    /// </summary>
    private async Task GiveTheNamesNoDatasetGives(CancellationToken cancellationToken)
    {
        status.Starting("the names of our own");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<OwnNameLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// The objects and the appointed times, as records with the words that name them. After every
    /// pass that names a word by its number, because a ruling here outranks those and should find
    /// them in place, and before the references, so the pages list their verses on the boot that
    /// writes them.
    /// </summary>
    private async Task WriteTheThingsMadeAndTheTimesKept(CancellationToken cancellationToken)
    {
        status.Starting("the objects and the appointed times");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<ThingLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// The serpent, the cherubim, the Holy Spirit, the New Jerusalem and Nathanael, and the words the
    /// narrative refers to Eve by. Beside the objects, for the same reasons, and before the fold,
    /// which moves onto Nathanael what the dataset's record of Bartholomew holds of him.
    /// </summary>
    private async Task WriteWhatTheNarrativesTurnOn(CancellationToken cancellationToken)
    {
        status.Starting(ThingSet.Narratives.About);

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<ThingLoader>();
        status.Record(await loader.Load(ThingSet.Narratives, cancellationToken));
    }

    /// <summary>
    /// Where a person or a place is named, read off the words this corpus annotated rather than
    /// taken from a dataset's list. After every step that writes an annotation and before the
    /// descriptions, which cite these references and would otherwise be checked against a verse
    /// list that is still a dataset's.
    /// </summary>
    private async Task CiteTheVersesOurOwnWordsName(CancellationToken cancellationToken)
    {
        status.Starting("the references this corpus reads for itself");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<OwnReferenceLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// The verses the dataset files under a man that name the people called after him, moved to the
    /// people, the few rows it files under the wrong record put right, and the verses it leaves off a
    /// record added. After the annotations and the references read off them, because a word already
    /// annotated to a people is part of what decides a verse; before the descriptions, because a
    /// clause may cite only a verse its entity is named in.
    /// </summary>
    private async Task GiveThePeoplesTheVersesFiledUnderTheirAncestors(
        string resources,
        CancellationToken cancellationToken)
    {
        status.Starting("the verses filed under a people's ancestor");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<MisfiledVerseLoader>();
        status.Record(await loader.Load(resources, cancellationToken));
    }

    /// <summary>
    /// What each person, place and people is, in this corpus's own words rather than in the
    /// sentence a dataset supplied. Last, because a clause names another entity and cites a verse
    /// that entity is named in, so both the records and their references have to be there before
    /// any of it can be checked.
    /// </summary>
    private async Task DescribeTheEntitiesInOurOwnWords(
        string resources,
        CancellationToken cancellationToken)
    {
        status.Starting("the descriptions this corpus writes for itself");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<EntityDescriptorLoader>();
        status.Record(await loader.Load(resources, cancellationToken));
    }

    /// <summary>
    /// The relationships an entity page draws, read off the clauses the step above loaded rather
    /// than left to a dataset's edge list. Immediately after it, because it reads nothing else, and
    /// after the dataset loaders because what a witness already states is half of the pick.
    /// </summary>
    private async Task RelateTheEntitiesOurOwnClausesRelate(CancellationToken cancellationToken)
    {
        status.Starting("the relationships this corpus reads for itself");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<OwnRelationshipLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// The names those lines put in a case: <em>тесть Мойсея</em>, not <em>тесть Мойсей</em>. After
    /// the descriptions, because what these forms are for is the entities those clauses name, and
    /// because a form the descriptor pass already wrote is the one left standing.
    /// </summary>
    private async Task DeclineTheNamesThoseLinesName(
        string resources,
        CancellationToken cancellationToken)
    {
        status.Starting("the names those descriptions put into a case");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<EntityNameFormLoader>();
        status.Record(await loader.Load(resources, cancellationToken));
    }

    /// <summary>
    /// The people the dataset wrote two records for, one list each, folded into one. After every pass
    /// that writes onto a record by the address a file gives it, because those files still name both,
    /// and before the passes that count and picture what the records hold, so they count one man.
    /// </summary>
    private async Task FoldTheRecordsWrittenTwice(CancellationToken cancellationToken)
    {
        status.Starting("the records the dataset wrote twice for one person");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<DuplicateRecordLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// What the passes read off a verse while the dataset filed it under the wrong man, moved to the
    /// man it names. After the fold, because a clause moved to him may repeat one a folded record
    /// brought, and before the passes that count and picture what the records hold.
    /// </summary>
    private async Task MoveWhatWasReadOffTheMisfiledVerses(CancellationToken cancellationToken)
    {
        status.Starting("what was read off the verses filed under the wrong man");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<RefiledTieLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// <em>The tribe of Naphtali</em>: the name after the Hebrew word for a tribe, read as the
    /// tribe. After every pass that names a Hebrew word, because it answers only where none of them
    /// did.
    /// </summary>
    private async Task NameTheTribesTheConstructNames(CancellationToken cancellationToken)
    {
        status.Starting("the tribes the Hebrew construct names");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<TribeNameLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// Two words of one verse given each other's names, put back where they belong. Last, because
    /// the spellings it reads are written by the step before it and the annotations it corrects are
    /// written by every naming step above.
    /// </summary>
    private async Task CrossBackTheNamesGivenToEachOther(CancellationToken cancellationToken)
    {
        status.Starting("the names two words of one verse were given of each other");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<CrossedNameLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// Every text's spellings of every name, counted from the words that name it. After every pass
    /// that names a word or corrects one, because it is a count of what they settled on and is
    /// rebuilt whole each time.
    /// </summary>
    private async Task CountHowEachTextSpellsEachName(CancellationToken cancellationToken)
    {
        status.Starting("how each text spells each name");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<EntityRenderingLoader>();
        status.Record(await loader.Load(cancellationToken));
    }

    /// <summary>
    /// One point for each place the gazetteer can locate under terms that allow it. After every step
    /// that creates, splits or joins a place, because it locates whatever records carry the
    /// gazetteer's identifier once they are settled.
    /// </summary>
    private async Task PutThePlacesOnTheMap(string resources, CancellationToken cancellationToken)
    {
        status.Starting("where the places are");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<OpenBibleLocationLoader>();
        status.Record(await loader.Load(Path.Combine(resources, "OpenBible"), cancellationToken));
    }

    /// <summary>
    /// The pictures of people and places, each with its credit. Last, because it pictures whatever
    /// records the steps before it settled on, found by slug and by the gazetteer's identifier.
    /// </summary>
    private async Task PictureThePeopleAndPlaces(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the pictures of people and places");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<EntityImageLoader>();
        status.Record(await loader.Load(resources, cancellationToken));
    }

    /// <summary>
    /// The 613 commandments as Maimonides counted them. Read from the output folder, like the world
    /// history, because the file is committed and never waits on a fetch.
    /// </summary>
    private async Task CountTheCommandments(CancellationToken cancellationToken)
    {
        status.Starting("the commandments");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<CommandmentLoader>();
        status.Record(await loader.Load(
            Path.Combine([AppContext.BaseDirectory, .. CommandmentLoader.FilePath]),
            cancellationToken));
    }

    /// <summary>The subjects of Nave's Topical Bible and the verses filed under each, on trial.</summary>
    private async Task FileTheVersesUnderNavesTopics(string resources, CancellationToken cancellationToken)
    {
        status.Starting("Nave's topics");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<NaveTopicLoader>();
        status.Record(await loader.Load(Path.Combine(resources, "BibleData2026"), cancellationToken));
    }

    private async Task Load(string what, Func<TextSource> read, CancellationToken cancellationToken)
    {
        status.Starting(what);
        var source = read();

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<CorpusLoader>();
        var loaded = await loader.Load(source, cancellationToken);
        status.Record(loaded);

        // A book the source gained after the text was loaded — Swete's Isaiah, which could not be
        // read until its own transcription was — goes into the text already there rather than
        // waiting for the whole text to be loaded again.
        if (loaded.AlreadyLoaded)
        {
            var added = await loader.AddMissingBooks(source, cancellationToken);
            if (added.Books.Count > 0)
            {
                status.Record(added);
            }
        }

        // What the edition calls its own verses, where its publisher renumbered it and it says so
        // in the text. Guarded on its own rows rather than on the text's, so it reaches a database
        // that already holds the text — which is every database the corpus has been loaded into.
        // Silent for a text that states nothing, which is most of them.
        using var numbering = services.CreateScope();
        var stated = numbering.ServiceProvider.GetRequiredService<StatedNumberLoader>();
        var outcome = await stated.Load(source, cancellationToken);
        if (outcome.Verses > 0 || outcome.AlreadyLoaded)
        {
            status.Record(outcome.ToString());
        }

        // Like stated verse numbers, notes must be a pass of their own: existing databases skip
        // the corpus load, while source notes are a new layer that belongs beside their verses.
        using var sourceNotes = services.CreateScope();
        var notes = sourceNotes.ServiceProvider.GetRequiredService<SourceNoteLoader>();
        var noteOutcome = await notes.Load(source, cancellationToken);
        if (noteOutcome.Notes > 0 || noteOutcome.AlreadyLoaded)
        {
            status.Record(noteOutcome.ToString());
        }

        // The same again for where the edition starts a paragraph or a line, which a text loaded
        // before the marks were read does not carry.
        using var paragraphing = services.CreateScope();
        var paragraphs = paragraphing.ServiceProvider.GetRequiredService<ParagraphMarkLoader>();
        var paragraphOutcome = await paragraphs.Mark(source, cancellationToken);
        if (paragraphOutcome.Marks > 0 || paragraphOutcome.AlreadyMarked)
        {
            status.Record(paragraphOutcome.ToString());
        }
    }
}
