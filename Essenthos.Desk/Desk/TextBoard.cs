using System.Text.Json;
using System.Text.Json.Nodes;
using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Desk;

/// <summary>What a text is and where it came from, as its row in the corpus says, read afresh on every request.</summary>
/// <param name="Aliases">The other identifiers it answers to.</param>
internal sealed record TextAbout(
    string Slug,
    string Name,
    string? NameNative,
    string Kind,
    string Language,
    string Direction,
    string Versification,
    string? Translators,
    string? Editors,
    string? Edition,
    int? PublishedYear,
    int? EditionYear,
    string? About,
    string? RightsNote,
    string? SourceUrl,
    string? RightsHolder,
    string? Licence,
    string? LicenceUrl,
    string? Citation,
    string Redistribution,
    string? TextualFamily,
    IReadOnlyList<string> Aliases);

/// <param name="Count">What the last census counted of it, or null where it was not counted — a text loaded since, or no census yet.</param>
internal sealed record TextEntry(TextAbout About, TextCount? Count);

/// <param name="At">When the counts shown were taken, or null where there are none yet.</param>
/// <param name="LoadedAt">When the corpus was last loaded, now.</param>
/// <param name="Stale">Whether the corpus was loaded again after the counts were taken.</param>
/// <param name="Counting">Whether a census is running now.</param>
/// <param name="Problem">Why the last census failed, in a sentence, where it did.</param>
internal sealed record CensusState(
    string? At,
    double? Seconds,
    string? LoadedAt,
    bool Stale,
    bool Counting,
    string? CountingSince,
    string? Problem);

/// <param name="ReaderTranslation">The translation a new reader opens in, as the site's settings name it.</param>
internal sealed record TextsResponse(
    IReadOnlyList<TextEntry> Texts,
    CensusState Census,
    string ReaderTranslation,
    IReadOnlyList<BookName> BookNames);

/// <param name="RanAt">When the load that measured it finished.</param>
/// <param name="Measures">The measures of the last load's check that are about this text, each as the check wrote it.</param>
internal sealed record TextVerification(string? RanAt, int? Broken, JsonObject Measures);

/// <summary>
/// The texts of the corpus as the console shows them: each text's own row, read live because it is a
/// few dozen rows, beside a census of everything its words carry and how they are linked, which is
/// a minute's sweep of the corpus and so is counted when asked for, kept on disk and shown with the
/// time it was taken. Everything here only reads the corpus.
/// </summary>
internal sealed class TextBoard(IServiceScopeFactory scopes, DeskPaths paths, ILogger<TextBoard> logger)
{
    public const string CensusFile = "text-census.json";

    /// <summary>
    /// The measures of the load's check that are about one text: the measure, the field naming the
    /// text in it, and what the console calls the rows. A witness is measured from the other end —
    /// every translation reaching it — so those rows are kept too, apart.
    /// </summary>
    private static readonly (string Measure, string Field, string Name)[] PerText =
    [
        ("coverage", "text", "coverage"),
        ("reach", "from", "reach"),
        ("reach", "witness", "reachedBy"),
        ("contention", "text", "contention"),
        ("pairing", "text", "pairing"),
        ("crowding", "text", "crowding"),
        ("absence", "text", "absence"),
    ];

    private readonly Lock _lock = new();

    private TextCensusSnapshot? _census;

    private bool _read;

    private Task? _counting;

    private string? _countingSince;

    private string? _problem;

    public string CensusPath => Path.Combine(paths.Cache, CensusFile);

    public TextCensusSnapshot? Census
    {
        get
        {
            lock (_lock)
            {
                if (!_read)
                {
                    _census = ReadCensus();
                    _read = true;
                }

                return _census;
            }
        }
    }

    /// <summary>Starts a census, unless one is running already.</summary>
    public void Recount()
    {
        lock (_lock)
        {
            if (_counting is { IsCompleted: false })
            {
                return;
            }

            _problem = null;
            _countingSince = JsonFiles.Now();
            _counting = Task.Run(Count);
        }
    }

    /// <summary>Waits for the census that is running, if one is; for a test, which cannot watch the page.</summary>
    public Task Counted()
    {
        lock (_lock)
        {
            return _counting ?? Task.CompletedTask;
        }
    }

    public async Task<TextsResponse> List(AppDbContext db, CancellationToken cancellationToken)
    {
        if (Census is null)
        {
            lock (_lock)
            {
                if (_counting is null && _problem is null)
                {
                    _countingSince = JsonFiles.Now();
                    _counting = Task.Run(Count);
                }
            }
        }

        var census = Census;
        var texts = await db.Texts.AsNoTracking().OrderBy(t => t.Slug).ToListAsync(cancellationToken);
        var loadedAt = await LastLoad(db, cancellationToken);
        var counts = census?.Texts.ToDictionary(t => t.Text, StringComparer.OrdinalIgnoreCase) ?? [];

        return new TextsResponse(
            [.. texts.Select(text => new TextEntry(About(text), counts.GetValueOrDefault(text.Slug)))],
            State(census, loadedAt),
            SiteSettings.ReadChoices(paths.SiteSettings)[SiteSettings.ReaderTranslation],
            census?.BookNames ?? []);
    }

    /// <summary>
    /// What the last load's check measured about one text: its coverage, how its words reach each
    /// witness, where two sources dispute a word, which verse pairs look misaligned. Null where the
    /// corpus has no such text.
    /// </summary>
    public static async Task<TextVerification?> Verification(AppDbContext db, string slug, CancellationToken cancellationToken)
    {
        var text = await db.Texts.AsNoTracking().Where(t => t.Slug == slug).Select(t => t.Slug).FirstOrDefaultAsync(cancellationToken);
        if (text is null)
        {
            return null;
        }

        var run = await db.VerificationRuns.AsNoTracking()
            .OrderByDescending(r => r.RanAt)
            .Select(r => new { r.RanAt, r.Broken, r.Measures })
            .FirstOrDefaultAsync(cancellationToken);

        var measures = new JsonObject();
        if (run is null)
        {
            return new TextVerification(null, null, measures);
        }

        var all = JsonNode.Parse(run.Measures.RootElement.GetRawText()) as JsonObject;
        foreach (var (measure, field, name) in PerText)
        {
            if (all?[measure] is not JsonArray rows)
            {
                continue;
            }

            measures[name] = new JsonArray([
                .. rows.OfType<JsonObject>()
                    .Where(row => row[field] is JsonValue value && value.TryGetValue<string>(out var named)
                                  && string.Equals(named, text, StringComparison.OrdinalIgnoreCase))
                    .Select(row => row.DeepClone()),
            ]);
        }

        return new TextVerification(Stamp(run.RanAt), run.Broken, measures);
    }

    public static TextAbout About(Text text) => new(
        text.Slug,
        text.Name,
        text.NameNative,
        EnumSpelling.Of(text.Kind),
        text.Language,
        EnumSpelling.Of(text.Direction),
        EnumSpelling.Of(text.Versification),
        text.Translators,
        text.Editors,
        text.Edition,
        text.PublishedYear,
        text.EditionYear,
        text.About,
        text.RightsNote,
        text.SourceUrl,
        text.RightsHolder,
        text.Licence,
        text.LicenceUrl,
        text.Citation,
        EnumSpelling.Of(text.Redistribution),
        text.TextualFamily,
        TextAliases.Of(text.Slug));

    public static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

    private static async Task<string?> LastLoad(AppDbContext db, CancellationToken cancellationToken)
    {
        var at = await db.VerificationRuns.MaxAsync(r => (DateTimeOffset?)r.RanAt, cancellationToken);
        return at is { } ran ? Stamp(ran) : null;
    }

    private CensusState State(TextCensusSnapshot? census, string? loadedAt)
    {
        lock (_lock)
        {
            var counting = _counting is { IsCompleted: false };
            return new CensusState(
                census?.At,
                census?.Seconds,
                loadedAt,
                census is not null && loadedAt is not null && !string.Equals(census.LoadedAt, loadedAt, StringComparison.Ordinal),
                counting,
                counting ? _countingSince : null,
                _problem);
        }
    }

    private async Task Count()
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.OpenConnectionAsync();
            var census = await TextCensus.Count(db.Database.GetDbConnection(), inTransaction: false, CancellationToken.None);
            WriteCensus(census);
            lock (_lock)
            {
                _census = census;
                _read = true;
            }

            logger.LogInformation("Counted {Texts} texts in {Seconds} s", census.Texts.Count, census.Seconds);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The census of the texts failed");
            lock (_lock)
            {
                // Whatever stopped it is said on the page: a census that fails in silence looks like one still to come.
                _problem = exception is not IOException
                    ? "The corpus could not be read. Start core-db, and load the corpus if it is empty."
                    : $"The counts could not be kept: {exception.Message}";
            }
        }
    }

    private TextCensusSnapshot? ReadCensus()
    {
        if (!File.Exists(CensusPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(CensusPath);
            return JsonSerializer.Deserialize(stream, DeskJsonContext.Default.TextCensusSnapshot);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            // A census kept by an older console, or cut off mid-write: counted again rather than trusted.
            logger.LogWarning(exception, "The kept census at {Path} could not be read; the texts will be counted again", CensusPath);
            return null;
        }
    }

    private void WriteCensus(TextCensusSnapshot census)
    {
        Directory.CreateDirectory(paths.Cache);
        var written = CensusPath + ".writing";
        using (var stream = File.Create(written))
        {
            JsonSerializer.Serialize(stream, census, DeskJsonContext.Default.TextCensusSnapshot);
        }

        File.Move(written, CensusPath, overwrite: true);
    }
}
