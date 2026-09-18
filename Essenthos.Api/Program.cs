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

// The corpus is opened read-only, by a role that holds SELECT and nothing else. Nothing this
// process serves writes a row of it: a release is built elsewhere and restored into a database of
// its own, and the connection here names whichever one is current.
builder.Services.AddDbContext<AppDbContext>(optionsBuilder =>
{
    optionsBuilder.UseNpgsql(databaseConnection);
});

builder.Services.AddSingleton<ICanonIndex, CanonIndex>();

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
v1.MapDatasets();
v1.MapAuth(providers);
v1.MapMe();
v1.MapDevices();
v1.MapNotes();

app.UseCors();

app.Run();
