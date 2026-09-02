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

            await Load("Brenton's Septuagint", () => SeptuagintTextSource.Read(
                Path.Combine(resources, "Septuagint")), stoppingToken);

            foreach (var translation in Bible4uTranslations)
            {
                await Load(translation, () => Bible4uTextSource.Read(
                    ResourcePaths.File(resources, "bible4u", $"{translation}.xml"), translation), stoppingToken);
            }

            await LoadTheLexicon(resources, stoppingToken);
            await LoadTheSyntax(bhsa, stoppingToken);
            await PlaceInTheFrame(resources, stoppingToken);
            await LinkTheOldTestament(resources, stoppingToken);
            await LinkTheNewTestament(resources, stoppingToken);
            await LinkThePrintedEditions(resources, stoppingToken);
            await LinkTheGreekWitnesses(stoppingToken);
            await GiveEveryWordASearchableForm(stoppingToken);
            await LinkFromTheInterlinear(resources, stoppingToken);
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

    /// <summary>
    /// The Greek texts the King James is matched against, in the order they are worth reading: the
    /// one it was translated from, then the one it was not.
    /// </summary>
    private static readonly string[] GreekWitnesses =
    [
        TextusReceptusTextSource.Slug(Edition.Scrivener1894),
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
        var frames = TvtmsReader.Read(ResourcePaths.File(resources, "Versification", "TVTMS.txt"));

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loader = scope.ServiceProvider.GetRequiredService<CanonicalFrameLoader>();

        foreach (var text in await db.Texts.OrderBy(t => t.Slug).ToListAsync(cancellationToken))
        {
            if (!frames.TryGetValue(text.Versification, out var frame))
            {
                logger.LogWarning(
                    "The text {Slug} follows {Versification} numbering, which the versification data does not " +
                    "cover, so it stays out of the shared frame and cannot be read beside another text",
                    text.Slug, text.Versification);
                continue;
            }

            status.Record(await loader.Place(text, frame, cancellationToken));
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
        status.Record(await loader.Load(records, cancellationToken));
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
    /// Nestle against the Textus Receptus, by the Strong numbers both editions state.
    ///
    /// Scrivener alone, because Stephanus already meets it word for word: a word carries the
    /// witness ids it reaches, so linking Nestle to Scrivener puts Scrivener's ids on both sides
    /// and joins all four Greek panes at once.
    /// </summary>
    private async Task LinkTheGreekWitnesses(CancellationToken cancellationToken)
    {
        status.Starting("the Greek witnesses to each other");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<GreekWitnessLinkLoader>();
        status.Record(await loader.Load("nestle1904", "scrivener1894", cancellationToken));
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
    /// The one stated word-level correspondence a Slavic text has. Everything else the Ukrainian
    /// reaches, it reaches through a model; this is people saying which word renders which.
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
    }

    /// <summary>
    /// The people, places and dated events the text names. DOC-0099 records why this dataset and
    /// not the others, and BibleDataLoader records what had to be corrected in it.
    /// </summary>
    private async Task LoadTheEncyclopedia(string resources, CancellationToken cancellationToken)
    {
        status.Starting("the encyclopedia");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<BibleDataLoader>();
        status.Record(await loader.Load(Path.Combine(resources, "BibleData2026"), cancellationToken));
    }

    private async Task Load(string what, Func<TextSource> read, CancellationToken cancellationToken)
    {
        status.Starting(what);
        var source = read();

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<CorpusLoader>();
        status.Record(await loader.Load(source, cancellationToken));
    }
}
