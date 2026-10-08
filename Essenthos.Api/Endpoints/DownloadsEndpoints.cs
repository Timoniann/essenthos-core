using Essenthos.Core.Corpus;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The files <c>forge export</c> wrote, as the page of downloads lists them.
///
///     GET /v1/downloads      every text that can be taken as a file, with its licence, its size and its checksum
///
/// The list is the export's own manifest, read from the folder named by <c>Downloads:Folder</c> and read
/// again when the file changes. A file's address is <c>Downloads:BaseUrl</c> and its path, so where the
/// files are served from is a setting and not a decision made here: with no base address the files are
/// listed and carry none. With no folder, or no manifest in it, the list is empty.
/// </summary>
internal sealed class DownloadsCatalogue(string? folder, string? baseUrl, ILogger<DownloadsCatalogue> logger)
{
    public const string FolderKey = "Downloads:Folder";

    public const string BaseUrlKey = "Downloads:BaseUrl";

    private readonly Lock _lock = new();

    private DateTime _readAt = DateTime.MinValue;

    private DownloadsResponse _current = DownloadsResponse.None;

    public static DownloadsCatalogue From(IConfiguration configuration, string contentRootPath, ILogger<DownloadsCatalogue> logger) =>
        new(
            configuration[FolderKey] is { Length: > 0 } folder ? Path.GetFullPath(Path.Combine(contentRootPath, folder)) : null,
            configuration[BaseUrlKey],
            logger);

    public DownloadsResponse Current()
    {
        if (folder is null)
        {
            return DownloadsResponse.None;
        }

        var path = Path.Combine(folder, DownloadsManifest.FileName);
        var written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        lock (_lock)
        {
            if (written == _readAt)
            {
                return _current;
            }

            try
            {
                _current = written == DateTime.MinValue ? DownloadsResponse.None : Describe(File.ReadAllText(path));
                _readAt = written;
            }
            catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
            {
                // Caught mid-write, or broken by hand: the last good reading stands until it reads again.
                logger.LogWarning(exception, "The downloads manifest at {Path} could not be read; the last reading stands", path);
            }

            return _current;
        }
    }

    internal DownloadsResponse Describe(string manifestJson)
    {
        if (DownloadsManifest.Parse(manifestJson) is not { Format: DownloadsManifest.CurrentFormat } manifest)
        {
            logger.LogWarning("The downloads manifest is not in a format this API reads");
            return DownloadsResponse.None;
        }

        DownloadFile Of(ExportedFile file) => new(Address(file.Path), file.Bytes, file.Sha256);
        return new DownloadsResponse(
            manifest.GeneratedAt,
            manifest.Fingerprint,
            [.. manifest.Texts.Select(t => new DownloadResponse(
                t.Slug, t.Name, t.NameNative, t.Language, t.Kind, t.Licence, t.LicenceUrl, t.RightsHolder, t.Citation,
                t.Books, t.Verses, Of(t.File), Of(t.Attribution)))]);
    }

    /// <summary>
    /// Where a file can be fetched from, or null where no base address is set. A path that climbs out of
    /// the folder or names another site is not a path the manifest could have written, and is not served.
    /// </summary>
    private string? Address(string path)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || path.Contains("..", StringComparison.Ordinal) || path.Contains(':') ||
            path.StartsWith('/'))
        {
            return null;
        }

        return $"{baseUrl.TrimEnd('/')}/{path}";
    }
}

internal static class DownloadsEndpoints
{
    public static void MapDownloads(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/downloads", (DownloadsCatalogue catalogue, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "public, max-age=300";
            return Results.Ok(catalogue.Current());
        });
}

/// <param name="GeneratedAt">When the export was written, or null where there is none.</param>
/// <param name="Fingerprint">One value naming every byte of every file: two exports with the same one are the same files.</param>
internal record DownloadsResponse(DateTimeOffset? GeneratedAt, string? Fingerprint, IReadOnlyList<DownloadResponse> Texts)
{
    public static readonly DownloadsResponse None = new(null, null, []);
}

/// <param name="Id">The text's identifier, as the rest of the API names it.</param>
/// <param name="Verses">How many verses the file holds.</param>
/// <param name="File">The text itself.</param>
/// <param name="Attribution">Who made it and on what terms, to be kept with the text wherever it goes.</param>
internal record DownloadResponse(
    string Id,
    string Name,
    string? NameNative,
    string Language,
    string Kind,
    string? Licence,
    string? LicenceUrl,
    string? RightsHolder,
    string? Citation,
    int Books,
    int Verses,
    DownloadFile File,
    DownloadFile Attribution);

/// <param name="Url">Where it can be fetched, or null while no address for the files is configured.</param>
/// <param name="Sha256">The checksum a downloader can compare the file with.</param>
internal record DownloadFile(string? Url, long Bytes, string Sha256);
