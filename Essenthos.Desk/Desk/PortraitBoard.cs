using System.Text.Json;
using System.Text.Json.Nodes;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Desk;

/// <param name="Verses">How many verses name the person, as the encyclopedia counts references.</param>
/// <param name="Tier">1 for the people named in 50 verses or more, 2 for 10 to 49, 3 for the rest.</param>
/// <param name="GeneratedListed">Whether the generated manifest lists a portrait whose file is there.</param>
/// <param name="GeneratedLoaded">Whether the corpus already shows a generated portrait.</param>
/// <param name="NeverPictured">God: never given a face or a figure.</param>
/// <param name="Glory">Whether the manifest lists the picture of the glory, the one kind God's records may have.</param>
internal sealed record PortraitPerson(
    string Slug,
    string Name,
    string? LocalName,
    string? Distinguisher,
    string? Sex,
    int Verses,
    int Tier,
    bool PublicImage,
    bool GeneratedListed,
    bool GeneratedLoaded,
    bool Brief,
    bool NeverPictured,
    bool Glory);

/// <param name="Problem">What could not be read beside the corpus — the manifest or the briefs — in a sentence.</param>
internal sealed record PortraitsResponse(IReadOnlyList<PortraitPerson> People, bool Manifest, bool Briefs, string? Problem);

/// <param name="File">The path under the images folder, which is also where the console serves it.</param>
/// <param name="Loaded">Whether the corpus holds it, rather than only a manifest listing it.</param>
/// <param name="OnDisk">Whether the file is there.</param>
internal sealed record PortraitImage(
    string File,
    string Kind,
    string Role,
    string? Caption,
    string? Credit,
    string? Licence,
    string? Source,
    bool Loaded,
    bool OnDisk,
    bool Glory);

/// <param name="Brief">What the briefs file says about the person, as it says it, or null.</param>
internal sealed record PortraitDetail(PortraitPerson Person, JsonNode? Brief, IReadOnlyList<PortraitImage> Images);

/// <summary>
/// The people of the encyclopedia as a portrait list: how often the text names each, which already
/// has a picture, which has a generated portrait listed or loaded, and which has a brief for the
/// artist. Read from the corpus and from the two files beside the generated portraits.
/// </summary>
internal sealed class PortraitBoard(AppDbContext db, DeskPaths paths)
{
    public const int FirstTier = 50;

    public const int SecondTier = 10;

    public const string ManifestFile = "manifest.json";

    public const string BriefsFile = "briefs.json";

    /// <summary>The records the text is read to name God by, which are never given a face or a figure.</summary>
    public const string GodSourcePrefix = "person:YHVH_";

    private const string Public = "public";

    private const string Generated = "generated";

    private static readonly JsonDocumentOptions Lenient = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static int TierOf(int verses) => verses >= FirstTier ? 1 : verses >= SecondTier ? 2 : 3;

    public async Task<PortraitsResponse> List(CancellationToken cancellationToken)
    {
        var (manifest, briefs, problem) = Beside();
        var people = await People(db.Entities.Where(e => e.Kind == EntityKind.Person), cancellationToken);
        return new PortraitsResponse(
            people.Select(p => Person(p, manifest, briefs)).OrderByDescending(p => p.Verses).ThenBy(p => p.Name).ToList(),
            manifest is not null,
            briefs is not null,
            problem);
    }

    public async Task<PortraitDetail?> Detail(string slug, CancellationToken cancellationToken)
    {
        var (manifest, briefs, _) = Beside();
        var row = (await People(db.Entities.Where(e => e.Slug == slug && e.Kind == EntityKind.Person), cancellationToken))
            .SingleOrDefault();
        if (row is null)
        {
            return null;
        }

        var loaded = await db.EntityImages
            .Where(i => i.EntityId == row.Id)
            .OrderBy(i => i.Kind).ThenBy(i => i.Role).ThenBy(i => i.Ordinal)
            .ToListAsync(cancellationToken);

        var images = loaded
            .Select(i => new PortraitImage(i.File, i.Kind, i.Role, i.Caption, i.Credit, i.Licence, i.Source, true,
                OnDisk(i.File), Listed(manifest, slug).Any(e => Glory(e) && File(e) == i.File)))
            .ToList();
        foreach (var entry in Listed(manifest, slug).Where(e => images.All(i => i.File != File(e))))
        {
            images.Add(new PortraitImage(
                File(entry),
                Generated,
                entry["role"]?.GetValue<string>() ?? "primary",
                entry["caption"]?.GetValue<string>(),
                (entry["credit"] ?? manifest?["credit"])?.GetValue<string>(),
                (entry["licence"] ?? manifest?["licence"])?.GetValue<string>(),
                manifest?["source"]?.GetValue<string>(),
                false,
                OnDisk(File(entry)),
                Glory(entry)));
        }

        return new PortraitDetail(Person(row, manifest, briefs), BriefOf(briefs, slug)?.DeepClone(), images);
    }

    private async Task<List<Row>> People(IQueryable<Entity> entities, CancellationToken cancellationToken) =>
        await entities
            .Select(e => new Row(
                e.Id,
                e.Slug,
                e.Name,
                db.EntityNameForms
                    .Where(f => f.EntityId == e.Id && f.Language == "ukr" && f.GrammaticalCase == GrammaticalCases.Nominative)
                    .Select(f => f.Form)
                    .FirstOrDefault(),
                e.Distinguisher,
                e.Sex,
                e.SourceId,
                e.Verses.Select(v => new { v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse }).Distinct().Count(),
                e.Images.Any(i => i.Kind == Public),
                e.Images.Any(i => i.Kind == Generated)))
            .ToListAsync(cancellationToken);

    private PortraitPerson Person(Row row, JsonNode? manifest, JsonArray? briefs)
    {
        var listed = Listed(manifest, row.Slug).ToList();
        return new PortraitPerson(
            row.Slug,
            row.Name,
            row.LocalName,
            row.Distinguisher,
            row.Sex,
            row.Verses,
            TierOf(row.Verses),
            row.Public,
            listed.Any(e => OnDisk(File(e))),
            row.Generated,
            BriefOf(briefs, row.Slug) is not null,
            row.SourceId.StartsWith(GodSourcePrefix, StringComparison.Ordinal),
            listed.Any(Glory));
    }

    /// <summary>The generated manifest and the briefs, each null where it is absent or cannot be read now.</summary>
    private (JsonNode? Manifest, JsonArray? Briefs, string? Problem) Beside()
    {
        var problems = new List<string>();
        var manifest = ReadOptional(Path.Combine(paths.Generated, ManifestFile), problems);
        var briefs = ReadOptional(Path.Combine(paths.Generated, BriefsFile), problems) as JsonArray;
        return (manifest, briefs, problems.Count > 0 ? string.Join(" ", problems) : null);
    }

    private static JsonNode? ReadOptional(string path, List<string> problems)
    {
        if (!System.IO.File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(System.IO.File.ReadAllText(path), documentOptions: Lenient);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            // The briefs are written while the console runs; a read that lands mid-write is tried again on the next load.
            problems.Add($"{Path.GetFileName(path)} could not be read just now ({exception.Message}).");
            return null;
        }
    }

    private static IEnumerable<JsonNode> Listed(JsonNode? manifest, string slug) =>
        (manifest?["images"]?.AsArray() ?? [])
        .OfType<JsonNode>()
        .Where(e => e["entity"]?.GetValue<string>() == slug && e["file"] is not null);

    private static JsonNode? BriefOf(JsonArray? briefs, string slug) =>
        briefs?.OfType<JsonNode>().FirstOrDefault(b => b["slug"]?.GetValue<string>() == slug);

    private static string File(JsonNode entry) => entry["file"]!.GetValue<string>().Replace('\\', '/');

    private static bool Glory(JsonNode entry) => entry["glory"]?.GetValueKind() == JsonValueKind.True;

    private bool OnDisk(string file) => DeskPaths.Under(paths.Images, file) is { } path && System.IO.File.Exists(path);

    private sealed record Row(
        int Id,
        string Slug,
        string Name,
        string? LocalName,
        string? Distinguisher,
        string? Sex,
        string SourceId,
        int Verses,
        bool Public,
        bool Generated);
}
