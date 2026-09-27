using System.Net;
using System.Text.Json;

namespace Essenthos.Core.Pages;

/// <summary>The reader's index.html, and what it calls its pages in each language, as one build has them.</summary>
internal sealed record ShellReading(string Html, IReadOnlyDictionary<string, PageWording> Wording)
{
    public PageWording WordingFor(SiteLanguage language) => Wording[language.Code];
}

/// <summary>
/// The reader's index.html, which every rendered page is written into, and its page wording.
///
/// Both are read from wherever the reader is served — the web container, in a deployment — rather than
/// built into this image, because the reader is deployed on its own: a copy baked in here would name
/// the scripts of whichever build this image happened to be made beside, and the day the reader is
/// redeployed without the API every page would load scripts the web container no longer has. Read
/// from the container, the shell and its scripts are always the same build. The wording is read from
/// <c>meta/&lt;language&gt;.json</c> beside the shell; where the build publishes none, the copy compiled
/// into the API stands in.
///
/// They are read again after <see cref="Freshness"/>, and a reading that fails keeps the last good one,
/// so a web container restarting costs nothing. Until the first reading succeeds there is no shell,
/// the page endpoint says so, and the proxy serves the plain application instead.
/// </summary>
internal sealed class PageShell(IConfiguration configuration, IHostEnvironment environment, ILogger<PageShell> logger)
    : IDisposable
{
    public const string ConfigurationKey = "Site:Shell";

    private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(3);

    private static readonly IReadOnlyDictionary<string, PageWording> Compiled =
        SiteLanguage.All.ToDictionary(language => language.Code, language => PageWording.Compiled(language.Code));

    private readonly HttpClient _http = new() { Timeout = Patience };

    private readonly SemaphoreSlim _gate = new(1, 1);

    private ShellReading? _reading;

    private DateTime _readAt = DateTime.MinValue;

    /// <summary>Where the shell is read from: an http address, or a file relative to the content root.</summary>
    public string? Source => configuration[ConfigurationKey] is { Length: > 0 } configured ? configured : null;

    public async Task<ShellReading?> Get(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow - _readAt < Freshness)
        {
            return _reading;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (DateTime.UtcNow - _readAt < Freshness)
            {
                return _reading;
            }

            if (Source is not { } source)
            {
                return null;
            }

            try
            {
                var html = await Read(source, cancellationToken)
                           ?? throw new FileNotFoundException($"Nothing is served at {source}.");
                var wording = new Dictionary<string, PageWording>();
                foreach (var language in SiteLanguage.All)
                {
                    wording[language.Code] = await Wording(source, language.Code, cancellationToken);
                }

                _reading = new ShellReading(html, wording);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException
                                                  or UnauthorizedAccessException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    exception,
                    "The reader's index.html could not be read from {Source}; {Fallback}. Check that {Key} names the web container's /index.html",
                    source, _reading is null ? "pages are left to the plain application" : "the last reading stands", ConfigurationKey);
            }

            // A failure is not retried on every request either: with a shell in hand the next attempt
            // waits as long as a success would have, and without one only a few seconds, since the
            // web container is most likely still starting.
            _readAt = _reading is null ? DateTime.UtcNow - Freshness + Retry : DateTime.UtcNow;
            return _reading;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PageWording> Wording(string shell, string code, CancellationToken cancellationToken)
    {
        var source = IsAddress(shell)
            ? new Uri(new Uri(shell), $"meta/{code}.json").ToString()
            : Path.Combine(Path.GetDirectoryName(shell) ?? "", "meta", $"{code}.json");
        try
        {
            return await Read(source, cancellationToken) is { } json ? PageWording.Parse(code, json) : Compiled[code];
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "The page wording at {Source} is not JSON; the API's own copy stands in", source);
            return Compiled[code];
        }
    }

    /// <summary>What is served at an address or kept in a file; null where there is nothing there.</summary>
    private async Task<string?> Read(string source, CancellationToken cancellationToken)
    {
        if (IsAddress(source))
        {
            using var response = await _http.GetAsync(source, cancellationToken);
            // A single-page application answers every address it does not have with its index.html,
            // so a page where a file was asked for is the file's absence.
            if (response.StatusCode == HttpStatusCode.NotFound
                || (!source.EndsWith(".html", StringComparison.Ordinal)
                    && response.Content.Headers.ContentType?.MediaType == "text/html"))
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        var path = Path.Combine(environment.ContentRootPath, source);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    }

    private static bool IsAddress(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
