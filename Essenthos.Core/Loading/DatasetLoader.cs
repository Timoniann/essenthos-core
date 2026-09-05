using Essenthos.Core.Bhsa;
using Essenthos.Core.Configuration;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Database;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.TextusReceptus;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Verification;
using Microsoft.EntityFrameworkCore;
using Essenthos.Core.Loading.Encyclopedia;

namespace Essenthos.Core.Loading;

/// <summary>
/// Loads the witnesses at startup, in the background, so the API answers while it works. Each text
/// checks whether it is already there and does nothing if it is, so this runs on every boot.
///
/// A failure is logged as an error and recorded on <see cref="DatasetStatus"/> rather than being
/// swallowed: an empty database that is still filling and an empty database that gave up look
/// identical from the outside, and only one of them is worth waiting for.
/// </summary>
internal sealed class DatasetLoader(
    IServiceProvider services,
    IHostEnvironment environment,
    IConfiguration configuration,
    DatasetStatus status,
    ICanonIndex canon,
    ILogger<DatasetLoader> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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

            // The one Greek witness that is neither critical nor Erasmian. It is loaded from the
            // same shape of file as the two above and carries a Strong number on every word, so it
            // needs no reader of its own beyond an alphabet and no aligner at all.
            await Load("the Byzantine Textform", () => ByzantineTextSource.Read(
                Path.Combine(resources, "Byzantine")), stoppingToken);

            await Load("Brenton's Septuagint", () => SeptuagintTextSource.Read(
                Path.Combine(resources, "Septuagint")), stoppingToken);

            // The Torah as the Samaritan community transmitted it, which is the first text here
            // that disagrees with BHSA about the Hebrew rather than about a translation of it.
            await Load("the Samaritan Pentateuch", () => SamaritanTextSource.Read(
                Path.Combine(resources, "SamaritanPentateuch")), stoppingToken);

            // The Berean's own edition, because rebuilding it from the tables is right nine verses
            // in ten and a text that is right nine times in ten is not a text. The tables then say
            // which of its words renders which Greek word. FTR-0182.
            await Load("the Berean Standard Bible", () => BereanTextSource.Read(
                ResourcePaths.File(resources, "Berean", "bsb.txt")), stoppingToken);

            foreach (var translation in Bible4uTranslations)
            {
                await Load(translation, () => Bible4uTextSource.Read(
                    ResourcePaths.File(resources, "bible4u", $"{translation}.xml"), translation), stoppingToken);
            }

            await LoadTheLexicon(resources, stoppingToken);
            await LoadTheSyntax(bhsa, stoppingToken);
            await PlaceInTheFrame(resources, stoppingToken);
            await LemmatiseTheSeptuagint(resources, stoppingToken);
            await LinkTheOldTestament(resources, stoppingToken);
            await LinkTheNewTestament(resources, stoppingToken);
            await LinkTheBerean(resources, stoppingToken);
            await CorroborateTheBerean(resources, stoppingToken);
            await LinkThePrintedEditions(resources, stoppingToken);
            await LinkTheGreekWitnesses(stoppingToken);
            await LinkTheHebrewWitnesses(stoppingToken);
            await GiveEveryWordASearchableForm(stoppingToken);
            await JoinTheWordsThatArePrintedTogether(stoppingToken);
            await LinkFromTheInterlinear(resources, stoppingToken);
            await CoverThePsalmTitles(resources, stoppingToken);
            await JoinTheVerses(stoppingToken);
            await LoadTheEncyclopedia(resources, stoppingToken);

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
    /// Nestle and the Byzantine Textform against the Textus Receptus, by the Strong numbers all
    /// three state.
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

        foreach (var witness in new[] { NestleTextSource.Slug, ByzantineTextSource.Slug })
        {
            using var scope = services.CreateScope();
            var loader = scope.ServiceProvider.GetRequiredService<GreekWitnessLinkLoader>();
            status.Record(await loader.Load(
                witness, TextusReceptusTextSource.Slug(Edition.Scrivener1894), cancellationToken));
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
        status.Record(await loader.Load(
            Path.Combine(resources, "Door43", "uk_ubio"),
            "ukr",
            "unfoldingWord's Ukrainian Bible Interlinear Ogienko, git.door43.org/uk_ts/uk_ubio, CC BY-SA 4.0",
            cancellationToken));

        status.Starting("the Russian Synodal alignment");

        status.Record(await loader.Load(
            Path.Combine(resources, "Door43", "ru_rsb"),
            "rusv",
            "Door43 Russian Synodal alignment of Titus, Philemon and 2 John, made in "
            + "translationCore and published at git.door43.org under CC0 1.0",
            cancellationToken));
    }

    /// <summary>
    /// The one text that arrived without lemmas. GLAUx is used as a dictionary and its own Greek is
    /// never loaded; DOC-0161 is the licence reading and the reason that distinction matters.
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
    /// A second person's answer to the question the Berean's own tables answer. Mostly it agrees,
    /// and where it agrees it adds a claim rather than a link — which is the first time this corpus
    /// has been able to record that two independent methods reached the same word pair.
    /// </summary>
    private async Task CorroborateTheBerean(string resources, CancellationToken cancellationToken)
    {
        status.Starting("Clear Bible on the Berean");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<Links.ClearBibleLinkLoader>();
        status.Record(await loader.Load(Path.Combine(resources, "ClearBible"), cancellationToken));
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
    /// The people, places and dated events the text names. DOC-0099 records why this dataset and
    /// not the others, and BibleDataLoader records what had to be corrected in it.
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
    }

    private async Task Load(string what, Func<TextSource> read, CancellationToken cancellationToken)
    {
        status.Starting(what);
        var source = read();

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<CorpusLoader>();
        status.Record(await loader.Load(source, cancellationToken));

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
    }
}
