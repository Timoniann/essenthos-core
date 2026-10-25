using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Marks;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Models">Things the corpus holds that have a 3D model.</param>
/// <param name="Drawn">Pictures drawn this run: a model new, changed, or without its file.</param>
/// <param name="Kept">Pictures already drawn from the model as it is now, left as they were.</param>
/// <param name="Removed">Pictures of things that no longer have a model.</param>
/// <param name="Unable">Why nothing could be drawn here, where nothing could; null where it ran.</param>
internal sealed record ModelPicturesOutcome(int Models, int Drawn, int Kept, int Removed, string? Unable, TimeSpan Elapsed)
{
    public override string ToString() => Unable is not null
        ? $"No picture drawn from a model: {Unable}; the pictures drawn before stand, in {Elapsed}"
        : $"{Models} things with a model: {Drawn} drawn from it, {Kept} already drawn from it as it is, " +
          $"{Removed} removed with their model, in {Elapsed}";
}

/// <summary>
/// The picture of every thing that has a 3D model, drawn from the model, and drawn again only when
/// the model changes.
///
/// <para>
/// The models are the site's own (essenthos-web, <c>src/features/models</c>), so the drawing is done
/// there: <c>scripts/model-pictures/render.mjs</c> builds them with the site's code in a headless
/// Chrome of its own, lit as the site's viewer lights them, and writes each picture into
/// <c>Resources/Images/models</c> beside a manifest that records the digest of the model it was drawn
/// from. A run draws only the things whose digest moved; the step runs only when the models, the
/// renderer or the things the corpus holds have moved (its files below and the <c>entity</c> table it
/// reads). The <c>images</c> step after it loads what it drew.
/// </para>
///
/// <para>
/// A machine with no checkout of the web, a server, draws nothing and keeps the pictures it was given.
/// <c>Models:WebPath</c> names the checkout where it is not beside this one.
/// </para>
/// </summary>
internal sealed class ModelPictures(AppDbContext db, IConfiguration configuration, ILogger<ModelPictures> logger)
{
    public const string WebConfigurationKey = "Models:WebPath";

    public const string NodeConfigurationKey = "Models:Node";

    public const string Script = "scripts/model-pictures/render.mjs";

    /// <summary>What a picture is drawn from, in the web checkout: the models, the renderer and the browser it drives, and the lock file that names three.js's version.</summary>
    public static readonly string[] Sources =
        ["src/features/models", "scripts/model-pictures", "scripts/visit/cdp.mjs", "package-lock.json"];

    /// <summary>A run that draws every model takes under a minute; one that hangs is stopped.</summary>
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(20);

    /// <summary>The web checkout: configured, or beside this repository's own folder.</summary>
    public static string WebFolder(IConfiguration configuration, string resources) =>
        Path.GetFullPath(configuration[WebConfigurationKey] is { Length: > 0 } configured
            ? configured
            : Path.Combine(resources, "..", "..", "essenthos-web"));

    /// <summary>The files the step reads, so that a changed model runs it again.</summary>
    public static StepFile[] Files(IConfiguration configuration, string resources)
    {
        var web = WebFolder(configuration, resources);
        return [.. Sources.Select(source => new StepFile(Path.Combine(web, source)))];
    }

    public async Task<ModelPicturesOutcome> Draw(string resources, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var web = WebFolder(configuration, resources);
        var script = Path.Combine(web, Script);
        if (!File.Exists(script))
        {
            logger.LogWarning(
                "No picture is drawn from the models: there is no renderer at {Script}. Set {Key} to the essenthos-web " +
                "checkout to draw them here.",
                script, WebConfigurationKey);
            return new ModelPicturesOutcome(0, 0, 0, 0, $"no renderer at {script}", started.Elapsed);
        }

        var things = await db.Entities
            .Where(e => e.Kind == EntityKind.Object)
            .Select(e => e.Slug)
            .OrderBy(slug => slug)
            .ToListAsync(cancellationToken);
        var records = Path.Combine(Path.GetTempPath(), $"essenthos-things-{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(records, things, cancellationToken);
        var output = Path.Combine(resources, EntityImageLoader.Folder, EntityImageLoader.RenderedFolder);
        try
        {
            var start = new ProcessStartInfo(configuration[NodeConfigurationKey] is { Length: > 0 } node ? node : "node")
            {
                WorkingDirectory = web,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in new[] { script, "--out", output, "--records", records })
            {
                start.ArgumentList.Add(argument);
            }

            Process process;
            try
            {
                process = Process.Start(start) ?? throw new InvalidOperationException("Node did not start.");
            }
            catch (Win32Exception exception)
            {
                throw new InvalidOperationException(
                    $"The pictures of the models need Node, which did not start ({exception.Message}). Install it, or " +
                    $"set {NodeConfigurationKey} to its path.", exception);
            }

            using (process)
            {
                string? last = null;
                var said = Task.Run(async () =>
                {
                    while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
                    {
                        logger.LogInformation("Model pictures: {Line}", line);
                        last = line;
                    }
                }, cancellationToken);
                var error = process.StandardError.ReadToEndAsync(cancellationToken);
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                limit.CancelAfter(Longest);
                try
                {
                    await process.WaitForExitAsync(limit.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    throw;
                }

                await Task.WhenAll(said, error);
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"The pictures of the models could not be drawn ({Script} exited {process.ExitCode}): {error.Result.Trim()}");
                }

                var summary = JsonSerializer.Deserialize<Summary>(last ?? "{}", Json) ?? new Summary();
                var outcome = new ModelPicturesOutcome(summary.Models, summary.Drawn, summary.Kept, summary.Removed, null,
                    started.Elapsed);
                logger.LogInformation("Drew the things from their models: {Outcome}", outcome);
                return outcome;
            }
        }
        finally
        {
            File.Delete(records);
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Summary(int Models = 0, int Drawn = 0, int Kept = 0, int Removed = 0);
}
