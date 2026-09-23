using Essenthos.Core;
using Essenthos.Core.Accounts;
using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Endpoints;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using System.Text;

// A report carrying Hebrew, Greek and an arrow is unreadable in the console's ANSI code page, and
// redirecting it to a file only moves the question marks.
Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateSlimBuilder(args);

// The database password is deliberately not in appsettings.json, so development reads it from user
// secrets — whatever the environment, and beneath the environment and the command line, so either can
// still override it. See UserSecrets.
UserSecrets.AddBelowEnvironment(builder.Configuration, typeof(Program).Assembly);

// Both of these are read here, once, rather than inside an options callback that runs later, so
// that a missing password or an unset origin stops the process at startup with the message that
// says what to set. Read lazily, the same mistake becomes a 500 on the first request that needs
// the database and a CORS policy that has quietly fallen back to the defaults — a failure nobody
// is watching, discovered by a reader instead of by the person starting it.
var allowedOrigins = CorsOrigins.Read(builder.Configuration);
var databaseConnection = DatabaseConnection.Read(builder.Configuration);
var images = ImageEndpoints.Folder(builder.Configuration, builder.Environment.ContentRootPath);

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

// The corpus is opened read-only, by a role that holds SELECT and nothing else. Nothing this
// process serves writes a row of it: a release is built elsewhere and restored into a database of
// its own, and the connection here names whichever one is current.
builder.Services.AddDbContext<AppDbContext>(optionsBuilder =>
{
    optionsBuilder.UseNpgsql(databaseConnection);
});

builder.Services.AddSingleton<ICanonIndex, CanonIndex>();
builder.Services.AddSingleton<TextFacts>();

// Accounts: the database the API owns and writes, and sign-in with whichever providers are
// configured. Reading needs none of it.
var providers = builder.Services.AddAccounts(builder.Configuration);

// Disposed on every return path, not only the one that serves. Draining the console logger's
// background queue is what disposal does, and a command that returns without it loses whatever is
// still queued — silently, and more of it the longer the report.
await using var app = builder.Build();

// The accounts schema is migrated by a one-shot run of this same image, before the API starts —
// never by the API as it boots, where two containers starting together would race each other. On a
// development machine there is only ever one, so it migrates itself.
if (args is ["migrate", ..] || app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
    await accounts.Database.MigrateAsync();
    if (args is ["migrate", ..])
    {
        app.Logger.LogInformation("The accounts database is at {Migration}",
            (await accounts.Database.GetAppliedMigrationsAsync()).LastOrDefault());
        return;
    }
}

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

app.UseAuthentication();
app.UseAuthorization();

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
v1.MapContext();
v1.MapImages(images);
v1.MapCommandments();
v1.MapDatasets();
v1.MapAuth(providers);
v1.MapMe();
v1.MapDevices();
v1.MapBookmarks();

app.UseCors();

app.Run();
