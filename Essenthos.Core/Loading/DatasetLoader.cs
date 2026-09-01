using Essenthos.Core.Configuration;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Database;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.TextusReceptus;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Verification;
using Microsoft.EntityFrameworkCore;

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

            await Load("BHSA", () => BhsaTextSource.Read(Path.Combine(resources, "etcbc")), stoppingToken);
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

            foreach (var translation in Bible4uTranslations)
            {
                await Load(translation, () => Bible4uTextSource.Read(
                    ResourcePaths.File(resources, "bible4u", $"{translation}.xml"), translation), stoppingToken);
            }

            await PlaceInTheFrame(resources, stoppingToken);
            await LinkTheOldTestament(resources, stoppingToken);
            await LinkTheNewTestament(resources, stoppingToken);

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
        status.Starting("the New Testament links");

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<NewTestamentLinkLoader>();
        status.Record(await loader.Load(
            ResourcePaths.File(resources, "Zefania", "SF_2009-01-20_ENG_KJV_(KJV+).xml"), cancellationToken));
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

    private async Task Load(string what, Func<TextSource> read, CancellationToken cancellationToken)
    {
        status.Starting(what);
        var source = read();

        using var scope = services.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<CorpusLoader>();
        status.Record(await loader.Load(source, cancellationToken));
    }
}
