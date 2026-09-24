using System.Text.Json.Nodes;
using Essenthos.Core.Configuration;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The owner's switches and choices for the site, read from the tracked file beside the API. Read
/// again only when the file changes, so a switch the owner flips in his console reaches the next
/// request without a restart, and every other request costs one look at the file's time.
/// </summary>
internal sealed class SiteSettingsFile(string path, ILogger<SiteSettingsFile> logger)
{
    public const string ConfigurationKey = "Site:SettingsPath";

    private readonly Lock _lock = new();

    private DateTime _readAt = DateTime.MinValue;

    private IReadOnlyDictionary<string, bool> _values = SiteSettings.Defaults();

    private IReadOnlyDictionary<string, string> _choices = SiteSettings.ChoiceDefaults();

    /// <summary>The file named by configuration, or the one in the content root, which is the checkout's own in development.</summary>
    public static string Path(IConfiguration configuration, string contentRootPath) =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(
            contentRootPath,
            configuration[ConfigurationKey] is { Length: > 0 } configured ? configured : SiteSettings.FileName));

    public IReadOnlyDictionary<string, bool> Values
    {
        get
        {
            Refresh();
            return _values;
        }
    }

    public IReadOnlyDictionary<string, string> Choices
    {
        get
        {
            Refresh();
            return _choices;
        }
    }

    public bool Is(string key) => Values.TryGetValue(key, out var on) ? on : SiteSettings.Defaults()[key];

    /// <summary>The licence the owner put Essenthos's own work under; null while he has not decided.</summary>
    public OwnWorkLicence? OwnWorkLicence => OwnWorkLicences.Find(Choices[SiteSettings.OwnWorkLicence]);

    private void Refresh()
    {
        var written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        lock (_lock)
        {
            if (written == _readAt)
            {
                return;
            }

            try
            {
                var file = SiteSettings.Parse(path);
                _values = SiteSettings.From(file);
                _choices = SiteSettings.ChoicesFrom(file);
                _readAt = written;
            }
            catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
            {
                // Caught mid-write, or broken by hand: the last good reading stands until it reads again.
                logger.LogWarning(exception, "The site's settings at {Path} could not be read; the last reading stands", path);
            }
        }
    }
}

internal static class SettingsEndpoints
{
    public static void MapSettings(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/settings", (SiteSettingsFile settings, HttpContext context) =>
        {
            // Short, so a switch flipped for the site reaches readers within the minute.
            context.Response.Headers.CacheControl = "public, max-age=60";
            var served = new JsonObject();
            foreach (var (key, on) in settings.Values)
            {
                served[key] = on;
            }

            foreach (var (key, chosen) in settings.Choices)
            {
                served[key] = chosen;
            }

            // Named and linked, so every credit on our own work can print it as it is.
            served[SiteSettings.OwnWorkLicence] = settings.OwnWorkLicence is { } licence
                ? new JsonObject { ["id"] = licence.Id, ["name"] = licence.Name, ["url"] = licence.Url }
                : null;

            return Results.Json(served, AppJsonSerializerContext.Default.JsonObject);
        });
}
