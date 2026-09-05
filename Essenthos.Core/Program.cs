using Essenthos.Core;
using Essenthos.Core.Configuration;
using Essenthos.Core.Database;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Verification;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Essenthos.Core.Loading.Encyclopedia;

var builder = WebApplication.CreateSlimBuilder(args);

// The slim builder does not read user secrets, and the database password is deliberately not in
// appsettings.json, so development has nowhere else to find it.
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(CorsOrigins.Read(builder.Configuration))
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
    optionsBuilder.UseNpgsql(DatabaseConnection.Read(builder.Configuration));
});

builder.Services.AddScoped<CorpusLoader>();
builder.Services.AddScoped<StatedNumberLoader>();
builder.Services.AddScoped<CanonicalFrameLoader>();
builder.Services.AddScoped<SuperscriptionFrameLoader>();
builder.Services.AddScoped<Essenthos.Core.Loading.Links.OldTestamentLinkLoader>();
builder.Services.AddScoped<Essenthos.Core.Loading.Links.NewTestamentLinkLoader>();
builder.Services.AddScoped<AlignmentPipeline>();
builder.Services.AddScoped<CompositionPipeline>();
builder.Services.AddScoped<CorpusCheck>();
builder.Services.AddScoped<StrongLexiconLoader>();
builder.Services.AddScoped<StrongGentilicLoader>();
builder.Services.AddScoped<SyntaxLoader>();
builder.Services.AddScoped<PrintedEditionLinkLoader>();
builder.Services.AddScoped<GreekWitnessLinkLoader>();
builder.Services.AddScoped<SamaritanLinkLoader>();
builder.Services.AddScoped<WordFoldingLoader>();
builder.Services.AddScoped<GraphicalWordLoader>();
builder.Services.AddScoped<Essenthos.Core.Glaux.GlauxLemmaLoader>();
builder.Services.AddScoped<Essenthos.Core.Glaux.SeptuagintStrongLoader>();
builder.Services.AddScoped<InterlinearLinkLoader>();
builder.Services.AddScoped<BereanLinkLoader>();
builder.Services.AddScoped<ClearBibleLinkLoader>();
builder.Services.AddScoped<VerseLinkLoader>();
builder.Services.AddScoped<BibleDataLoader>();
builder.Services.AddScoped<UssherAnnalsLoader>();
builder.Services.AddScoped<OpenBiblePlaceLoader>();
builder.Services.AddScoped<WorldHistoryLoader>();
builder.Services.AddSingleton<DatasetStatus>();
builder.Services.AddSingleton<ICanonIndex, CanonIndex>();
builder.Services.AddHostedService<DatasetLoader>();

var app = builder.Build();

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
        composeFrom,
        composeVia,
        composeTo,
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
    app.Logger.LogInformation("\n{Report}", await scorer.Measure(
        scoreFrom,
        scoreTo,
        Path.Combine(Path.GetTempPath(), "essenthos-align",
            $"{scoreFrom}-{scoreTo}{(args.Contains("--surface") ? "-surface" : string.Empty)}" +
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

// What the target text's own syntax is worth as a check on the model, before it is believed: every
// proposal the model made, bucketed by how it sits among its neighbours' answers, against what a
// source states. The last column is the weight the rescorer uses, so revising it is a reading.
if (args is ["syntax", var syntaxFrom, var syntaxTo, ..])
{
    using var syntaxScope = app.Services.CreateScope();
    var prior = syntaxScope.ServiceProvider.GetRequiredService<AlignmentPipeline>();
    app.Logger.LogInformation("\n{Report}", await prior.Diagnose(
        syntaxFrom,
        syntaxTo,
        Path.Combine(Path.GetTempPath(), "essenthos-align", $"{syntaxFrom}-{syntaxTo}"),
        args.Contains("--model") ? args[Array.IndexOf(args, "--model") + 1] : "ibm4",
        args.Contains("--stated")));
    return 0;
}

if (args is ["align", var alignFrom, var alignTo, ..])
{
    using var alignScope = app.Services.CreateScope();
    var pipeline = alignScope.ServiceProvider.GetRequiredService<AlignmentPipeline>();
    var confidence = Array.IndexOf(args, "--min");
    app.Logger.LogInformation("{Outcome}", await pipeline.Run(
        alignFrom,
        alignTo,
        Path.Combine(Path.GetTempPath(), "essenthos-align", $"{alignFrom}-{alignTo}"),
        confidence >= 0 && confidence + 1 < args.Length
            ? double.Parse(args[confidence + 1], System.Globalization.CultureInfo.InvariantCulture)
            : AlignmentPipeline.DefaultMinimumConfidence,
        args.Contains("--model") ? args[Array.IndexOf(args, "--model") + 1] : "ibm4"));
    return 0;
}

var v1 = app.MapGroup("/v1");
v1.MapHealth();
v1.MapRead();
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
