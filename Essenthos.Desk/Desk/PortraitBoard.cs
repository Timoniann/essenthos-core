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
/// <param name="NeverPictured">God, under any of his records, the Holy Spirit's among them: never given a face or a figure.</param>
/// <param name="Glory">Whether the manifest lists the picture of the glory, the one kind God's records may have.</param>
/// <param name="Status">Where the portrait stands: one of <see cref="PortraitBoard.Statuses"/>.</param>
/// <param name="Question">What the brief says is still the owner's to decide about the portrait, or null.</param>
/// <param name="Waiting">Whether something here waits on the owner: a picture for his word, or the brief's question while the portrait is unsettled.</param>
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
    bool Glory,
    string Status,
    string? Question,
    bool Waiting);

/// <param name="Problem">What could not be read beside the corpus — the manifest or the briefs — in a sentence.</param>
internal sealed record PortraitsResponse(IReadOnlyList<PortraitPerson> People, bool Manifest, bool Briefs, string? Problem);

/// <param name="File">The path under the images folder, which is also where the console serves it.</param>
/// <param name="Loaded">Whether the corpus holds it, rather than only a list naming it.</param>
/// <param name="OnDisk">Whether the file is there.</param>
/// <param name="Review">For one of our own: <c>pending</c>, <c>approved</c>, <c>rejected</c>, or null where nobody wrote one.</param>
/// <param name="Choice">What the owner chose about it, or null where he chose nothing.</param>
/// <param name="Option">The brief's option it was drawn from, where the prompt it was drawn from is one of them.</param>
/// <param name="Shared">The other people whose pages our manifest lists the same picture for.</param>
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
    bool Glory,
    string? Review,
    PictureChoice? Choice,
    PortraitOption? Option,
    IReadOnlyList<PortraitShare> Shared);

/// <param name="Id">The option's letter in the brief.</param>
internal sealed record PortraitOption(string Id, string? Label);

internal sealed record PortraitShare(string Slug, string Name, string? LocalName);

/// <param name="Brief">What the briefs file says about the person, as it says it, or null.</param>
internal sealed record PortraitDetail(PortraitPerson Person, JsonNode? Brief, IReadOnlyList<PortraitImage> Images);

/// <param name="Kind"><c>person</c> or <c>place</c>.</param>
/// <param name="Leading">The file the page leads with now, for a thumbnail, or null.</param>
/// <param name="Pictures">How many pictures the corpus holds of it.</param>
/// <param name="Chosen">How many of them the owner chose something about.</param>
internal sealed record PicturedEntity(
    string Slug,
    string Name,
    string? LocalName,
    string Kind,
    string? Distinguisher,
    string? Leading,
    int Pictures,
    int Hidden,
    int Chosen);

internal sealed record PicturedResponse(IReadOnlyList<PicturedEntity> Entities);

internal sealed record PictureSet(PicturedEntity Entity, IReadOnlyList<PortraitImage> Images);

/// <summary>
/// The people of the encyclopedia as a portrait list — how often the text names each, which already
/// has a picture, where their generated portrait stands and whether the artist has a brief — and the
/// pictures of every person and place, with the owner's choices about each. Read from the corpus
/// and from the files beside the pictures.
/// </summary>
internal sealed class PortraitBoard(AppDbContext db, DeskPaths paths)
{
    public const int FirstTier = 50;

    public const int SecondTier = 10;

    public const string ManifestFile = "manifest.json";

    public const string BriefsFile = "briefs.json";

    public const string NotStarted = "not-started";

    /// <summary>A brief is written and the portrait can be drawn from it.</summary>
    public const string Ready = "ready";

    public const string ToGenerate = "to-generate";

    public const string Generated = "generated";

    public const string Approved = "approved";

    public const string Rejected = "rejected";

    /// <summary>The owner decided this person is not portrayed at all.</summary>
    public const string NoPortrait = "no-portrait";

    public static readonly IReadOnlyList<string> Statuses = [NotStarted, Ready, ToGenerate, Generated, Approved, Rejected, NoPortrait];

    /// <summary>The statuses a portrait is still on its way in; the others are the owner's settled word.</summary>
    public static readonly IReadOnlySet<string> Unsettled = new HashSet<string>(StringComparer.Ordinal)
    {
        NotStarted, Ready, ToGenerate, Generated,
    };

    private const string Public = "public";

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

        var brief = BriefOf(briefs, slug);
        return new PortraitDetail(
            Person(row, manifest, briefs),
            brief?.DeepClone(),
            await Images(row.Id, slug, manifest, brief, cancellationToken));
    }

    /// <summary>Every person and place with a picture, or with a choice the owner made about one.</summary>
    public async Task<PicturedResponse> Pictured(CancellationToken cancellationToken)
    {
        var choices = PictureChoices.Read(paths);
        var chosen = choices.Keys.ToList();
        var rows = await Summaries(
            db.Entities.Where(e => (e.Kind == EntityKind.Person || e.Kind == EntityKind.Place)
                                   && (e.Images.Any() || chosen.Contains(e.Slug))),
            cancellationToken);
        return new PicturedResponse(rows
            .Select(row => Pictured(row, choices.GetValueOrDefault(row.Slug) ?? []))
            .OrderBy(e => e.Kind).ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList());
    }

    public async Task<PictureSet?> Pictures(string slug, CancellationToken cancellationToken)
    {
        var row = (await Summaries(db.Entities.Where(e => e.Slug == slug), cancellationToken)).SingleOrDefault();
        if (row is null)
        {
            return null;
        }

        var (manifest, briefs, _) = Beside();
        return new PictureSet(
            Pictured(row, PictureChoices.Read(paths).GetValueOrDefault(slug) ?? []),
            await Images(row.Id, slug, manifest, BriefOf(briefs, slug), cancellationToken));
    }

    /// <summary>
    /// The pictures of one entity: those the corpus holds, those our manifest lists that it does not
    /// hold yet, and those the owner hid, which the corpus no longer holds once the pictures are loaded
    /// again and which he must still be able to bring back.
    /// </summary>
    private async Task<List<PortraitImage>> Images(
        int id, string slug, JsonNode? manifest, JsonNode? brief, CancellationToken cancellationToken)
    {
        var choices = (PictureChoices.Read(paths).GetValueOrDefault(slug) ?? []).ToDictionary(c => c.File, StringComparer.Ordinal);
        var listed = Listed(manifest, slug).ToList();
        var shared = await SharedOf(manifest, slug, cancellationToken);
        var loaded = await db.EntityImages
            .Where(i => i.EntityId == id)
            .OrderBy(i => i.Kind).ThenBy(i => i.Role).ThenBy(i => i.Ordinal)
            .ToListAsync(cancellationToken);

        var images = loaded
            .Select(i =>
            {
                var entry = listed.FirstOrDefault(e => File(e) == i.File);
                return new PortraitImage(i.File, i.Kind, i.Role, i.Caption, i.Credit, i.Licence, i.Source, true,
                    OnDisk(i.File), entry is not null && Glory(entry), entry is null ? null : Review(entry),
                    choices.GetValueOrDefault(i.File), entry is null ? null : OptionOf(entry, brief),
                    shared.GetValueOrDefault(i.File) ?? []);
            })
            .ToList();
        foreach (var entry in listed.Where(e => images.All(i => i.File != File(e))))
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
                Glory(entry),
                Review(entry),
                choices.GetValueOrDefault(File(entry)),
                OptionOf(entry, brief),
                shared.GetValueOrDefault(File(entry)) ?? []));
        }

        foreach (var choice in choices.Values.Where(c => images.All(i => i.File != c.File)))
        {
            images.Add(new PortraitImage(choice.File, Public, "gallery", null, null, null, null, false, OnDisk(choice.File),
                false, null, choice, null, []));
        }

        return images;
    }

    /// <summary>For each picture our manifest lists for the person, the other people it lists the same file for.</summary>
    private async Task<Dictionary<string, List<PortraitShare>>> SharedOf(
        JsonNode? manifest, string slug, CancellationToken cancellationToken)
    {
        var files = Listed(manifest, slug).Select(File).ToHashSet(StringComparer.Ordinal);
        var others = (manifest?["images"]?.AsArray() ?? [])
            .OfType<JsonNode>()
            .Where(e => e["file"] is not null && e["entity"]?.GetValue<string>() is { } entity && entity != slug
                        && files.Contains(File(e)))
            .Select(e => (File: File(e), Slug: e["entity"]!.GetValue<string>()))
            .ToList();
        if (others.Count == 0)
        {
            return [];
        }

        var slugs = others.Select(o => o.Slug).Distinct().ToList();
        var names = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .Select(e => new PortraitShare(
                e.Slug,
                e.Name,
                db.EntityNameForms
                    .Where(f => f.EntityId == e.Id && f.Language == "ukr" && f.GrammaticalCase == GrammaticalCases.Nominative)
                    .Select(f => f.Form)
                    .FirstOrDefault()))
            .ToDictionaryAsync(s => s.Slug, StringComparer.Ordinal, cancellationToken);
        return others
            .GroupBy(o => o.File, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(o => names.GetValueOrDefault(o.Slug) ?? new PortraitShare(o.Slug, o.Slug, null))
                    .DistinctBy(s => s.Slug)
                    .ToList(),
                StringComparer.Ordinal);
    }

    /// <summary>The brief's option whose prompt the picture was drawn from, as its provenance records the prompt.</summary>
    internal static PortraitOption? OptionOf(JsonNode entry, JsonNode? brief)
    {
        if (entry["provenance"]?["brief_prompt"] is not JsonValue drawn || !drawn.TryGetValue<string>(out var prompt)
            || brief?["options"] is not JsonArray options)
        {
            return null;
        }

        var option = options.OfType<JsonObject>().FirstOrDefault(o =>
            o["prompt"] is JsonValue value && value.TryGetValue<string>(out var text) && text == prompt);
        return option?["id"] is JsonValue id && id.TryGetValue<string>(out var letter)
            ? new PortraitOption(letter, option["label"] is JsonValue label && label.TryGetValue<string>(out var said) ? said : null)
            : null;
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

    private async Task<List<Summary>> Summaries(IQueryable<Entity> entities, CancellationToken cancellationToken) =>
        await entities
            .Select(e => new Summary(
                e.Id,
                e.Slug,
                e.Name,
                db.EntityNameForms
                    .Where(f => f.EntityId == e.Id && f.Language == "ukr" && f.GrammaticalCase == GrammaticalCases.Nominative)
                    .Select(f => f.Form)
                    .FirstOrDefault(),
                e.Kind,
                e.Distinguisher,
                e.Images.OrderBy(i => i.Role == "primary" ? 0 : 1).ThenBy(i => i.Kind == Generated ? 0 : 1)
                    .Select(i => i.File).FirstOrDefault(),
                e.Images.Count()))
            .ToListAsync(cancellationToken);

    private static PicturedEntity Pictured(Summary row, IReadOnlyCollection<PictureChoice> choices) => new(
        row.Slug,
        row.Name,
        row.LocalName,
        row.Kind == EntityKind.Place ? "place" : "person",
        row.Distinguisher,
        choices.FirstOrDefault(c => c.Primary)?.File ?? row.Leading,
        row.Pictures,
        choices.Count(c => c.Hidden),
        choices.Count);

    private PortraitPerson Person(Row row, JsonNode? manifest, JsonArray? briefs)
    {
        var listed = Listed(manifest, row.Slug).ToList();
        var brief = BriefOf(briefs, row.Slug);
        var neverPictured = DivineRecords.Contains(row.SourceId);
        var status = StatusOf(brief, listed, neverPictured);
        var question = brief?["owner_decision_needed"] is JsonValue asked && asked.TryGetValue<string>(out var text)
                       && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
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
            brief is not null,
            neverPictured,
            listed.Any(Glory),
            status,
            question,
            Waits(listed, question, status));
    }

    /// <summary>
    /// Whether the portrait waits on the owner: a picture of it waits for his word, or its brief asks
    /// him something while the portrait is still on its way. A brief still to write or a picture still
    /// to draw waits on whoever writes and draws them, not on him.
    /// </summary>
    public static bool Waits(IReadOnlyCollection<JsonNode> listed, string? question, string status) =>
        listed.Any(e => Review(e) == PortraitEditor.Pending) || (question is not null && Unsettled.Contains(status));

    /// <summary>
    /// Where a portrait stands: as its brief says, where the brief says one of the statuses; else, where
    /// our manifest lists a portrait, as the owner reviewed it — one written by hand with no review is
    /// shown on the site, so it counts as approved; else a brief is ready or nothing is started — except
    /// for a record of God, which has no portrait to start until the picture of the glory is listed.
    /// </summary>
    public static string StatusOf(JsonNode? brief, IReadOnlyCollection<JsonNode> listed, bool neverPictured = false)
    {
        var said = brief?["status"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        if (said is not null && Statuses.Contains(said))
        {
            return said;
        }

        var reviews = listed.Select(Review).ToList();
        return reviews switch
        {
            [] when neverPictured => NoPortrait,
            [] => brief is null ? NotStarted : Ready,
            _ when reviews.Contains(PortraitEditor.Pending) => Generated,
            _ when reviews.All(r => r == PortraitEditor.Rejected) => Rejected,
            _ => Approved,
        };
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

    internal static IEnumerable<JsonNode> Listed(JsonNode? manifest, string slug) =>
        (manifest?["images"]?.AsArray() ?? [])
        .OfType<JsonNode>()
        .Where(e => e["entity"]?.GetValue<string>() == slug && e["file"] is not null);

    internal static JsonNode? BriefOf(JsonArray? briefs, string slug) =>
        briefs?.OfType<JsonNode>().FirstOrDefault(b => b["slug"]?.GetValue<string>() == slug);

    internal static string File(JsonNode entry) => entry["file"]!.GetValue<string>().Replace('\\', '/');

    private static bool Glory(JsonNode entry) => entry["glory"]?.GetValueKind() == JsonValueKind.True;

    internal static string? Review(JsonNode entry) =>
        entry["review"] is JsonValue value && value.TryGetValue<string>(out var review) ? review : null;

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

    private sealed record Summary(
        int Id,
        string Slug,
        string Name,
        string? LocalName,
        EntityKind Kind,
        string? Distinguisher,
        string? Leading,
        int Pictures);
}
