using Essenthos.Core.Configuration;
using Essenthos.Core.Database;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Desk;

/// <summary>
/// The console's host, built in one place so a test runs the same pipeline the owner does: the
/// loopback guard, the endpoints and the services behind them.
/// </summary>
internal static class DeskApplication
{
    /// <summary>Where every endpoint of the console lives, and what the web console's dev server passes through.</summary>
    public const string Prefix = "/desk-api";

    /// <param name="contentRoot">
    /// Where appsettings.json is read from: beside the assembly unless given, so it is found however
    /// the console is started, and wherever a test says so it can hold nothing.
    /// </param>
    public static WebApplication Build(string[] args, string? contentRoot = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = contentRoot ?? AppContext.BaseDirectory,
        });

        UserSecrets.AddBelowEnvironment(builder.Configuration, typeof(DeskApplication).Assembly);

        LocalOnly.RequireLoopback(builder.Configuration);
        var paths = DeskPaths.Read(builder.Configuration);
        var databaseConnection = DatabaseConnection.Read(builder.Configuration);

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, DeskJsonContext.Default);
        });

        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(databaseConnection));
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton(new LocalOnly(builder.Configuration.GetSection("Desk:Origins").Get<string[]>() ?? []));
        builder.Services.AddSingleton<Avioniq>();
        builder.Services.AddSingleton<ChangeLog>();
        builder.Services.AddSingleton<ThingReview>();
        builder.Services.AddSingleton<PictureChoices>();
        builder.Services.AddSingleton<SiteSwitches>();
        builder.Services.AddScoped<PortraitBoard>();
        builder.Services.AddScoped<PortraitEditor>();
        builder.Services.AddSingleton<TextBoard>();
        builder.Services.AddSingleton<TextProblems>();
        builder.Services.AddSingleton(new OperationAllowance(
            builder.Configuration.GetSection("Desk:Operations").Get<string[]>() ?? []));
        builder.Services.AddSingleton<Operations>();
        builder.Services.AddSingleton<Deployment>();
        builder.Services.AddSingleton<IServerShell, SshShell>();
        builder.Services.AddSingleton<ServerBackups>();
        builder.Services.AddHostedService<ServerBackupSchedule>();

        var app = builder.Build();

        app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            var feature = context.Features.Get<IExceptionHandlerFeature>();
            app.Logger.LogError(feature?.Error, "Unhandled exception for {Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(
                "The console could not do this. The cause is in the console's own log, in the terminal it was started from.");
        }));

        app.Use(app.Services.GetRequiredService<LocalOnly>().Guard);

        var desk = app.MapGroup(Prefix);
        desk.MapSummary();
        desk.MapHistory();
        desk.MapSettings();
        desk.MapThingReview();
        desk.MapPortraits();
        desk.MapOperations();
        desk.MapTexts();
        desk.MapDeployment();
        desk.MapServerBackups();

        return app;
    }
}
