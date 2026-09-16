using Essenthos.Core;
using Essenthos.Core.ClearBible;
using Essenthos.Core.Configuration;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Loading.Links.Evidentia;
using Essenthos.Core.Verification;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Essenthos.Core.Loading.Encyclopedia;
using System.Text;
using System.Text.Json;

// A report carrying Hebrew, Greek and an arrow is unreadable in the console's ANSI code page, and
// redirecting it to a file only moves the question marks.
Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateSlimBuilder(args);

// The slim builder does not read user secrets, and the database password is deliberately not in
// appsettings.json, so development has nowhere else to find it.
//
// Read unconditionally rather than only in Development. The gate was one more thing that had to be
// right before the API could start, and it depended on the launch profile setting the environment,
// which depended on avioniq passing --launch-profile, which depended on nothing upstream having
// already set ASPNETCORE_ENVIRONMENT. Each link is invisible when it breaks: the failure is not
// "the environment is wrong", it is "no database password", pointing at a secret that is sitting
// there correctly set. On a deployed machine there is no secrets file, the provider is empty, and
// the environment variable that supplies the password there is unaffected.
builder.Configuration.AddUserSecrets<Program>(optional: true);

// Both of these are read here, once, and never inside a lambda that runs later.
//
// `builder.Configuration` is a ConfigurationManager and it is disposed when the application is
// built. A lambda that closes over it and runs afterwards — which is what every options callback
// does — reads a dead object, and a dead ConfigurationManager does not throw: it answers null to
// everything. So the password was found at startup and absent an hour later, and the API served
// every database endpoint with "No database password" while its user secrets sat there correctly
// set (PRB-0414). The CORS policy had the same shape and silently fell back to the defaults.
//
// Reading eagerly also moves the failure to where it can be seen. A missing password now stops the
// process at startup, with the message, instead of answering 500 to a request nobody is watching.
var allowedOrigins = CorsOrigins.Read(builder.Configuration);
var databaseConnection = DatabaseConnection.Read(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddDbContext<AppDbContext>(optionsBuilder =>
{
    optionsBuilder.UseNpgsql(databaseConnection);
});

builder.Services.AddScoped<CorpusLoader>();
builder.Services.AddScoped<StatedNumberLoader>();
builder.Services.AddScoped<SourceNoteLoader>();
builder.Services.AddScoped<MorphGntParsingLoader>();
builder.Services.AddScoped<MaculaAnnotationLoader>();
builder.Services.AddScoped<CanonicalFrameLoader>();
builder.Services.AddScoped<SuperscriptionFrameLoader>();
builder.Services.AddScoped<Essenthos.Core.Loading.Links.OldTestamentLinkLoader>();
builder.Services.AddScoped<Essenthos.Core.Loading.Links.NewTestamentLinkLoader>();
builder.Services.AddScoped<AlignmentPipeline>();
builder.Services.AddSingleton<ILanguagePack, EnglishLanguagePack>();
builder.Services.AddSingleton<ILanguagePack, SlavicLanguagePack>();
builder.Services.AddSingleton<ILanguagePack, OriginalLanguagePack>();
builder.Services.AddSingleton<LanguagePackRegistry>();
builder.Services.AddScoped<EvidentiaPipeline>();
builder.Services.AddScoped<EvidentiaCorpusPreviewLoader>();
builder.Services.AddSingleton<EvidentiaStrongProposalResolver>();
builder.Services.AddSingleton<EvidentiaKnownRenderingProposalResolver>();
builder.Services.AddSingleton<EvidentiaTargetGlossProposalResolver>();
builder.Services.AddSingleton<EvidentiaDictionaryProposalResolver>();
builder.Services.AddSingleton<EvidentiaSyntaxReviewGate>();
builder.Services.AddSingleton<UdpipeAnnotator>();
builder.Services.AddScoped<EvidentiaDictionarySenseIndex>();
builder.Services.AddScoped<EvidentiaKnownRenderingIndex>();
builder.Services.AddSingleton<IEvidentiaEvidenceSource, StrongNumberEvidenceSource>();
builder.Services.AddScoped<CompositionPipeline>();
builder.Services.AddScoped<CorpusCheck>();
builder.Services.AddScoped<StrongLexiconLoader>();
builder.Services.AddScoped<StrongGentilicLoader>();
builder.Services.AddScoped<StrongTranslationLoader>();
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
builder.Services.AddScoped<WorldHistoryLoader>();
builder.Services.AddScoped<PeopleLoader>();
builder.Services.AddScoped<PlaceRegisterLoader>();
builder.Services.AddScoped<PersonRegisterLoader>();
builder.Services.AddScoped<EntityAnnotationLoader>();
builder.Services.AddScoped<OwnRecordLoader>();
builder.Services.AddScoped<SenseReadingLoader>();
builder.Services.AddScoped<SiteSplitLoader>();
builder.Services.AddScoped<GreekNamesakeLoader>();
builder.Services.AddScoped<AnnotationCarrier>();
builder.Services.AddScoped<SoleBearerLoader>();
builder.Services.AddScoped<TermLoader>();
builder.Services.AddScoped<TitleLoader>();
builder.Services.AddScoped<OwnReferenceLoader>();
builder.Services.AddScoped<EntityDescriptorLoader>();
builder.Services.AddScoped<EntityNameFormLoader>();
builder.Services.AddScoped<OwnRelationshipLoader>();
builder.Services.AddSingleton<DatasetStatus>();
builder.Services.AddSingleton<ICanonIndex, CanonIndex>();
builder.Services.AddHostedService<DatasetLoader>();

// Disposed on every return path, not only the one that serves. Draining the console logger's
// background queue is what disposal does, and a command that returns without it loses whatever is
// still queued — silently, and more of it the longer the report.
await using var app = builder.Build();

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    app.Logger.LogError(feature?.Error, "Unhandled exception for {Path}", context.Request.Path);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "text/plain";
    await context.Response.WriteAsync(
        "The request could not be served. This is a fault in the API, not in the request; the cause is in " +
        "the API's own log.");
}));

// A text identifier as the corpus spells it, from one typed at a shell in whatever case came to
// hand. The pipelines below compare it to the column, and the workspace they leave in the temp
// folder is named after it: without this, `align kjv bhsa` finds no text, and on a file system that
// tells KJV-BHSA from kjv-bhsa the same pair would train a second model beside the first.
static string Identifier(string typed) => typed.ToUpperInvariant();

// Alignment is computed once per pair of texts, not per request, so it is a batch run rather than
// part of the startup pipeline: an API that trains a model before it answers is the shape PRB-0005
// warned about.
// What a threshold costs, on the one pair where a source says what the right answer is. It reuses
// the alignment in the workspace, so a sweep is seconds once the model has been run.
// The second route to the same word, through a text whose own links to the target are stated.
// Russian against Hebrew is one hard hop; Russian against the King James is an easy one, and the
// King James against BHSA is not a hop at all.
if (args is ["compose", var composeFrom, var composeVia, var composeTo, ..])
{
    using var composeScope = app.Services.CreateScope();
    var composer = composeScope.ServiceProvider.GetRequiredService<CompositionPipeline>();
    var least = Array.IndexOf(args, "--min");
    app.Logger.LogInformation("{Outcome}", await composer.Run(
        Identifier(composeFrom),
        Identifier(composeVia),
        Identifier(composeTo),
        least >= 0 && least + 1 < args.Length
            ? double.Parse(args[least + 1], System.Globalization.CultureInfo.InvariantCulture)
            : AlignmentPipeline.DefaultMinimumConfidence));
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

    app.Logger.LogInformation("\n{Report}", measures.Describe());

    var rendered = measures.Rendered;
    if (measures.Broken > 0)
    {
        app.Logger.LogError(
            "{Broken} integrity checks found something, and every one of them should find nothing",
            measures.Broken);
        return 1;
    }

    if (rendered < floor)
    {
        app.Logger.LogError(
            "{Rendered:P1} of the words in a linked text reach a witness, below the floor of {Floor:P1}. Either " +
            "the load lost something, or the floor is stale and should be raised deliberately",
            rendered, floor);
        return 1;
    }

    app.Logger.LogInformation(
        "{Rendered:P1} of the words in a linked text reach a witness, floor {Floor:P1}; the weakest section of " +
        "any one text reaches {Weakest:P1}",
        rendered, floor, measures.Weakest);
    return 0;
}

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
    app.Logger.LogInformation("\n{Report}", await scorer.Measure(
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
    app.Logger.LogInformation("\n{Preview}", preview);
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
        Identifier(measureFrom), Identifier(measureTo), book, chapter, EvidentiaOptions(args));
    await WriteDisagreements(args, measurement.Disagreements);
    app.Logger.LogInformation("\n{Measurement}", measurement);
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
        Identifier(measureBookFrom), Identifier(measureBookTo), book, EvidentiaOptions(args),
        firstChapter: fromChapter,
        lastChapter: toChapter);
    await WriteDisagreements(args, measurement.Disagreements);
    app.Logger.LogInformation("\n{Measurement}", measurement);
    return 0;
}

// Contradicted proposals go to a file rather than to the report, because they are read one at a
// time and there are thousands of them: a classification pass needs the rows the aggregate counted,
// and a console report is neither a stable record of them nor wide enough to hold a whole verse.
static async Task WriteDisagreements(string[] arguments, IReadOnlyList<EvidentiaDisagreement> rows)
{
    if (OptionalText(arguments, "--disagreements") is not { } path)
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
static EvidentiaMeasurementOptions EvidentiaOptions(string[] arguments) => new(
    AllowSourceStrongEvidence: !arguments.Contains("--without-source-strong"),
    AllowKnownRenderingEvidence: !arguments.Contains("--without-known-renderings"),
    LearnRenderingsFrom: OptionalText(arguments, "--learn-from") is { } learnFrom ? Identifier(learnFrom) : null,
    LearnedRenderingMethods: arguments.Contains("--learn-from-strong-numbers")
        ? [LinkMethod.StatedBySource, LinkMethod.StrongNumber]
        : null,
    GoldSource: OptionalText(arguments, "--gold-source"),
    SampleSize: OptionalInt(arguments, "--sample") ?? 0,
    RecordDisagreements: OptionalText(arguments, "--disagreements") is not null);

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
    app.Logger.LogInformation("\n{Report}", await scorer.MeasureAnchors(
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
    app.Logger.LogInformation("\n{Report}", await prior.Diagnose(
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
    app.Logger.LogInformation(
        "{Outcome}", await tagged.Load(Identifier(strongFrom), Identifier(strongTo)));

    // The verse links for the pair just written. The startup pipeline does this for pairs the
    // alignment commands leave behind, and a command that cannot be followed by a restart has to
    // do it itself: without them every word link of a new pair reads as crossing a verse boundary
    // nothing backs, which is an integrity check the corpus keeps at zero.
    app.Logger.LogInformation(
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
        ResourcePaths.Read(app.Configuration, app.Environment.ContentRootPath),
        SynodalStrongLinkLoader.EditionFile);
    string[] witnesses = args.Length > 1 ? [.. args[1..].Select(Identifier)] : SynodalStrongLinkLoader.Witnesses;

    await synodalScope.ServiceProvider.GetRequiredService<SynodalStrongLinkLoader>().Load(editionPath, witnesses);
    app.Logger.LogInformation(
        "{Outcome}", await synodalScope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());

    // The links just written and the guesses just removed decide which Synodal words an annotation
    // reaches, and nothing on a restart asks that again.
    await synodalScope.ServiceProvider.GetRequiredService<AnnotationCarrier>().Carry();
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

// Clear Bible's hand-made alignments, as a batch run for the same reason the startup pipeline is
// not always available: a corpus already loaded gets them without a restart. Idempotent per set,
// like the pipeline step it shares a loader with.
if (args is ["clearbible", ..])
{
    using var clearScope = app.Services.CreateScope();
    var clearBible = clearScope.ServiceProvider.GetRequiredService<ClearBibleLinkLoader>();
    var folder = Path.Combine(
        ResourcePaths.Read(app.Configuration, app.Environment.ContentRootPath), "ClearBible");

    foreach (var set in new[]
             {
                 ClearBibleSet.Berean(BereanTextSource.Slug, NestleTextSource.Slug),
                 ClearBibleSet.ReinaValeraOldTestament(EbibleTextSource.ReinaValera, BhsaTextSource.Slug),
                 ClearBibleSet.ReinaValeraNewTestament(EbibleTextSource.ReinaValera, NestleTextSource.Slug),
             })
    {
        app.Logger.LogInformation("{Outcome}", await clearBible.Load(folder, set));
    }

    app.Logger.LogInformation(
        "{Outcome}", await clearScope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());
    return 0;
}

if (args is ["align", var alignFrom, var alignTo, ..])
{
    using var alignScope = app.Services.CreateScope();
    var pipeline = alignScope.ServiceProvider.GetRequiredService<AlignmentPipeline>();
    var confidence = Array.IndexOf(args, "--min");
    var alignOne = Identifier(alignFrom);
    var alignTwo = Identifier(alignTo);
    app.Logger.LogInformation("{Outcome}", await pipeline.Run(
        alignOne,
        alignTwo,
        Path.Combine(Path.GetTempPath(), "essenthos-align", $"{alignOne}-{alignTwo}"),
        confidence >= 0 && confidence + 1 < args.Length
            ? double.Parse(args[confidence + 1], System.Globalization.CultureInfo.InvariantCulture)
            : AlignmentPipeline.DefaultMinimumConfidence,
        args.Contains("--model") ? args[Array.IndexOf(args, "--model") + 1] : "ibm4"));
    return 0;
}

var v1 = app.MapGroup("/v1");
v1.MapHealth();
v1.MapRead();
v1.MapVerses();
v1.MapParallel();
v1.MapStrong();
v1.MapSyntax();
v1.MapWords();
v1.MapSearch();
v1.MapEncyclopedia();
v1.MapDatasets();

app.UseCors();

app.Run();
return 0;
