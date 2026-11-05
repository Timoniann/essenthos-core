using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Loading.Links.Evidentia;
using Essenthos.Core.Publishing;
using Essenthos.Core.Verbs;
using Essenthos.Core.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;

// Everything that reads a source file and writes the corpus: the loaders, the aligners, EVIDENTIA,
// and the verification pass. None of it is reachable from the API, which is the point of it being a
// separate assembly — a server that serves the corpus has no business holding a parser, and a
// boundary the compiler checks is the only kind that holds.
//
// Every verb is a batch run. There is no background service here and no request to answer:
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
var databaseConnection = ForgeVerbs.Target(args, DatabaseConnection.Read(builder.Configuration));

AlignmentStore.Configure(builder.Configuration, builder.Environment.ContentRootPath);

// Every context and every command made from its connection waits as long as the configuration says,
// so no step is the one that forgot to raise Npgsql's thirty seconds.
var commandTimeout = new Npgsql.NpgsqlConnectionStringBuilder(databaseConnection).CommandTimeout;
builder.Services.AddDbContext<AppDbContext>(optionsBuilder =>
{
    optionsBuilder.UseNpgsql(databaseConnection, npgsql => npgsql.CommandTimeout(commandTimeout));
});

builder.Services.AddScoped<TextRelationLoader>();
builder.Services.AddScoped<CanonicalFrameLoader>();
builder.Services.AddScoped<Essenthos.Core.Loading.Links.OldTestamentLinkLoader>();
builder.Services.AddScoped<AlignmentPipeline>();
builder.Services.AddSingleton<ILanguagePack, EnglishLanguagePack>();
builder.Services.AddSingleton<ILanguagePack, UkrainianLanguagePack>();
builder.Services.AddSingleton<ILanguagePack, RussianLanguagePack>();
builder.Services.AddSingleton<ILanguagePack, GermanLanguagePack>();
builder.Services.AddSingleton<ILanguagePack, SpanishLanguagePack>();
builder.Services.AddSingleton<ILanguagePack, OriginalLanguagePack>();
builder.Services.AddSingleton<LanguagePackRegistry>();
builder.Services.AddScoped<EvidentiaPipeline>();
builder.Services.AddScoped<EvidentiaCorpusPreviewLoader>();
builder.Services.AddScoped<EvidentiaRunner>();
builder.Services.AddScoped<EvidentiaReviewQueue>();
builder.Services.AddScoped<EvidentiaLinkWriter>();
builder.Services.AddScoped<EvidentiaLedger>();
builder.Services.AddScoped<EvidentiaProblemVerses>();
builder.Services.AddSingleton<EvidentiaStrongProposalResolver>();
builder.Services.AddSingleton<EvidentiaKnownRenderingProposalResolver>();
builder.Services.AddSingleton<EvidentiaTargetGlossProposalResolver>();
builder.Services.AddSingleton<EvidentiaDictionaryProposalResolver>();
builder.Services.AddSingleton<EvidentiaSyntaxReviewGate>();
builder.Services.AddSingleton<UdpipeAnnotator>();
builder.Services.AddSingleton<EvidentiaFileSourceTexts>();
builder.Services.AddSingleton<EvidentiaContextGlossIndex>();
builder.Services.AddScoped<EvidentiaDictionarySenseIndex>();
builder.Services.AddScoped<EvidentiaKnownRenderingIndex>();
builder.Services.AddSingleton<IEvidentiaEvidenceSource, StrongNumberEvidenceSource>();
builder.Services.AddScoped<CompositionPipeline>();
builder.Services.AddScoped<NameListPass>();
builder.Services.AddScoped<NamesFirstPass>();
builder.Services.AddScoped<PossessivePass>();
builder.Services.AddScoped<SharedWordPass>();
// How many of verify's measures run at once, each on its own connection. A server verifying a
// restored release builds its own CorpusCheck and measures one at a time.
var verifySessions = builder.Configuration.GetValue("Verification:Sessions", 1);
builder.Services.AddScoped(services => new CorpusCheck(
    services.GetRequiredService<AppDbContext>(), services.GetRequiredService<ILogger<CorpusCheck>>(), verifySessions));
builder.Services.AddScoped<StrongRelationLoader>();
builder.Services.AddScoped<StrongTranslationLoader>();
builder.Services.AddScoped<GeezLexiconLoader>();
builder.Services.AddScoped<GeezLexiconMeasure>();
builder.Services.AddScoped<InterlinearLinkLoader>();
builder.Services.AddScoped<TaggedTextLinkLoader>();
builder.Services.AddScoped<SynodalStrongLinkLoader>();
builder.Services.AddScoped<UnionStrongLinkLoader>();
builder.Services.AddScoped<ReinaValeraStrongLinkLoader>();
builder.Services.AddScoped<VerseLinkLoader>();
builder.Services.AddScoped<OpenBibleLocationLoader>();
builder.Services.AddScoped<EntityImageLoader>();
builder.Services.AddScoped<ModelPictures>();
builder.Services.AddScoped<WikipediaLinkLoader>();
builder.Services.AddScoped<PeriodOLoader>();
builder.Services.AddScoped<PersonRegisterLoader>();
builder.Services.AddScoped<OwnRecordLoader>();
builder.Services.AddScoped<ContextBearerLoader>();
builder.Services.AddScoped<SpelledNameLoader>();
builder.Services.AddScoped<FixedTitleLoader>();
builder.Services.AddScoped<PassageReadingLoader>();
builder.Services.AddScoped<ForeignNames>();
builder.Services.AddScoped<PronounReferents>();
builder.Services.AddScoped<StrayNames>();
builder.Services.AddScoped<TitleLoader>();
builder.Services.AddScoped<TitleReadingLoader>();
builder.Services.AddScoped<VerseReadingLoader>();
builder.Services.AddScoped<RelationshipVerseLoader>();
builder.Services.AddScoped<MisplacedAnnotationLoader>();
builder.Services.AddScoped<ReignLoader>();
builder.Services.AddScoped<OwnNameLoader>();
builder.Services.AddScoped<WithdrawnRecordLoader>();
builder.Services.AddScoped<ThingLoader>();
builder.Services.AddScoped<DistinguisherLoader>();
builder.Services.AddScoped<NoteTranslationLoader>();
builder.Services.AddScoped<PersonPresenceLoader>();
builder.Services.AddScoped<OccupationLoader>();
builder.Services.AddScoped<OwnReferenceLoader>();
builder.Services.AddSingleton(_ => ReviewLists.Read(builder.Configuration));
builder.Services.AddScoped<EntityDescriptorLoader>();
builder.Services.AddScoped<CommandmentLoader>();
builder.Services.AddScoped<NaveTopicLoader>();
builder.Services.AddScoped<Essenthos.Core.Loading.CrossReferences.CrossReferenceLoader>();
builder.Services.AddScoped<EntityNameFormLoader>();
builder.Services.AddScoped<EntityNameTranslationLoader>();
builder.Services.AddScoped<KinStatementLoader>();
builder.Services.AddScoped<EntityRenderingLoader>();
builder.Services.AddScoped<NameConsensusPass>();
builder.Services.AddScoped<OwnRelationshipLoader>();
builder.Services.AddScoped<DuplicateRecordLoader>();
builder.Services.AddSingleton<ICanonIndex, CanonIndex>();
builder.Services.AddScoped<Publisher>();
builder.Services.AddScoped<TextExporter>();

// Disposed on every return path, not only the one that serves. Draining the console logger's
// background queue is what disposal does, and a command that returns without it loses whatever is
// still queued — silently, and more of it the longer the report.
using var app = builder.Build();

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("forge");
var configuration = app.Services.GetRequiredService<IConfiguration>();
var environment = app.Services.GetRequiredService<IHostEnvironment>();
var resources = ResourcePaths.Read(configuration, environment.ContentRootPath);

// Every verb is declared once, in Verbs/ForgeVerbs.cs, with its arguments and a line of help; `forge help`
// lists them, and no arguments at all is that list.
string[] arguments = args is [] ? ["help"] : args;
var verb = ForgeVerbs.Find(arguments[0]);
if (verb is null)
{
    logger.LogError("Nothing is known to do with \"{Verb}\". `forge help` lists the verbs", arguments[0]);
    return 1;
}

if (!verb.Accepts(arguments))
{
    logger.LogError("forge {Usage}", verb.Usage);
    return 1;
}

var forge = new ForgeRun(app.Services, logger, resources, databaseConnection);
return await verb.Run(forge, arguments);
