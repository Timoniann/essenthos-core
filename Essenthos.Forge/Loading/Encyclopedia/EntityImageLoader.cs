using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Places">Place pictures taken from the gazetteer.</param>
/// <param name="Curated">Pictures taken from the curated list of public works.</param>
/// <param name="Generated">Our own pictures taken from the generated folder's manifest.</param>
/// <param name="Unlicensed">Pictures left out because their terms are not ones a picture is shown under.</param>
/// <param name="Uncredited">Pictures left out because nobody is named to credit.</param>
/// <param name="Missing">Pictures left out because their file is not in the images folder.</param>
/// <param name="Unheld">Pictures of a place or a person the encyclopedia holds no record of.</param>
/// <param name="Refused">Pictures of God other than the glory, which are never shown.</param>
/// <param name="Withheld">Our own pictures the owner has not approved yet or has rejected.</param>
/// <param name="Hidden">Pictures the owner hid in his console.</param>
internal sealed record EntityImageOutcome(
    int Places,
    int Curated,
    int Generated,
    int Unlicensed,
    int Uncredited,
    int Missing,
    int Unheld,
    int Refused,
    int Withheld,
    int Hidden,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        $"{Places} place pictures from the gazetteer, {Curated} curated public works and {Generated} of our " +
        $"own; left out: {Unlicensed} under terms that do not allow showing them, {Uncredited} with nobody " +
        $"to credit, {Missing} whose file is not in the images folder, {Unheld} of records the " +
        $"encyclopedia does not hold, {Refused} of God that were not the glory, {Withheld} of our own not " +
        $"approved and {Hidden} the owner hid, in {Elapsed}";
}

/// <summary>
/// The pictures of people and places: the gazetteer's photograph of each place, a curated list of
/// public-domain and openly licensed works, and our own generated portraits once there are any.
///
/// <para>
/// The files live under <c>Resources/Images</c>, outside the repository like the rest of the corpus
/// bytes, and a row names its file by a path under that folder. <c>scripts/fetch-images.ps1</c> puts
/// them there: it unpacks the gazetteer's thumbnail archive into <c>openbible/</c> and downloads each
/// curated work into the path its entry names. Generated portraits are dropped into
/// <c>generated/</c> by hand, beside a <c>manifest.json</c> of the same shape as the curated list.
/// </para>
///
/// <para>
/// A manifest is <c>{ "source", "credit"?, "licence"?, "licenceUrl"?, "images": [...] }</c>, the
/// top-level values standing for any entry that does not state its own. An entry is
/// <c>{ "entity": slug, "file": path under the images folder, "caption", "captions", "credit",
/// "creditUrl", "licence", "licenceUrl", "role": "primary" | "gallery", "focus": [x, y], "download",
/// "glory" }</c>; <c>captions</c> is the caption in a reader's language, by the three-letter code the
/// texts are tagged with — <c>{ "ukr": "...", "deu": "..." }</c> — for a picture a reader must be
/// told about in words he reads;
/// <c>download</c> is the address the fetch script takes the file from and is not read here, and
/// <c>glory</c> is below. An entry with no
/// credit or no licence is refused, because a picture with no credit under it reads as ours.
/// </para>
///
/// <para>
/// A generated entry may carry <c>"review"</c>: <c>pending</c> while the owner has not looked at it
/// and <c>rejected</c> once he has said no, and either keeps it off the site; <c>approved</c>, or no
/// review at all for an entry written by hand, shows it. And the owner's choices in
/// <c>Resources/Essenthos/image-choices.json</c> — a picture hidden, a different one leading, a
/// caption of his own — are applied over every list; see <see cref="ImageChoices"/>.
/// </para>
///
/// <para>
/// <strong>Rebuilt whole on every run.</strong> It is a thousand-odd rows read from three small
/// descriptions, so a guard asking whether it had already run would only stop a new portrait or a
/// corrected credit from arriving. Written in one transaction, so a reader never finds a page with
/// its picture gone.
/// </para>
///
/// <para>
/// God is never given a face or a figure. The one picture a record of God may have is ours, of the
/// glory as light with nothing inside it to see — Exodus 24:10, Ezekiel 1:27–28 — marked
/// <c>"glory": true</c> in the generated manifest by whoever put it there. Any other picture of
/// YHVH is refused whoever listed it, and so is every picture of a word the text uses of God.
/// </para>
/// </summary>
internal sealed class EntityImageLoader(AppDbContext db, ILogger<EntityImageLoader> logger)
{
    public const string Folder = "Images";

    public const string GeneratedFolder = "generated";

    public const string ManifestFile = "manifest.json";

    private const string CuratedResource = "Essenthos.Core.Loading.Encyclopedia.PublicImages.json";

    private const string GazetteerSource =
        "OpenBible.info Bible Geocoding thumbnails";

    public const string Public = "public";

    public const string Generated = "generated";

    public const string Primary = "primary";

    public const string Gallery = "gallery";

    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public async Task<EntityImageOutcome> Load(string resources, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var root = Path.Combine(resources, Folder);

        var entities = await db.Entities
            .Select(e => new { e.Id, e.Slug, e.Kind, e.SourceId, e.OpenBibleId })
            .ToListAsync(cancellationToken);
        var bySlug = entities.ToDictionary(e => e.Slug, StringComparer.Ordinal);

        // A picture listed under the address of a record since folded into another is a picture of
        // the record it became part of, and the owner's word on it stays keyed by the address it was
        // listed under.
        var byId = entities.ToDictionary(e => e.Id);
        foreach (var merged in await db.MergedRecords.Select(m => new { m.Slug, m.EntityId }).ToListAsync(cancellationToken))
        {
            if (byId.TryGetValue(merged.EntityId, out var into))
            {
                bySlug.TryAdd(merged.Slug, into);
            }
        }

        var byOpenBibleId = entities
            .Where(e => e.OpenBibleId != null)
            .ToLookup(e => e.OpenBibleId!, StringComparer.Ordinal);
        var depiction = entities.ToDictionary(e => e.Id, e => (e.Kind, e.SourceId));

        var candidates = new List<Candidate>();
        int unlicensed = 0, uncredited = 0, unheld = 0, withheld = 0;

        // The curated and generated lists first: somebody chose each of those, so where one names a
        // primary picture for a place it leads over the gazetteer's.
        foreach (var (manifest, kind) in Manifests(root))
        {
            foreach (var entry in manifest.Images)
            {
                if (!bySlug.TryGetValue(entry.Entity, out var entity))
                {
                    logger.LogWarning(
                        "{Manifest} lists a picture of \"{Slug}\", which the encyclopedia holds no record of. " +
                        "Correct the slug in the manifest, or remove the entry.",
                        manifest.Name, entry.Entity);
                    unheld++;
                    continue;
                }

                if (kind == Generated && entry.Review is ImageChoices.Pending or ImageChoices.Rejected)
                {
                    withheld++;
                    continue;
                }

                var credit = entry.Credit ?? manifest.Credit;
                var licence = entry.Licence ?? manifest.Licence;
                if (credit is null || licence is null)
                {
                    logger.LogWarning(
                        "{Manifest} lists {File} with no {Missing}. Every picture is shown with who made it and " +
                        "under what licence; state both in the entry or at the top of the manifest.",
                        manifest.Name, entry.File, credit is null ? "credit" : "licence");
                    if (credit is null)
                    {
                        uncredited++;
                    }
                    else
                    {
                        unlicensed++;
                    }

                    continue;
                }

                candidates.Add(new Candidate(
                    entity.Id,
                    entry.Entity,
                    kind,
                    entry.Role == Gallery ? Gallery : Primary,
                    entry.File.Replace('\\', '/'),
                    entry.Caption,
                    credit,
                    entry.CreditUrl,
                    licence,
                    entry.LicenceUrl ?? (entry.Licence is null ? manifest.LicenceUrl : null),
                    manifest.Source,
                    entry.Focus is [var x and >= 0 and <= 1, var y and >= 0 and <= 1] ? (x, y) : null,
                    entry.Glory == true,
                    Captions(entry.Captions)));
            }
        }

        var gazetteer = Path.Combine(resources, "OpenBible");
        var ancient = Path.Combine(gazetteer, OpenBibleThumbnails.AncientFile);
        var images = Path.Combine(gazetteer, OpenBibleThumbnails.ImageFile);
        if (File.Exists(ancient) && File.Exists(images))
        {
            var reading = OpenBibleThumbnails.Read(ancient, images);
            unlicensed += reading.Unlicensed;
            uncredited += reading.Uncredited;
            foreach (var thumbnail in reading.Thumbnails)
            {
                if (!byOpenBibleId.Contains(thumbnail.PlaceId))
                {
                    unheld++;
                    continue;
                }

                candidates.AddRange(byOpenBibleId[thumbnail.PlaceId].Select(place => new Candidate(
                    place.Id,
                    place.Slug,
                    Public,
                    Primary,
                    $"{OpenBibleThumbnails.Folder}/{thumbnail.File}",
                    thumbnail.Caption,
                    thumbnail.Credit,
                    thumbnail.CreditUrl,
                    thumbnail.Licence,
                    thumbnail.LicenceUrl,
                    GazetteerSource,
                    null,
                    false)));
            }
        }
        else
        {
            logger.LogWarning(
                "No place pictures: {Ancient} and {Images} are both needed. Run scripts/fetch-images.ps1, " +
                "which fetches the gazetteer's image metadata and unpacks its thumbnails.",
                ancient, images);
        }

        candidates = ImageChoices.Apply(
            candidates, ImageChoices.Read(Path.Combine(resources, ImageChoices.Folder, ImageChoices.FileName)), out var hidden);

        var refused = candidates.RemoveAll(c =>
        {
            var (kind, sourceId) = depiction[c.EntityId];
            return Refused(kind, sourceId, c.Kind, c.Glory);
        });
        if (refused > 0)
        {
            logger.LogError(
                "{Refused} pictures of God were listed and none is kept: God is never given a face or a figure. The " +
                "one picture a record of God may have is our own of the glory, light with no figure, listed in the " +
                "generated manifest with \"glory\": true. Remove the others from the manifest they came from.",
                refused);
        }

        var people = entities.Where(e => e.Kind == EntityKind.Person).Select(e => e.Id).ToHashSet();
        var (rows, missing) = Rows(root, candidates, PortraitFaces.Read(), people);

        // Its own transaction unless the caller already holds one, which is then the caller's to commit.
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await db.EntityImages.ExecuteDeleteAsync(cancellationToken);
        db.EntityImages.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var outcome = new EntityImageOutcome(
            rows.Count(r => r.Source == GazetteerSource),
            rows.Count(r => r.Kind == Public && r.Source != GazetteerSource),
            rows.Count(r => r.Kind == Generated),
            unlicensed,
            uncredited,
            missing,
            unheld,
            refused,
            withheld,
            hidden,
            started.Elapsed);
        logger.LogInformation("Pictured the people and places: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// Whether an entity may never be pictured: God, under every record the text is read to name him
    /// by, and every word the text uses of God.
    /// </summary>
    internal static bool NeverDepicted(EntityKind kind, string sourceId) => kind == EntityKind.Term || IsGod(sourceId);

    /// <summary>
    /// Whether a picture is refused: any picture of a word the text uses of God, and any picture of
    /// God but ours of the glory — a generated picture its manifest marks as light with no figure.
    /// </summary>
    internal static bool Refused(EntityKind kind, string sourceId, string pictureKind, bool glory) =>
        NeverDepicted(kind, sourceId) && !(kind != EntityKind.Term && IsGod(sourceId) && pictureKind == Generated && glory);

    private static bool IsGod(string sourceId) => DivineRecords.Contains(sourceId);

    /// <summary>
    /// The rows the candidates make, in the order they were listed: the first primary of each kind an
    /// entity is given stays primary and any after it joins the gallery, and a candidate whose file is
    /// absent or unreadable is counted and left out. A person's picture whose face was measured
    /// on this very file is given its head and shoulders, and its focus there where its list states
    /// none; a picture of the glory has no face to frame.
    /// </summary>
    internal (List<EntityImage> Rows, int Missing) Rows(
        string root,
        IEnumerable<Candidate> candidates,
        IReadOnlyDictionary<string, PortraitFaces.Measured>? faces = null,
        IReadOnlySet<int>? people = null)
    {
        var rows = new List<EntityImage>();
        var missing = 0;
        var unmeasured = 0;
        var files = new Dictionary<string, ((int Width, int Height)? Size, string Digest)?>(StringComparer.Ordinal);
        var taken = new HashSet<(int, string)>();
        var primaries = new HashSet<(int, string)>();
        var ordinals = new Dictionary<(int, string, string), int>();

        foreach (var candidate in candidates)
        {
            if (!files.TryGetValue(candidate.File, out var file))
            {
                var path = Path.GetFullPath(Path.Combine(root, candidate.File));
                file = ImageFiles.Extensions.Contains(Path.GetExtension(path))
                       && path.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
                       && File.Exists(path)
                    ? ImageFiles.Read(path)
                    : null;
                files[candidate.File] = file;
            }

            if (file is not { Size: { } size } read)
            {
                logger.LogDebug("No readable picture at {File} for {Slug}", candidate.File, candidate.Slug);
                missing++;
                continue;
            }

            if (!taken.Add((candidate.EntityId, candidate.File)))
            {
                continue;
            }

            var role = candidate.Role == Primary && primaries.Add((candidate.EntityId, candidate.Kind))
                ? Primary
                : Gallery;
            var key = (candidate.EntityId, candidate.Kind, role);
            var ordinal = ordinals.GetValueOrDefault(key);
            ordinals[key] = ordinal + 1;

            var portrait = people?.Contains(candidate.EntityId) == true && !candidate.Glory;
            var measured = faces?.GetValueOrDefault(candidate.File);
            if (portrait && measured?.Digest != read.Digest)
            {
                unmeasured++;
            }

            var bust = portrait && measured is { Face: { } face } && measured.Digest == read.Digest
                ? PortraitFaces.Bust(face, size.Width, size.Height)
                : null;
            var focus = candidate.Focus
                        ?? (bust is var (x, y, across, down) ? (x + across / 2, y + down / 2) : null);

            rows.Add(new EntityImage
            {
                EntityId = candidate.EntityId,
                Kind = candidate.Kind,
                Role = role,
                Ordinal = ordinal,
                File = candidate.File,
                Digest = read.Digest,
                Width = size.Width,
                Height = size.Height,
                Caption = candidate.Caption,
                Captions =
                [
                    .. (candidate.Captions ?? new Dictionary<string, string>()).Select(c =>
                        new EntityImageCaption { Language = c.Key, Caption = c.Value }),
                ],
                Credit = candidate.Credit,
                CreditUrl = candidate.CreditUrl,
                Licence = candidate.Licence,
                LicenceUrl = candidate.LicenceUrl,
                Source = candidate.Source,
                FocusX = focus?.X,
                FocusY = focus?.Y,
                BustX = bust?.X,
                BustY = bust?.Y,
                BustWidth = bust?.Width,
                BustHeight = bust?.Height,
            });
        }

        if (missing > 0)
        {
            logger.LogWarning(
                "{Missing} listed pictures have no readable file under {Root}. Run scripts/fetch-images.ps1, or " +
                "put the file where its manifest entry says.",
                missing, root);
        }

        if (unmeasured > 0 && faces is not null)
        {
            logger.LogWarning(
                "{Unmeasured} pictures of people have no face measured on the file as it is now, and are shown " +
                "small as whole figures. Run scripts/find-faces.ps1 under Windows PowerShell and rebuild.",
                unmeasured);
        }

        return (rows, missing);
    }

    /// <summary>The curated list inside the assembly, and the generated folder's manifest where there is one.</summary>
    private static IEnumerable<(ImageManifest Manifest, string Kind)> Manifests(string root)
    {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(CuratedResource)
                            ?? throw new InvalidOperationException(
                                $"{CuratedResource} is not embedded in the assembly. It is listed in " +
                                "Essenthos.Forge.csproj as an EmbeddedResource; rebuild."))
        {
            yield return (Parse(stream, "PublicImages.json"), Public);
        }

        var generated = Path.Combine(root, GeneratedFolder, ManifestFile);
        if (File.Exists(generated))
        {
            using var stream = File.OpenRead(generated);
            yield return (Parse(stream, generated), Generated);
        }
    }

    /// <summary>The captions an entry gives in other languages, by lower-cased code, without the empty ones.</summary>
    private static Dictionary<string, string>? Captions(IDictionary<string, string>? captions)
    {
        var given = captions?
            .Where(c => !string.IsNullOrWhiteSpace(c.Key) && !string.IsNullOrWhiteSpace(c.Value))
            .ToDictionary(c => c.Key.Trim().ToLowerInvariant(), c => c.Value.Trim(), StringComparer.Ordinal);
        return given is { Count: > 0 } ? given : null;
    }

    internal static ImageManifest Parse(Stream stream, string name) =>
        (JsonSerializer.Deserialize<ImageManifest>(stream, ManifestJson)
         ?? throw new InvalidDataException($"{name} is empty. It must hold {{ \"source\", \"images\": [...] }}."))
        with { Name = name };

    internal sealed record Candidate(
        int EntityId,
        string Slug,
        string Kind,
        string Role,
        string File,
        string? Caption,
        string Credit,
        string? CreditUrl,
        string Licence,
        string? LicenceUrl,
        string Source,
        (double X, double Y)? Focus,
        bool Glory,
        IReadOnlyDictionary<string, string>? Captions = null);
}

/// <param name="Source">The collection the pictures come from, as a row's source says it.</param>
internal sealed record ImageManifest(
    string Source,
    string? Credit,
    string? Licence,
    string? LicenceUrl,
    IList<ImageManifestEntry> Images)
{
    public string Name { get; init; } = string.Empty;
}

/// <param name="Entity">The slug of the person or place pictured.</param>
/// <param name="File">The path under the images folder.</param>
/// <param name="Download">Where the fetch script takes the file from. Not read by the loader.</param>
/// <param name="Glory">
/// That the picture is of the glory of God as light, with no face, body or figure in it: the one kind
/// of picture a record of God may have, and only from the generated manifest.
/// </param>
/// <param name="Review">The owner's word on one of our own: <c>pending</c>, <c>approved</c> or <c>rejected</c>.</param>
/// <param name="Captions">The caption in other languages, by three-letter language code.</param>
internal sealed record ImageManifestEntry(
    string Entity,
    string File,
    string? Caption = null,
    string? Credit = null,
    string? CreditUrl = null,
    string? Licence = null,
    string? LicenceUrl = null,
    string? Role = null,
    double[]? Focus = null,
    string? Download = null,
    bool? Glory = null,
    string? Review = null,
    IDictionary<string, string>? Captions = null);
