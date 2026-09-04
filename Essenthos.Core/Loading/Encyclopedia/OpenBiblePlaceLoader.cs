using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record PlacesOutcome(
    bool AlreadyLoaded,
    int Places,
    int Joined,
    int Added,
    int References,
    int Unaddressed,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the second places source is already loaded"
            : $"{Places} places with {References} references — {Joined} of them joined onto a place " +
              $"the other dataset already had and {Added} new — and {Unaddressed} citations naming a " +
              $"verse the canonical frame does not hold, in {Elapsed}";
}

/// <summary>
/// OpenBible.info's Bible Geocoding: the place layer's second source, and the one that makes it a
/// layer at all.
///
/// BibleData's places are marked in progress by their own author and read that way: 118 places,
/// 492 verses, and every one of them in Genesis or Exodus, so Jerusalem's page said the text names
/// it once. This states 1,342 places and 8,742 place-verse references across 61 books, with
/// Jerusalem at 955.
///
/// **It is not a replacement.** 111 of BibleData's 118 places already carry OpenBible's identifier,
/// 109 of which OpenBible still has, and those join onto the entity that exists rather than
/// standing beside it as a second Jerusalem —
/// but the references themselves each carry the name of the dataset that stated them, so a count
/// is never a blend of two sources presented as one claim.
///
/// **CC BY 4.0**, which is why it and not the alternative. Theographic states 7,310 place-verse
/// references and is CC BY-SA 4.0; share-alike at that scale reaches everything built on top of
/// it, and RUL-0183 puts that clause, not the non-commercial one, at the line. Measured against
/// the King James text the corpus already serves, of the 7,600 references OpenBible says the King
/// James itself carries a name for, 99.1% have that name in the verse; the 64 that do not were
/// read one by one and every one is a name too short for the check, a spelling the source records
/// under a different heading, or a psalm superscription the corpus's own King James text drops.
///
/// Only <c>ancient.jsonl</c> is read. The coordinates and geometry beside it are partly
/// OpenStreetMap's and carry ODbL, so they are neither fetched nor loaded — see the LICENCE.md
/// kept beside the data.
/// </summary>
internal sealed partial class OpenBiblePlaceLoader(AppDbContext db, ILogger<OpenBiblePlaceLoader> logger)
{
    private const string Source =
        "OpenBible.info Bible Geocoding, github.com/openbibleinfo/Bible-Geocoding-Data, CC BY 4.0";

    private const string FileName = "ancient.jsonl";

    /// <summary>
    /// The source disambiguates same-named places by a trailing index — <em>Aroer 2</em>. That is
    /// an ordinal in its own catalogue and not part of the name, so it comes off; what tells the
    /// places apart on a page is the identification beside it.
    /// </summary>
    [GeneratedRegex(@"\s+\d+$")]
    private static partial Regex TrailingIndex();

    /// <summary>
    /// Descriptions carry inline markup naming other entries — <c>along the &lt;modern
    /// id="m664b51"&gt;Wadi el Esh&lt;/modern&gt;</c>. The corpus has no page for those ids yet, so
    /// the tags come out and the words stay.
    /// </summary>
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Markup();

    public async Task<PlacesOutcome> Load(string folder, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var file = Path.Combine(folder, FileName);
        if (!File.Exists(file))
        {
            logger.LogWarning(
                "No second places source: {File} is not there. Run scripts/fetch-openbible.ps1. The " +
                "place layer stands on BibleData alone, which reaches Genesis and Exodus only.",
                file);
            return new PlacesOutcome(true, 0, 0, 0, 0, 0, started.Elapsed);
        }

        if (await db.EntityVerses.AnyAsync(v => v.Source == Source, cancellationToken))
        {
            return new PlacesOutcome(true, 0, 0, 0, 0, 0, started.Elapsed);
        }

        var places = Read(file);

        var byOpenBibleId = await db.Entities
            .Where(e => e.Kind == EntityKind.Place && e.OpenBibleId != null)
            .ToDictionaryAsync(e => e.OpenBibleId!, cancellationToken);
        var slugs = (await db.Entities.Select(e => e.Slug).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var shared = places
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        var added = new List<Entity>();
        var entities = new Dictionary<string, Entity>(places.Count, StringComparer.Ordinal);

        foreach (var place in places)
        {
            if (byOpenBibleId.TryGetValue(place.Id, out var existing))
            {
                entities[place.Id] = existing;
                continue;
            }

            var entity = new Entity
            {
                Kind = EntityKind.Place,
                Slug = Unique(Slugs.Of(place.Name), slugs),
                Name = place.Name,
                Distinguisher = shared.Contains(place.Name) ? place.Identification : null,
                PlaceKind = place.Kind,
                ModernEquivalent = place.Identification == place.Name ? null : place.Identification,
                OpenBibleId = place.Id,
                SourceId = $"openbible:{place.Id}",
                Source = Source,
            };

            entities[place.Id] = entity;
            added.Add(entity);
        }

        db.Entities.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);

        var references = new List<EntityVerse>(9_000);
        var unaddressed = 0;

        foreach (var place in places)
        {
            var entity = entities[place.Id];
            foreach (var citation in place.Verses)
            {
                if (Reference(citation) is not { } reference)
                {
                    unaddressed++;
                    continue;
                }

                references.Add(new EntityVerse
                {
                    EntityId = entity.Id,
                    CanonicalBook = reference.Book,
                    CanonicalChapter = reference.Chapter,
                    CanonicalVerse = reference.Verse,
                    Disputed = false,
                    Source = Source,
                });
            }
        }

        db.EntityVerses.AddRange(references);
        await db.SaveChangesAsync(cancellationToken);

        if (unaddressed > 0)
        {
            logger.LogWarning(
                "{Rows} of the second places source's citations name a book the canonical frame does " +
                "not hold and were dropped.",
                unaddressed);
        }

        var outcome = new PlacesOutcome(
            false,
            places.Count,
            places.Count - added.Count,
            added.Count,
            references.Count,
            unaddressed,
            started.Elapsed);

        logger.LogInformation("Loaded the second places source: {Outcome}", outcome);
        return outcome;
    }

    /// <param name="Verses">
    /// Where the place is named, as the source addresses it. Its own frame is the ESV's, and every
    /// one of its citations lands on a verse this corpus already holds; the two it knows the King
    /// James numbers differently it says so about itself, and those are taken at the King James
    /// address because that is the numbering the canonical frame keeps.
    /// </param>
    internal sealed record Place(
        string Id,
        string Name,
        string? Identification,
        string? Kind,
        IReadOnlyList<string> Verses);

    internal static List<Place> Read(string file)
    {
        var places = new List<Place>(1_400);

        foreach (var line in File.ReadLines(file))
        {
            if (line.Length == 0)
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            var id = Text(root, "id");
            var friendly = Text(root, "friendly_id");
            if (id is null || friendly is null)
            {
                continue;
            }

            places.Add(new Place(
                id,
                TrailingIndex().Replace(friendly, string.Empty),
                Identification(root),
                Kinds(root),
                [.. Citations(root)]));
        }

        return places;
    }

    /// <summary>
    /// Where the scholarship puts the place, in the source's own words: <em>Khirbet Ayun Musa</em>,
    /// <em>between Dedan and Kedar</em>, <em>another name for Bethel 1</em>. Every entry has one,
    /// and the first is the one the dataset itself orders first.
    /// </summary>
    private static string? Identification(JsonElement root)
    {
        if (!root.TryGetProperty("identifications", out var identifications))
        {
            return null;
        }

        foreach (var identification in identifications.EnumerateArray())
        {
            if (Text(identification, "description") is { } description)
            {
                var stripped = Markup().Replace(description, string.Empty).Trim();
                if (stripped.Length > 0)
                {
                    return stripped;
                }
            }
        }

        return null;
    }

    private static string? Kinds(JsonElement root)
    {
        if (!root.TryGetProperty("types", out var types) || types.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var kinds = string.Join(", ", types.EnumerateArray()
            .Select(type => type.GetString())
            .Where(type => !string.IsNullOrWhiteSpace(type)));

        return kinds.Length > 0 ? kinds : null;
    }

    /// <summary>
    /// The source states each citation in its own frame and, where a translation puts the words in
    /// a different verse, states that too. Two rows in the whole file carry a King James
    /// alternative and it is taken, because the canonical frame numbers verses as the King James
    /// does.
    /// </summary>
    private static IEnumerable<string> Citations(JsonElement root)
    {
        if (!root.TryGetProperty("verses", out var verses) || verses.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var verse in verses.EnumerateArray())
        {
            var citation = Text(verse, "osis");
            if (verse.TryGetProperty("alternate_verses", out var alternates)
                && alternates.ValueKind == JsonValueKind.Object
                && Text(alternates, "kjv") is { } instead)
            {
                citation = instead;
            }

            if (citation is not null)
            {
                yield return citation;
            }
        }
    }

    internal static (int Book, int Chapter, int Verse)? Reference(string citation)
    {
        var parts = citation.Split('.');
        if (parts.Length != 3
            || BibleBookAbbreviation.GetAbbreviation(parts[0]) is not { } book
            || !int.TryParse(parts[1], out var chapter)
            || !int.TryParse(parts[2], out var verse))
        {
            return null;
        }

        return (book.Ordinal, chapter, verse);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static string Unique(string slug, HashSet<string> taken)
    {
        var candidate = slug;
        var suffix = 2;
        while (!taken.Add(candidate))
        {
            candidate = $"{slug}-{suffix++}";
        }

        return candidate;
    }
}
