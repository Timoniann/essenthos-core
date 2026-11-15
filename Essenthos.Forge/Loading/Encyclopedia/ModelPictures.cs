using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Essenthos.Core.Products;
using Essenthos.Core.Records;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Models">Things the corpus holds that have a 3D model.</param>
/// <param name="Drawn">Pictures drawn this run: a model new, changed, or without its file.</param>
/// <param name="Kept">Pictures already drawn from the model as it is now, left as they were.</param>
/// <param name="Removed">Pictures of things that no longer have a model.</param>
/// <param name="Unable">Why nothing could be drawn here, where nothing could; null where it ran.</param>
/// <param name="Current">Nothing a picture is drawn from moved since the last drawing, so the renderer was not started.</param>
internal sealed record ModelPicturesOutcome(
    int Models, int Drawn, int Kept, int Removed, string? Unable, TimeSpan Elapsed, bool Current = false)
{
    public override string ToString() => (Unable, Current) switch
    {
        ({ } unable, _) => $"No picture drawn from a model: {unable}; the pictures drawn before stand, in {Elapsed}",
        (null, true) => $"{Models} pictures drawn from the models: nothing they are drawn from moved since, in {Elapsed}",
        _ => $"{Models} things with a model: {Drawn} drawn from it, {Kept} already drawn from it as it is, " +
             $"{Removed} removed with their model, in {Elapsed}",
    };
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
/// from. A run draws only the things whose digest moved. The things are the <c>object</c> rows of
/// <c>records/records</c>; records-derive draws first and then reads what it drew into the pictures' record files,
/// so every build lists a picture drawn anew under the digest of its new bytes.
/// </para>
///
/// <para>
/// Starting the renderer costs about twenty seconds even when it draws nothing, so <see cref="SourcesFile"/> beside
/// the pictures records what they were last drawn from (every file of <see cref="Sources"/> and the things) and a run
/// that finds the same and every listed picture in place starts nothing.
/// </para>
///
/// <para>
/// A machine with no checkout of the web, a server, draws nothing and keeps the pictures it was given.
/// <c>Models:WebPath</c> names the checkout where it is not beside this one.
/// </para>
/// </summary>
internal sealed class ModelPictures(IConfiguration configuration, ILogger<ModelPictures> logger)
{
    public const string WebConfigurationKey = "Models:WebPath";

    public const string NodeConfigurationKey = "Models:Node";

    public const string Script = "scripts/model-pictures/render.mjs";

    /// <summary>What a picture is drawn from, in the web checkout: the models, the renderer and the browser it drives, and the lock file that names three.js's version.</summary>
    public static readonly string[] Sources =
        ["src/features/models", "scripts/model-pictures", "scripts/visit/cdp.mjs", "package-lock.json"];

    /// <summary>What the pictures in the folder were last drawn from, written after each drawing.</summary>
    public const string SourcesFile = "drawn-from.tsv";

    /// <summary>A run that draws every model takes under a minute; one that hangs is stopped.</summary>
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(20);

    /// <summary>The web checkout: configured, or beside this repository's own folder.</summary>
    public static string WebFolder(IConfiguration configuration, string resources) =>
        Path.GetFullPath(configuration[WebConfigurationKey] is { Length: > 0 } configured
            ? configured
            : Path.Combine(resources, "..", "..", "essenthos-web"));

    /// <param name="resources">The Resources folder the pictures are drawn into.</param>
    /// <param name="store">The store whose records/records names the things.</param>
    public async Task<ModelPicturesOutcome> Draw(string resources, ProductStore store, CancellationToken cancellationToken = default)
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

        var things = RecordProducers.Read(store, "records/records")
            .Where(row => row[1] == "object")
            .Select(row => row[0]!)
            .Order(StringComparer.Ordinal)
            .ToList();
        var output = Path.Combine(resources, EntityImageLoader.Folder, EntityImageLoader.RenderedFolder);
        var drawnFrom = DrawnFrom(web, things);
        var stamp = Path.Combine(output, SourcesFile);
        if (File.Exists(stamp) && await File.ReadAllTextAsync(stamp, cancellationToken) == drawnFrom && Listed(output) is { } listed)
        {
            var current = new ModelPicturesOutcome(listed, 0, listed, 0, null, started.Elapsed, Current: true);
            logger.LogInformation("Drew the things from their models: {Outcome}", current);
            return current;
        }

        var records = Path.Combine(Path.GetTempPath(), $"essenthos-things-{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(records, things, cancellationToken);
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
                Directory.CreateDirectory(output);
                await File.WriteAllTextAsync(stamp, drawnFrom, cancellationToken);
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

    /// <summary>
    /// Every file a picture is drawn from, with its SHA-256, in ordinal order of its path under the web checkout, and the
    /// digest of the things drawn.
    /// </summary>
    internal static string DrawnFrom(string web, IReadOnlyList<string> things)
    {
        var files = Sources
            .Select(source => Path.Combine(web, source))
            .SelectMany(path => Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                : File.Exists(path) ? [path] : [])
            .Select(path => Path.GetRelativePath(web, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
        var text = new StringBuilder();
        foreach (var file in files)
        {
            text.Append(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(web, file))))).Append('\t')
                .Append(file).Append('\n');
        }

        var named = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', things)));
        return text.Append(Convert.ToHexStringLower(named)).Append("\tthings\n").ToString();
    }

    /// <summary>How many pictures the folder's manifest lists, where it lists some and every one is in place; null otherwise.</summary>
    private static int? Listed(string output)
    {
        var manifest = Path.Combine(output, EntityImageLoader.ManifestFile);
        if (!File.Exists(manifest))
        {
            return null;
        }

        using var stream = File.OpenRead(manifest);
        var images = EntityImageLoader.Parse(stream, manifest).Images ?? [];
        var root = Path.GetDirectoryName(output)!;
        return images.All(image => image.File is { Length: > 0 } file && File.Exists(Path.Combine(root, file))) ? images.Count : null;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Summary(int Models = 0, int Drawn = 0, int Kept = 0, int Removed = 0);
}
