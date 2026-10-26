using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

internal static partial class EncyclopediaEndpoints
{
    /// <summary>The verses of each place a page lists, unless asked for more or fewer.</summary>
    private const int SharedVersesShown = 20;

    private const int MostSharedVersesShown = 200;

    /// <summary>The roles a place can have in someone's story, in the order a page lists them.</summary>
    internal static class StoryRoles
    {
        public const string Born = "born";
        public const string Lived = "lived";
        public const string Event = "event";
        public const string Died = "died";
        public const string Buried = "buried";
        public const string NamedTogether = "named-together";

        public static readonly string[] Order = [Born, Lived, Event, Died, Buried, NamedTogether];
    }

    /// <summary>
    /// Which part of the list a place belongs to: <see cref="Story"/>, the places the story happens
    /// at, or <see cref="OtherBooks"/>, places only named alongside the record outside the books that
    /// tell a story.
    /// </summary>
    internal static class StoryGroups
    {
        public const string Story = "story";
        public const string OtherBooks = "other-books";
    }

    private const int Genesis = 1;
    private const int Esther = 17;
    private const int Matthew = 40;
    private const int Acts = 44;

    private const int Romans = 45;
    private const int Philemon = 57;

    /// <summary>
    /// The books named after the person they are for, outside those that tell a story already: a
    /// prophet's own book, and the letters that bear their writer's name, Paul's from Romans to
    /// Philemon. Hebrews is not Paul's by name, Lamentations is not Jeremiah's, and Revelation is not
    /// named after John. Keyed by the record's address, which is stable across rebuilds.
    /// </summary>
    private static readonly Dictionary<string, int[]> OwnBooks = new(StringComparer.Ordinal)
    {
        ["job"] = [18],
        ["isaiah"] = [23],
        ["jeremiah-6"] = [24],
        ["ezekiel"] = [26],
        ["daniel-2"] = [27],
        ["hosea"] = [28],
        ["joel-11"] = [29],
        ["amos"] = [30],
        ["obadiah-12"] = [31],
        ["jonah"] = [32],
        ["micah-4"] = [33],
        ["nahum"] = [34],
        ["habakkuk"] = [35],
        ["zephaniah-3"] = [36],
        ["haggai"] = [37],
        ["zechariah-13"] = [38],
        ["malachi"] = [39],
        ["saul-2"] = [.. Enumerable.Range(Romans, Philemon - Romans + 1)],
        ["james-3"] = [59],
        ["simon"] = [60, 61],
        ["john-2"] = [62, 63, 64],
        ["judas-2"] = [65],
    };

    /// <summary>
    /// Genesis to Esther, the four Gospels and Acts: the books that tell what happened where; and,
    /// for the person a book is named after, that book too. A place named with someone in a psalm,
    /// in another's prophecy or in a letter's greeting is not where their story happens.
    /// </summary>
    internal static bool TellsAStory(int canonicalBook, string? slug = null) =>
        canonicalBook is >= Genesis and <= Esther or >= Matthew and <= Acts
        || (slug is not null && OwnBooks.TryGetValue(slug, out var own) && own.Contains(canonicalBook));

    /// <summary>
    /// The ties to a place that say what it was in someone's life. <c>from-place</c> is where the
    /// text says they came from, which is where they lived.
    /// </summary>
    private static readonly Dictionary<string, string> TiedRoles = new(StringComparer.Ordinal)
    {
        [DescriptorRelations.LivedIn] = StoryRoles.Lived,
        [DescriptorRelations.FromPlace] = StoryRoles.Lived,
        [DescriptorRelations.BuriedIn] = StoryRoles.Buried,
    };

    /// <summary>The places of one record's story, with the verses it shares with each.</summary>
    private static void MapStoryPlaces(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/entities/{slug}/places", async (
            string slug,
            [FromQuery] string? language,
            [FromQuery] string? prose,
            [FromQuery] int? verses,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            slug = await MergedAddresses.Current(db, slug, cancellationToken);
            var entity = await db.Entities.Where(e => e.Slug == slug).Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (entity == 0)
            {
                return ApiResults.NotFound($"There is no person, place or people \"{slug}\".");
            }

            return Results.Ok(await StoryPlaces(
                db, entity, language, prose, verses ?? SharedVersesShown, cancellationToken));
        });
    }

    /// <summary>
    /// A person's admitted bodily visits and burials, with the passages supporting each finding.
    /// Records of other kinds retain their list of places named alongside them.
    /// </summary>
    internal static async Task<EntityPlacesResponse> StoryPlaces(
        AppDbContext db,
        int entityId,
        string? language,
        string? prose,
        int verses,
        CancellationToken cancellationToken = default)
    {
        var kind = await db.Entities.Where(e => e.Id == entityId).Select(e => e.Kind).FirstOrDefaultAsync(cancellationToken);
        return kind == EntityKind.Person
            ? await PresencePlaces(db, entityId, language, prose, verses, cancellationToken)
            : await NamedPlaces(db, entityId, language, prose, verses, cancellationToken);
    }

    internal static async Task<EntityPlacesResponse> NamedPlaces(
        AppDbContext db,
        int entityId,
        string? language,
        string? prose,
        int verses,
        CancellationToken cancellationToken = default)
    {
        var record = await db.Entities.Where(e => e.Id == entityId).Select(e => e.Slug)
            .FirstOrDefaultAsync(cancellationToken);
        var shared = await SharedAddresses(db, entityId).ToListAsync(cancellationToken);
        var addresses = shared
            .GroupBy(row => row.EntityId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Address).Order().ToList());

        var tiedTypes = TiedRoles.Keys.ToList();
        var ties = await db.EntityRelationships
            .Where(r => r.FromEntityId == entityId && r.To!.Kind == EntityKind.Place && tiedTypes.Contains(r.Type))
            .Select(r => new { r.ToEntityId, r.Type })
            .ToListAsync(cancellationToken);

        var events = await db.Events
            .Where(e => e.EntityId == entityId && e.Location != null)
            .Select(e => new { e.Kind, e.Location, e.LocationBook, e.LocationChapter, e.LocationVerse })
            .ToListAsync(cancellationToken);
        var located = events
            .Select(e => (e.Kind, At: EventLocation.Of(e.Location, e.LocationBook, e.LocationChapter, e.LocationVerse)))
            .ToList();
        var named = await PlacesNamed(db, located.Select(e => e.At), cancellationToken);
        var eventSlugs = located
            .Select(e => e.At is { } at ? named.GetValueOrDefault(at) : null)
            .OfType<string>()
            .Distinct()
            .ToList();

        var tiedIds = ties.Select(t => t.ToEntityId).ToList();
        var sharedIds = addresses.Keys.ToList();
        var places = await db.Entities
            .Where(e => e.Kind == EntityKind.Place
                        && (sharedIds.Contains(e.Id) || tiedIds.Contains(e.Id) || eventSlugs.Contains(e.Slug)))
            .Select(e => new
            {
                e.Id,
                e.Slug,
                e.Name,
                e.PlaceKind,
                Location = e.Location == null
                    ? null
                    : new { e.Location.Longitude, e.Location.Latitude, e.Location.Kind, e.Location.Score },
            })
            .ToListAsync(cancellationToken);

        var roles = places.ToDictionary(p => p.Id, _ => new HashSet<string>(StringComparer.Ordinal));
        foreach (var tie in ties)
        {
            roles.GetValueOrDefault(tie.ToEntityId)?.Add(TiedRoles[tie.Type]);
        }

        var bySlug = places.ToDictionary(p => p.Slug, p => p.Id, StringComparer.Ordinal);
        foreach (var (kind, at) in located)
        {
            if (at is { } location && named.GetValueOrDefault(location) is { } slug
                                   && bySlug.TryGetValue(slug, out var id))
            {
                roles[id].Add(kind switch
                {
                    "Birth" => StoryRoles.Born,
                    "Death" => StoryRoles.Died,
                    _ => StoryRoles.Event,
                });
            }
        }

        foreach (var (id, held) in roles)
        {
            if (held.Count == 0 && addresses.ContainsKey(id))
            {
                held.Add(StoryRoles.NamedTogether);
            }
        }

        var ids = places.Select(p => p.Id).ToList();
        var local = await EntityNames.Of(db, ids, language, cancellationToken);
        var bySource = await EntityNames.NamedOnlyBySource(db, ids, language, local, cancellationToken);
        var told = await Distinguished(
            db,
            [.. places.Select(p => (p.Slug, Shown: local.GetValueOrDefault(p.Id) ?? p.Name))],
            ReaderLanguages.Prose(prose, language),
            language,
            cancellationToken);

        var take = Math.Clamp(verses, 0, MostSharedVersesShown);
        return new EntityPlacesResponse(
            places.Count,
            [
                .. places
                    .Select(p =>
                    {
                        var shared = addresses.GetValueOrDefault(p.Id) ?? [];
                        var told = shared.Count(address => TellsAStory(address / BookStride, record));
                        var story = told > 0 || !roles[p.Id].SetEquals([StoryRoles.NamedTogether]);
                        return (Place: p, Addresses: shared, Told: told, Story: story);
                    })
                    .OrderByDescending(p => p.Story)
                    .ThenByDescending(p => p.Told)
                    .ThenByDescending(p => p.Addresses.Count)
                    .ThenBy(p => local.GetValueOrDefault(p.Place.Id) ?? p.Place.Name, StringComparer.CurrentCulture)
                    .ThenBy(p => p.Place.Slug, StringComparer.Ordinal)
                    .Select(p => new EntityPlaceResponse(
                        p.Place.Slug,
                        p.Place.Name,
                        p.Story ? StoryGroups.Story : StoryGroups.OtherBooks,
                        p.Addresses.Count,
                        p.Told,
                        [.. p.Addresses.Take(take).Select(VerseAt)],
                        [.. StoryRoles.Order.Where(roles[p.Place.Id].Contains)])
                    {
                        LocalName = local.GetValueOrDefault(p.Place.Id),
                        NameFromSource = bySource.Contains(p.Place.Id),
                        Distinguisher = told.GetValueOrDefault(p.Place.Slug),
                        PlaceKind = p.Place.PlaceKind,
                        Lon = p.Place.Location?.Longitude,
                        Lat = p.Place.Location?.Latitude,
                        Kind = p.Place.Location?.Kind,
                        Confidence = p.Place.Location is { } at ? at.Score / ScoreScale : null,
                    }),
            ]);
    }

    /// <summary>
    /// Each place named in a verse the record's list shows, with that verse's address: a semi-join of
    /// the place layer on the record's own verses, both read as the page reads them.
    /// </summary>
    internal static IQueryable<SharedAddress> SharedAddresses(AppDbContext db, int entityId)
    {
        var mine = db.EntityVerses.Shown().Where(v => v.EntityId == entityId);
        return db.EntityVerses.Shown()
            .Where(v => v.Entity!.Kind == EntityKind.Place && v.EntityId != entityId
                        && mine.Any(m => m.CanonicalBook == v.CanonicalBook
                                         && m.CanonicalChapter == v.CanonicalChapter
                                         && m.CanonicalVerse == v.CanonicalVerse))
            .Select(v => new SharedAddress(
                v.EntityId,
                (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride) + v.CanonicalVerse))
            .Distinct();
    }

    internal sealed record SharedAddress(int EntityId, int Address);

    private static async Task<EntityPlacesResponse> PresencePlaces(
        AppDbContext db, int person, string? language, string? prose, int verses, CancellationToken cancellationToken)
    {
        var held = await db.PersonPresences.AsNoTracking().Where(p => p.PersonId == person)
            .Select(p => new { p.PlaceId, p.Category, p.Addresses }).ToListAsync(cancellationToken);
        var ids = held.Select(p => p.PlaceId).Distinct().ToList();
        var places = await db.Entities.Where(e => ids.Contains(e.Id) && e.Kind == EntityKind.Place)
            .Select(e => new
            {
                e.Id, e.Slug, e.Name, e.PlaceKind,
                Location = e.Location == null ? null : new { e.Location.Longitude, e.Location.Latitude, e.Location.Kind, e.Location.Score },
            }).ToListAsync(cancellationToken);
        var local = await EntityNames.Of(db, ids, language, cancellationToken);
        var bySource = await EntityNames.NamedOnlyBySource(db, ids, language, local, cancellationToken);
        var told = await Distinguished(db,
            [.. places.Select(p => (p.Slug, Shown: local.GetValueOrDefault(p.Id) ?? p.Name))],
            ReaderLanguages.Prose(prose, language), language, cancellationToken);
        var take = Math.Clamp(verses, 0, MostSharedVersesShown);
        var items = places.Select(place =>
        {
            var findings = held.Where(p => p.PlaceId == place.Id).ToList();
            var addresses = findings.SelectMany(p => p.Addresses).Distinct().Order().ToList();
            var roles = new List<string>();
            if (findings.Any(p => p.Category == "present")) roles.Add(StoryRoles.Event);
            if (findings.Any(p => p.Category == "buried")) roles.Add(StoryRoles.Buried);
            return new EntityPlaceResponse(place.Slug, place.Name, StoryGroups.Story,
                addresses.Count, addresses.Count, [.. addresses.Take(take).Select(VerseAt)], roles)
            {
                LocalName = local.GetValueOrDefault(place.Id), NameFromSource = bySource.Contains(place.Id),
                Distinguisher = told.GetValueOrDefault(place.Slug), PlaceKind = place.PlaceKind,
                Lon = place.Location?.Longitude, Lat = place.Location?.Latitude, Kind = place.Location?.Kind,
                Confidence = place.Location is { } at ? at.Score / ScoreScale : null,
            };
        }).OrderByDescending(p => p.SharedVerses)
            .ThenBy(p => p.LocalName ?? p.Name, StringComparer.CurrentCulture).ThenBy(p => p.Slug, StringComparer.Ordinal).ToList();
        return new EntityPlacesResponse(items.Count, items);
    }

    private static VerseRefResponse VerseAt(int address) =>
        BookReferences.At(address / BookStride, address % BookStride / ChapterStride, address % ChapterStride)!;
}
