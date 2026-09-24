using Essenthos.Core.ClearBible;
using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Loading.Links.Evidentia;
using Essenthos.Core.Publishing;
using Essenthos.Core.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

// Everything that reads a source file and writes the corpus: the loaders, the aligners, EVIDENTIA,
// and the verification pass. None of it is reachable from the API, which is the point of it being a
// separate assembly — a server that serves the corpus has no business holding a parser, and a
// boundary the compiler checks is the only kind that holds.
//
// Every verb below is a batch run. There is no background service here and no request to answer:
// the process starts, does one thing, says what it did, and exits with a status a script can read.

// A report carrying Hebrew, Greek and an arrow is unreadable in the console's ANSI code page, and
// redirecting it to a file only moves the question marks.
Console.OutputEncoding = Encoding.UTF8;

// The content root is the folder the assembly sits in, not the directory somebody happened to run
// this from. A console host would otherwise take the working directory, and then appsettings.json is
// found or not depending on where the shell was, and a container finds neither it nor Resources/.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// The database password is deliberately not in appsettings.json, so development reads it from user
// secrets — whatever the environment, and beneath the environment and the command line, so either can
// still override it. See UserSecrets.
UserSecrets.AddBelowEnvironment(builder.Configuration, typeof(Program).Assembly);

// Read here, once, rather than inside an options callback that runs later, so that a missing
// password stops the process at startup with the message that says what to set.
var databaseConnection = DatabaseConnection.Read(builder.Configuration);

builder.Services.AddDbContext<AppDbContext>(optionsBuilder =>
{
    optionsBuilder.UseNpgsql(databaseConnection);
});

builder.Services.AddScoped<CorpusLoader>();
builder.Services.AddScoped<StatedNumberLoader>();
builder.Services.AddScoped<SourceNoteLoader>();
builder.Services.AddScoped<ParagraphMarkLoader>();
builder.Services.AddScoped<MorphGntParsingLoader>();
builder.Services.AddScoped<MaculaAnnotationLoader>();
builder.Services.AddScoped<CanonicalFrameLoader>();
builder.Services.AddScoped<SuperscriptionFrameLoader>();
builder.Services.AddScoped<PsalmOpeningLoader>();
builder.Services.AddScoped<SweteRestorationLoader>();
builder.Services.AddScoped<Essenthos.Core.Loading.Links.OldTestamentLinkLoader>();
builder.Services.AddScoped<Essenthos.Core.Loading.Links.NewTestamentLinkLoader>();
builder.Services.AddScoped<AlignmentPipeline>();
builder.Services.AddSingleton<ILanguagePack, EnglishLanguagePack>();
builder.Services.AddSingleton<ILanguagePack, SlavicLanguagePack>();
builder.Services.AddSingleton<ILanguagePack, OriginalLanguagePack>();
builder.Services.AddSingleton<LanguagePackRegistry>();
builder.Services.AddScoped<EvidentiaPipeline>();
builder.Services.AddScoped<EvidentiaCorpusPreviewLoader>();
builder.Services.AddScoped<EvidentiaRunner>();
builder.Services.AddScoped<EvidentiaReviewQueue>();
builder.Services.AddScoped<EvidentiaLinkWriter>();
builder.Services.AddScoped<EvidentiaProblemVerses>();
builder.Services.AddSingleton<EvidentiaStrongProposalResolver>();
builder.Services.AddSingleton<EvidentiaKnownRenderingProposalResolver>();
builder.Services.AddSingleton<EvidentiaTargetGlossProposalResolver>();
builder.Services.AddSingleton<EvidentiaDictionaryProposalResolver>();
builder.Services.AddSingleton<EvidentiaSyntaxReviewGate>();
builder.Services.AddSingleton<UdpipeAnnotator>();
builder.Services.AddSingleton<EvidentiaFileSourceTexts>();
builder.Services.AddScoped<EvidentiaDictionarySenseIndex>();
builder.Services.AddScoped<EvidentiaKnownRenderingIndex>();
builder.Services.AddSingleton<IEvidentiaEvidenceSource, StrongNumberEvidenceSource>();
builder.Services.AddScoped<CompositionPipeline>();
builder.Services.AddScoped<NameListPass>();
builder.Services.AddScoped<CorpusCheck>();
builder.Services.AddScoped<StrongLexiconLoader>();
builder.Services.AddScoped<StrongGentilicLoader>();
builder.Services.AddScoped<StrongTranslationLoader>();
builder.Services.AddScoped<GreekGlossLoader>();
builder.Services.AddScoped<SyntaxLoader>();
builder.Services.AddScoped<PrintedEditionLinkLoader>();
builder.Services.AddScoped<GreekWitnessLinkLoader>();
builder.Services.AddScoped<SamaritanLinkLoader>();
builder.Services.AddScoped<SeptuagintLinkLoader>();
builder.Services.AddScoped<WordFoldingLoader>();
builder.Services.AddScoped<GraphicalWordLoader>();
builder.Services.AddScoped<Essenthos.Core.Glaux.GlauxLemmaLoader>();
builder.Services.AddScoped<Essenthos.Core.Glaux.SeptuagintStrongLoader>();
builder.Services.AddScoped<InterlinearLinkLoader>();
builder.Services.AddScoped<BereanLinkLoader>();
builder.Services.AddScoped<ClearBibleLinkLoader>();
builder.Services.AddScoped<TaggedTextLinkLoader>();
builder.Services.AddScoped<SynodalStrongLinkLoader>();
builder.Services.AddScoped<VerseLinkLoader>();
builder.Services.AddScoped<BibleDataLoader>();
builder.Services.AddScoped<UssherAnnalsLoader>();
builder.Services.AddScoped<OpenBiblePlaceLoader>();
builder.Services.AddScoped<OpenBibleLocationLoader>();
builder.Services.AddScoped<EntityImageLoader>();
builder.Services.AddScoped<WorldHistoryLoader>();
builder.Services.AddScoped<SeptuagintReckoningLoader>();
builder.Services.AddScoped<PeopleLoader>();
builder.Services.AddScoped<PlaceRegisterLoader>();
builder.Services.AddScoped<PersonRegisterLoader>();
builder.Services.AddScoped<EntityAnnotationLoader>();
builder.Services.AddScoped<OwnRecordLoader>();
builder.Services.AddScoped<SenseReadingLoader>();
builder.Services.AddScoped<SiteSplitLoader>();
builder.Services.AddScoped<GreekNamesakeLoader>();
builder.Services.AddScoped<ListedBearerLoader>();
builder.Services.AddScoped<HebrewOriginNameLoader>();
builder.Services.AddScoped<RenderedNameLoader>();
builder.Services.AddScoped<TribeNameLoader>();
builder.Services.AddScoped<CrossedNameLoader>();
builder.Services.AddScoped<AnnotationCarrier>();
builder.Services.AddScoped<SoleBearerLoader>();
builder.Services.AddScoped<TermLoader>();
builder.Services.AddScoped<TitleLoader>();
builder.Services.AddScoped<OwnNameLoader>();
builder.Services.AddScoped<ThingLoader>();
builder.Services.AddScoped<OwnReferenceLoader>();
builder.Services.AddSingleton(_ => ReviewLists.Read(builder.Configuration));
builder.Services.AddScoped<MisfiledVerseLoader>();
builder.Services.AddScoped<EntityDescriptorLoader>();
builder.Services.AddScoped<CommandmentLoader>();
builder.Services.AddScoped<NaveTopicLoader>();
builder.Services.AddScoped<EntityNameFormLoader>();
builder.Services.AddScoped<EntityRenderingLoader>();
builder.Services.AddScoped<OwnRelationshipLoader>();
builder.Services.AddScoped<DuplicateRecordLoader>();
builder.Services.AddScoped<RefiledTieLoader>();
builder.Services.AddSingleton<DatasetStatus>();
builder.Services.AddSingleton<ICanonIndex, CanonIndex>();
builder.Services.AddScoped<DatasetLoader>();
builder.Services.AddScoped<Publisher>();

// Disposed on every return path, not only the one that serves. Draining the console logger's
// background queue is what disposal does, and a command that returns without it loses whatever is
// still queued — silently, and more of it the longer the report.
using var app = builder.Build();

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("forge");
var configuration = app.Services.GetRequiredService<IConfiguration>();
var environment = app.Services.GetRequiredService<IHostEnvironment>();
var resources = ResourcePaths.Read(configuration, environment.ContentRootPath);

// The load itself, which the API used to run in the background while it served. It is a command
// here because that is what it always was: hours of parsing that ends, against a database nobody is
// reading yet. Each source checks whether it is already there and does nothing if it is, so running
// it twice is how a new witness is added.
if (args is [] or ["load", ..])
{
    using var loadScope = app.Services.CreateScope();
    await loadScope.ServiceProvider.GetRequiredService<DatasetLoader>().Run(CancellationToken.None);
    return 0;
}


// A text identifier as the corpus spells it, from one typed at a shell in whatever case came to
// hand. The pipelines below compare it to the column, and the workspace they leave in the temp
// folder is named after it: without this, `align kjv bhsa` finds no text, and on a file system that
// tells KJV-BHSA from kjv-bhsa the same pair would train a second model beside the first.
static string Identifier(string typed) => typed.ToUpperInvariant();

// Alignment is computed once per pair of texts, not per request, so it is a batch run rather than
// part of the startup pipeline: an API that trains a model before it answers is the shape that
// leaves a cold start answering 404 to everything, with nothing saying it is still working.
// What a threshold costs, on the one pair where a source says what the right answer is. It reuses
// the alignment in the workspace, so a sweep is seconds once the model has been run.
// The second route to the same word, through a text whose own links to the target are stated.
// Russian against Hebrew is one hard hop; Russian against the King James is an easy one, and the
// King James against BHSA is not a hop at all.
if (args is ["compose", var composeFrom, var composeVia, var composeTo, ..])
{
    using var composeScope = app.Services.CreateScope();
    var composer = composeScope.ServiceProvider.GetRequiredService<CompositionPipeline>();
    // One middle text or two, named together: KJV,BSB.
    var composeVias = composeVia.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Identifier).ToList();
    var least = Array.IndexOf(args, "--min");

    // How often each combination of readings must have named the stated word to be written at all.
    var composePrecision = Option(args, "--precision") is { } bar
        ? double.Parse(bar, System.Globalization.CultureInfo.InvariantCulture)
        : Admission.DefaultPrecision;
    var composeMinimum = least >= 0 && least + 1 < args.Length
        ? double.Parse(args[least + 1], System.Globalization.CultureInfo.InvariantCulture)
        : AlignmentPipeline.DefaultMinimumConfidence;

    // A trial: the same three readings and the same merge, scored against what the corpus holds and
    // written nowhere. --books trains on a few canonical books alone, which is the cheap first look;
    // --explain <file> writes every answer each reading gave, for asking why a word was left bare.
    if (args.Contains("--dry-run"))
    {
        logger.LogInformation("\n{Report}", await composer.Measure(
            Identifier(composeFrom),
            composeVias,
            Identifier(composeTo),
            composeMinimum,
            Option(args, "--books") is { } composeBooks
                ? composeBooks.Split(',').Select(int.Parse).ToHashSet()
                : null,
            composePrecision,
            Option(args, "--explain")));
        return 0;
    }

    logger.LogInformation("{Outcome}", await composer.Run(
        Identifier(composeFrom),
        composeVias,
        Identifier(composeTo),
        composeMinimum,
        composePrecision,
        args.Contains("--unmeasured")));
    return 0;
}

// The measures as a command, so a build can fail on them. The floor is set below where the corpus
// already stands: its job is to catch a load that lost something, not to be an aspiration.
if (args is ["verify", ..])
{
    using var verifyScope = app.Services.CreateScope();
    var check = verifyScope.ServiceProvider.GetRequiredService<CorpusCheck>();
    var measures = await check.Measure();
    var floor = Array.IndexOf(args, "--floor") is var at and >= 0 && at + 1 < args.Length
        ? double.Parse(args[at + 1], System.Globalization.CultureInfo.InvariantCulture)
        : CorpusCheck.RenderedFloor;

    logger.LogInformation("\n{Report}", measures.Describe());
    return CorpusGate.Pass(measures, floor, logger) ? 0 : 1;
}

// A corpus release, and moving one to a server. See Publishing/Publisher.cs for the whole design;
// in short: `release` verifies and dumps this machine's corpus into .releases/, `publish` restores
// that file into a new database on a target, verifies it there, and only then swaps it in. Each of
// release, publish and rollback takes --dry-run, which says what it would do and changes nothing.
if (args is ["release", ..])
{
    using var releaseScope = app.Services.CreateScope();
    return await releaseScope.ServiceProvider.GetRequiredService<Publisher>()
        .Release(args.Contains("--allow-dirty"), CancellationToken.None, dryRun: args.Contains("--dry-run"));
}

if (args is ["publish", ..])
{
    using var publishScope = app.Services.CreateScope();
    return await publishScope.ServiceProvider.GetRequiredService<Publisher>().Publish(
        Option(args, "--to") ?? throw new InvalidOperationException("forge publish --to <target> [--release <name>]"),
        Option(args, "--release"),
        args.Contains("--without-rehearsal"),
        CancellationToken.None,
        dryRun: args.Contains("--dry-run"));
}

if (args is ["rollback", ..])
{
    using var rollbackScope = app.Services.CreateScope();
    return await rollbackScope.ServiceProvider.GetRequiredService<Publisher>().Rollback(
        Option(args, "--to") ?? throw new InvalidOperationException("forge rollback --to <target>"),
        CancellationToken.None,
        dryRun: args.Contains("--dry-run"));
}

if (args is ["releases", ..])
{
    using var releasesScope = app.Services.CreateScope();
    return await releasesScope.ServiceProvider.GetRequiredService<Publisher>().List(
        Option(args, "--on"), CancellationToken.None);
}

static string? Option(string[] args, string name) =>
    Array.IndexOf(args, name) is var at and >= 0 && at + 1 < args.Length ? args[at + 1] : null;

// `--suppletion` scores the Slavic texts with the closed-class table switched on, which is how
// what that table is worth stays a measurement rather than an opinion. It gets its own workspace
// because the reduction changes the tokens the model trains on, and reusing the other one would
// score the wrong run.
if (args is ["score", var scoreFrom, var scoreTo, ..])
{
    using var scoreScope = app.Services.CreateScope();
    var scorer = scoreScope.ServiceProvider.GetRequiredService<AlignmentPipeline>();
    var scoreOne = Identifier(scoreFrom);
    var scoreTwo = Identifier(scoreTo);
    logger.LogInformation("\n{Report}", await scorer.Measure(
        scoreOne,
        scoreTwo,
        Path.Combine(Path.GetTempPath(), "essenthos-align",
            $"{scoreOne}-{scoreTwo}{(args.Contains("--surface") ? "-surface" : string.Empty)}" +
            $"{(args.Contains("--suppletion") ? "-suppletion" : string.Empty)}"),
        args.Contains("--min")
            ? [.. args[Array.IndexOf(args, "--min") + 1].Split(',')
                .Select(t => double.Parse(t, System.Globalization.CultureInfo.InvariantCulture))]
            : [0.25, 0.40],
        args.Contains("--model") ? args[Array.IndexOf(args, "--model") + 1] : "ibm4",
        args.Contains("--surface"),
        args.Contains("--stated"),
        args.Contains("--suppletion")));
    return 0;
}

// The Door43 join a benchmark's Slavic answer key rests on, re-made from the files without writing
// anything: which verses, spans and words arrived, why the rest did not, and whether the links the
// corpus holds are still this join. With --replace the join is then written over the stored rows,
// which the startup load never does for a text it has already linked.
if (args is ["interlinear-join", var interlinearText, ..])
{
    var interlinearSlug = Identifier(interlinearText);
    using var interlinearScope = app.Services.CreateScope();
    var interlinearLoader = interlinearScope.ServiceProvider.GetRequiredService<InterlinearLinkLoader>();
    var interlinearFolder = InterlinearFolder(resources, interlinearSlug);
    logger.LogInformation("\n{Report}", await interlinearLoader.Measure(interlinearFolder, interlinearSlug));

    if (args.Contains("--replace"))
    {
        logger.LogInformation("{Outcome}", await interlinearLoader.Replace(
            interlinearFolder, interlinearSlug, InterlinearLinkLoader.Interlinear(interlinearSlug).Source));

        // A stated link across a verse boundary the frame does not join is a verse pair the source
        // states, and a command that cannot be followed by a restart has to write it itself.
        logger.LogInformation(
            "{Outcome}", await interlinearScope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());
        logger.LogInformation("\n{Report}", await interlinearLoader.Measure(interlinearFolder, interlinearSlug));
    }

    return 0;
}

// Read one real chapter through the deterministic evidence graph. Unlike `align`, this command
// never writes links: its result tells us whether the current language packs have enough evidence
// to justify a future mapping pass and exactly where IBM fallback would be required.
if (args is ["evidentia-preview", var previewFrom, var previewTo, var previewBook, var previewChapter, ..])
{
    if (!int.TryParse(previewBook, out var book) || !int.TryParse(previewChapter, out var chapter))
    {
        throw new ArgumentException("evidentia-preview needs numeric canonical book and chapter.");
    }

    var verseIndex = Array.IndexOf(args, "--verse");
    int? verse = verseIndex >= 0 && verseIndex + 1 < args.Length
        ? int.Parse(args[verseIndex + 1])
        : null;
    using var previewScope = app.Services.CreateScope();
    var preview = await previewScope.ServiceProvider.GetRequiredService<EvidentiaCorpusPreviewLoader>().Preview(
        Identifier(previewFrom), Identifier(previewTo), book, chapter, verse,
        allowSourceStrongEvidence: !args.Contains("--without-source-strong"),
        allowKnownRenderingEvidence: !args.Contains("--without-known-renderings"));
    logger.LogInformation("\n{Preview}", preview);
    return 0;
}

if (args is ["evidentia-measure", var measureFrom, var measureTo, var measureBook, var measureChapter, ..])
{
    if (!int.TryParse(measureBook, out var book) || !int.TryParse(measureChapter, out var chapter))
    {
        throw new ArgumentException("evidentia-measure needs numeric canonical book and chapter.");
    }

    using var measureScope = app.Services.CreateScope();
    var measurement = await measureScope.ServiceProvider.GetRequiredService<EvidentiaCorpusPreviewLoader>().MeasureChapter(
        Identifier(measureFrom), Identifier(measureTo), book, chapter, EvidentiaOptions(args, resources));
    await WriteRows(args, "--disagreements", measurement.Disagreements);
    await WriteRows(args, "--words", measurement.Words);
    await WriteRows(args, "--absences", measurement.Absences);
    logger.LogInformation("\n{Measurement}", measurement);
    return 0;
}

if (args is ["evidentia-measure-book", var measureBookFrom, var measureBookTo, var measureBookOrdinal, ..])
{
    if (!int.TryParse(measureBookOrdinal, out var book))
    {
        throw new ArgumentException("evidentia-measure-book needs a numeric canonical book.");
    }

    using var measureBookScope = app.Services.CreateScope();
    var fromChapter = OptionalInt(args, "--from-chapter");
    var toChapter = OptionalInt(args, "--to-chapter");
    if (fromChapter.HasValue && toChapter.HasValue && fromChapter > toChapter)
    {
        throw new ArgumentException("evidentia-measure-book needs --from-chapter less than or equal to --to-chapter.");
    }
    var measurement = await measureBookScope.ServiceProvider.GetRequiredService<EvidentiaCorpusPreviewLoader>().MeasureBook(
        Identifier(measureBookFrom), Identifier(measureBookTo), book, EvidentiaOptions(args, resources),
        firstChapter: fromChapter,
        lastChapter: toChapter);
    await WriteRows(args, "--disagreements", measurement.Disagreements);
    await WriteRows(args, "--words", measurement.Words);
    await WriteRows(args, "--absences", measurement.Absences);
    logger.LogInformation("\n{Measurement}", measurement);
    return 0;
}

// A stored run: the same measurement, with every decision kept under a run id so it can be ranked,
// reviewed and compared. It writes the run's own tables and nothing of the corpus.
if (args is ["evidentia-run", var runFrom, var runTo, ..])
{
    var scopes = Positional(args, 3).Select(EvidentiaBookScope.Parse).ToList();
    if (scopes.Count == 0)
    {
        throw new ArgumentException(
            "evidentia-run needs at least one canonical book, optionally with chapters: evidentia-run BSB BHSA 1:1-10 8 32.");
    }

    using var runScope = app.Services.CreateScope();
    logger.LogInformation("\n{Outcome}", await runScope.ServiceProvider.GetRequiredService<EvidentiaRunner>()
        .Run(Identifier(runFrom), Identifier(runTo), scopes, EvidentiaOptions(args, resources)));
    return 0;
}

if (args is ["evidentia-runs", ..])
{
    using var runsScope = app.Services.CreateScope();
    logger.LogInformation("\n{Runs}", await runsScope.ServiceProvider.GetRequiredService<EvidentiaRunner>().List());
    return 0;
}

if (args is ["evidentia-queue", var queueRun, ..])
{
    using var queueScope = app.Services.CreateScope();
    logger.LogInformation("\n{Queue}", await queueScope.ServiceProvider.GetRequiredService<EvidentiaReviewQueue>()
        .Pending(int.Parse(queueRun), QueueFilter(args)));
    return 0;
}

// A verdict is recorded and goes no further; evidentia-apply is the only thing that writes a link.
if (args is ["evidentia-approve" or "evidentia-reject", ..])
{
    using var verdictScope = app.Services.CreateScope();
    var queue = verdictScope.ServiceProvider.GetRequiredService<EvidentiaReviewQueue>();
    var ids = Positional(args, 1).Select(long.Parse).ToList();
    var reviewer = OptionalText(args, "--reviewer") ?? string.Empty;
    var approving = args[0] == "evidentia-approve";
    var settled = approving
        ? await queue.Approve(ids, reviewer, OptionalText(args, "--note"))
        : await queue.Reject(ids, reviewer, OptionalText(args, "--note"));
    logger.LogInformation(
        "{Settled} decisions {Verdict}; nothing reaches the corpus until evidentia-apply --write",
        settled, approving ? "approved" : "rejected");
    return 0;
}

if (args is ["evidentia-correct", var correctDecision, var correctTarget, ..])
{
    using var correctScope = app.Services.CreateScope();
    await correctScope.ServiceProvider.GetRequiredService<EvidentiaReviewQueue>().Correct(
        long.Parse(correctDecision), long.Parse(correctTarget),
        OptionalText(args, "--reviewer") ?? string.Empty, OptionalText(args, "--note"));
    logger.LogInformation(
        "Decision {Decision} corrected to word {Target}; nothing reaches the corpus until evidentia-apply --write",
        correctDecision, correctTarget);
    return 0;
}

if (args is ["evidentia-accept-tier", var acceptRun, ..])
{
    using var acceptScope = app.Services.CreateScope();
    var accepted = await acceptScope.ServiceProvider.GetRequiredService<EvidentiaReviewQueue>().AcceptTier(
        int.Parse(acceptRun), QueueFilter(args), OptionalText(args, "--reviewer") ?? string.Empty,
        OptionalText(args, "--note"));
    logger.LogInformation(
        "{Accepted} proposals accepted with their tier, unread. They will be written as rule-based claims, never as a " +
        "person's, and only by evidentia-apply --write", accepted);
    return 0;
}

// The one command that writes EVIDENTIA's output into the corpus, and only what a reviewer approved
// or corrected. Without --write it reports what it would write and writes nothing.
if (args is ["evidentia-apply", var applyRun, ..])
{
    using var applyScope = app.Services.CreateScope();
    logger.LogInformation("\n{Outcome}", await applyScope.ServiceProvider.GetRequiredService<EvidentiaLinkWriter>()
        .Apply(int.Parse(applyRun), args.Contains("--write")));
    return 0;
}

if (args is ["evidentia-problems", var problemRuns, ..])
{
    using var problemScope = app.Services.CreateScope();
    logger.LogInformation("\n{Problems}", await problemScope.ServiceProvider.GetRequiredService<EvidentiaProblemVerses>()
        .Worst(
            [.. problemRuns.Split(',').Select(int.Parse)],
            OptionalInt(args, "--take") ?? EvidentiaProblemVerses.DefaultTake,
            OptionalInt(args, "--min-words") ?? EvidentiaProblemVerses.DefaultMinimumContentWords,
            args.Contains("--flagged")));
    return 0;
}

if (args is ["evidentia-rerun", var rerunRun, ..])
{
    using var rerunScope = app.Services.CreateScope();
    logger.LogInformation("\n{Rerun}", await rerunScope.ServiceProvider.GetRequiredService<EvidentiaProblemVerses>()
        .Rerun(
            int.Parse(rerunRun),
            OptionalInt(args, "--take") ?? EvidentiaProblemVerses.DefaultTake,
            OptionalInt(args, "--min-words") ?? EvidentiaProblemVerses.DefaultMinimumContentWords));
    return 0;
}

if (args is ["evidentia-compare", var compareBefore, var compareAfter, ..])
{
    using var compareScope = app.Services.CreateScope();
    logger.LogInformation("\n{Comparison}", await compareScope.ServiceProvider.GetRequiredService<EvidentiaProblemVerses>()
        .Compare(int.Parse(compareBefore), int.Parse(compareAfter)));
    return 0;
}

// The arguments from a position up to the first option.
static IEnumerable<string> Positional(string[] arguments, int from) =>
    arguments.Skip(from).TakeWhile(argument => !argument.StartsWith("--", StringComparison.Ordinal));

static EvidentiaQueueFilter QueueFilter(string[] arguments) => new(
    Tier: OptionalText(arguments, "--tier"),
    Book: OptionalInt(arguments, "--book"),
    Chapter: OptionalInt(arguments, "--chapter"),
    Verse: OptionalInt(arguments, "--verse"),
    Take: OptionalInt(arguments, "--take") ?? EvidentiaReviewQueue.DefaultTake);

// Contradicted proposals and per-word outcomes go to files rather than to the report, because they
// are read one at a time and there are thousands of them: a classification pass or a comparison of
// two runs needs the rows the aggregate counted, and a console report is neither a stable record of
// them nor wide enough to hold a whole verse.
static async Task WriteRows<T>(string[] arguments, string option, IReadOnlyList<T> rows)
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

static int? OptionalInt(string[] arguments, string option)
{
    var index = Array.IndexOf(arguments, option);
    return index >= 0 && index + 1 < arguments.Length ? int.Parse(arguments[index + 1]) : null;
}

static string? OptionalText(string[] arguments, string option)
{
    var index = Array.IndexOf(arguments, option);
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
}

// A measurement is only worth the isolation it can state, so every one of these is a way of saying
// which evidence the run was allowed to see and which answer key it was scored against.
// A Door43 interlinear, by the text it aligns: the only two this corpus has.
static string InterlinearFolder(string resourcesPath, string slug) => Path.Combine(
    resourcesPath,
    "Door43",
    InterlinearLinkLoader.Interlinear(slug).Folder);

static EvidentiaMeasurementOptions EvidentiaOptions(string[] arguments, string resourcesPath) => new(
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
    GoldInterlinear: arguments.Contains("--gold-interlinear")
        ? InterlinearFolder(resourcesPath, Identifier(arguments[1]))
        : null,
    LearnAcrossLanguages: arguments.Contains("--learn-across-languages"),
    SourceFromFiles: arguments.Contains("--source-from-files"),
    RouteTexts: OptionalText(arguments, "--routes") is { } routes
        ? [.. routes.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Identifier)]
        : null);

// Unlike `score`, this is an out-of-sample test: only 80% of the stated and Strong one-to-one pairs
// reach SIL.Machine as its partial-alignment corpus, and a deterministic fifth of verses stays out
// of both that file and the source-stated score. The result is therefore an improvement measure,
// not the model repeating the key it was handed.
if (args is ["score-anchors", var anchorFrom, var anchorTo, ..])
{
    using var anchorScope = app.Services.CreateScope();
    var scorer = anchorScope.ServiceProvider.GetRequiredService<AlignmentPipeline>();
    var anchorOne = Identifier(anchorFrom);
    var anchorTwo = Identifier(anchorTo);
    var anchorModel = args.Contains("--model") ? args[Array.IndexOf(args, "--model") + 1] : "ibm4";
    var anchorFold = args.Contains("--fold") ? int.Parse(args[Array.IndexOf(args, "--fold") + 1]) : 0;
    logger.LogInformation("\n{Report}", await scorer.MeasureAnchors(
        anchorOne,
        anchorTwo,
        Path.Combine(Path.GetTempPath(), "essenthos-align",
            $"{anchorOne}-{anchorTwo}-held-out-strong-anchors-{anchorModel}-fold-{anchorFold}"),
        args.Contains("--min")
            ? [.. args[Array.IndexOf(args, "--min") + 1].Split(',')
                .Select(t => double.Parse(t, System.Globalization.CultureInfo.InvariantCulture))]
            : [0.25, 0.40],
        anchorModel,
        anchorFold));
    return 0;
}

// What the target text's own syntax is worth as a check on the model, before it is believed: every
// proposal the model made, bucketed by how it sits among its neighbours' answers, against what a
// source states. The last column is the weight the rescorer uses, so revising it is a reading.
if (args is ["syntax", var syntaxFrom, var syntaxTo, ..])
{
    using var syntaxScope = app.Services.CreateScope();
    var prior = syntaxScope.ServiceProvider.GetRequiredService<AlignmentPipeline>();
    var syntaxOne = Identifier(syntaxFrom);
    var syntaxTwo = Identifier(syntaxTo);
    logger.LogInformation("\n{Report}", await prior.Diagnose(
        syntaxOne,
        syntaxTwo,
        Path.Combine(Path.GetTempPath(), "essenthos-align", $"{syntaxOne}-{syntaxTwo}"),
        args.Contains("--model") ? args[Array.IndexOf(args, "--model") + 1] : "ibm4",
        args.Contains("--stated")));
    return 0;
}

// A translation that arrived carrying its own Strong numbers, matched to a witness that carries
// them too — the one route Luther 1912 has to the originals that is not our own inference. It is a
// batch run for the same reason `align` is: which pairs are worth drawing is a judgement about the
// texts, and a text tagged in one series says nothing about the other.
if (args is ["strong", var strongFrom, var strongTo, ..])
{
    using var strongScope = app.Services.CreateScope();
    var tagged = strongScope.ServiceProvider.GetRequiredService<TaggedTextLinkLoader>();
    logger.LogInformation(
        "{Outcome}", await tagged.Load(Identifier(strongFrom), Identifier(strongTo)));

    // The verse links for the pair just written. The startup pipeline does this for pairs the
    // alignment commands leave behind, and a command that cannot be followed by a restart has to
    // do it itself: without them every word link of a new pair reads as crossing a verse boundary
    // nothing backs, which is an integrity check the corpus keeps at zero.
    logger.LogInformation(
        "{Outcome}", await strongScope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());
    return 0;
}

// The Synodal by Bob Jones University's Strong numbering, read from the edition and never stored:
// the links are written and the numbers are gone when the command returns. With no witness named it
// runs all five the numbering reaches.
if (args is ["synodal-strong", ..])
{
    using var synodalScope = app.Services.CreateScope();
    var editionPath = ResourcePaths.File(
        resources,
        SynodalStrongLinkLoader.EditionFile);
    string[] witnesses = args.Length > 1 ? [.. args[1..].Select(Identifier)] : SynodalStrongLinkLoader.Witnesses;

    await synodalScope.ServiceProvider.GetRequiredService<SynodalStrongLinkLoader>().Load(editionPath, witnesses);
    logger.LogInformation(
        "{Outcome}", await synodalScope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());

    // The links just written and the guesses just removed decide which Synodal words an annotation
    // reaches, and nothing on a restart asks that again.
    await synodalScope.ServiceProvider.GetRequiredService<AnnotationCarrier>().Carry();
    return 0;
}

// Where the places are, for a corpus loaded before the gazetteer's points were. The load does the
// same as its last encyclopedia step; this is that step alone, so a full corpus is not read again
// to add thirteen hundred rows.
if (args is ["locate", ..])
{
    using var locateScope = app.Services.CreateScope();
    var located = await locateScope.ServiceProvider.GetRequiredService<OpenBibleLocationLoader>()
        .Load(Path.Combine(resources, "OpenBible"));
    Console.WriteLine(located);
    return 0;
}

// The pictures of people and places, drawn again from the images folder and its manifests in an
// already loaded corpus. The load does the same as its last encyclopedia step; this is that step
// alone, so a new portrait or a corrected credit arrives without reading the corpus again.
if (args is ["images", ..])
{
    using var imagesScope = app.Services.CreateScope();
    Console.WriteLine(await imagesScope.ServiceProvider.GetRequiredService<EntityImageLoader>().Load(resources));
    return 0;
}

// How each text spells each name, counted again from the words that name it, in an already loaded
// corpus. The load does this as its last naming step; this is that step alone.
if (args is ["spell", ..])
{
    using var spellScope = app.Services.CreateScope();
    Console.WriteLine(await spellScope.ServiceProvider.GetRequiredService<EntityRenderingLoader>().Load());
    return 0;
}

// The commandments and Nave's topics, for a corpus loaded before either was. The load does both as
// its last steps; these are those steps alone.
if (args is ["commandments", ..])
{
    using var commandmentScope = app.Services.CreateScope();
    Console.WriteLine(await commandmentScope.ServiceProvider.GetRequiredService<CommandmentLoader>()
        .Load(Path.Combine([AppContext.BaseDirectory, .. CommandmentLoader.FilePath])));
    return 0;
}

if (args is ["topics", ..])
{
    using var topicScope = app.Services.CreateScope();
    Console.WriteLine(await topicScope.ServiceProvider.GetRequiredService<NaveTopicLoader>()
        .Load(Path.Combine(resources, "BibleData2026")));
    return 0;
}

// The person register on a corpus already built: the verses a dataset filed under the wrong man put
// back, and the bearers of those names matched again with the verses where they now stand. The load
// does the same at its own step; run it before fold-records, which folds what it leaves twice.
if (args is ["persons", ..])
{
    using var personScope = app.Services.CreateScope();
    Console.WriteLine(await personScope.ServiceProvider.GetRequiredService<PersonRegisterLoader>().Load(resources));
    return 0;
}

// The records a dataset wrote twice for one person, folded into one, without the rest of the load.
// The load does the same at its place in the pipeline; this is for a corpus already built.
if (args is ["fold-records", ..])
{
    using var foldScope = app.Services.CreateScope();
    Console.WriteLine(await foldScope.ServiceProvider.GetRequiredService<DuplicateRecordLoader>().Load());
    return 0;
}

// Every annotation a pass carried into another text, carried again over the links as they stand.
// For a corpus whose links were changed by something that did not carry them itself.
if (args is ["carry", ..])
{
    using var carryScope = app.Services.CreateScope();
    await carryScope.ServiceProvider.GetRequiredService<AnnotationCarrier>().Carry();
    return 0;
}

// A stated mapping drawn again from its file, after a change to how the file is read. The rows that
// file wrote are withdrawn and loaded afresh, and the verse links and carried annotations are brought
// up to the links as they then stand. `redraw berean BHSA` for the tables' Hebrew half,
// `redraw clearbible BSB` for Clear Bible's sets on one translation. Redrawing the Berean against
// NESTLE1904 withdraws Clear Bible's claims on those links too, so the Clear Bible set goes after it.
if (args is ["redraw", var redrawSource, var redrawSlug])
{
    using var redrawScope = app.Services.CreateScope();
    var slug = Identifier(redrawSlug);

    switch (redrawSource)
    {
        case "berean":
            var berean = redrawScope.ServiceProvider.GetRequiredService<BereanLinkLoader>();
            logger.LogInformation(
                "Withdrew {Links} links the Berean tables wrote against {Witness}",
                await berean.Withdraw(slug), slug);
            logger.LogInformation(
                "{Outcome}",
                await berean.Load(ResourcePaths.File(resources, "Berean", "bsb_tables.tsv"), slug));
            break;

        case "clearbible":
            var clearBible = redrawScope.ServiceProvider.GetRequiredService<ClearBibleLinkLoader>();
            foreach (var set in ClearBibleSet.All().Where(set => set.From == slug))
            {
                await clearBible.Withdraw(set);
                logger.LogInformation(
                    "{Outcome}", await clearBible.Load(Path.Combine(resources, "ClearBible"), set));
            }

            break;

        default:
            logger.LogError(
                "Nothing is known to redraw from \"{Source}\". Name berean with a witness, as in `redraw berean "
                + "BHSA`, or clearbible with a translation, as in `redraw clearbible BSB`", redrawSource);
            return 1;
    }

    logger.LogInformation(
        "{Outcome}", await redrawScope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());
    await redrawScope.ServiceProvider.GetRequiredService<AnnotationCarrier>().Carry();
    return 0;
}

// Clear Bible's hand-made alignments, as a batch run for the same reason the startup pipeline is
// not always available: a corpus already loaded gets them without a restart. Idempotent per set,
// like the pipeline step it shares a loader with.
if (args is ["clearbible", ..])
{
    using var clearScope = app.Services.CreateScope();
    var clearBible = clearScope.ServiceProvider.GetRequiredService<ClearBibleLinkLoader>();
    var folder = Path.Combine(
        resources, "ClearBible");

    foreach (var set in ClearBibleSet.All())
    {
        logger.LogInformation("{Outcome}", await clearBible.Load(folder, set));
    }

    logger.LogInformation(
        "{Outcome}", await clearScope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());
    return 0;
}

// The names of each verse settled by spelling and order over links already written, so a pair need
// not be aligned again for it. Reports and writes nothing without --apply. --chapters 1:46,40:1
// reports those chapters apart, with examples; --books 1,13 reads those canonical books only.
if (args is ["names", var namesFrom, var namesTo, ..])
{
    using var namesScope = app.Services.CreateScope();
    var chapters = (Option(args, "--chapters") ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(chapter => chapter.Split(':'))
        .Select(parts => (int.Parse(parts[0]), int.Parse(parts[1])))
        .ToHashSet();
    logger.LogInformation("\n{Report}", await namesScope.ServiceProvider.GetRequiredService<NameListPass>().Run(
        Identifier(namesFrom),
        Identifier(namesTo),
        chapters,
        Option(args, "--books") is { } namesBooks ? namesBooks.Split(',').Select(int.Parse).ToHashSet() : null,
        args.Contains("--apply")));
    return 0;
}

if (args is ["align", var alignFrom, var alignTo, ..])
{
    using var alignScope = app.Services.CreateScope();
    var pipeline = alignScope.ServiceProvider.GetRequiredService<AlignmentPipeline>();
    var confidence = Array.IndexOf(args, "--min");
    var alignOne = Identifier(alignFrom);
    var alignTwo = Identifier(alignTo);
    logger.LogInformation("{Outcome}", await pipeline.Run(
        alignOne,
        alignTwo,
        Path.Combine(Path.GetTempPath(), "essenthos-align", $"{alignOne}-{alignTwo}"),
        confidence >= 0 && confidence + 1 < args.Length
            ? double.Parse(args[confidence + 1], System.Globalization.CultureInfo.InvariantCulture)
            : AlignmentPipeline.DefaultMinimumConfidence,
        args.Contains("--model") ? args[Array.IndexOf(args, "--model") + 1] : "ibm4",
        replace: args.Contains("--replace")));
    return 0;
}


logger.LogError(
    "Nothing is known to do with \"{Verb}\". The verbs are load, verify, release, publish, rollback, releases, align, names, score, score-anchors, syntax, "
    + "compose, strong, synodal-strong, carry, clearbible, redraw, interlinear-join, locate, spell, images and the evidentia family",
    args[0]);
return 1;
