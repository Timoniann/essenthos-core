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

    /// <summary>
    /// Genesis to Esther, the four Gospels and Acts: the books that tell what happened where. A
    /// place named with someone in a psalm, a prophecy or a letter's greeting is not where their
    /// story happens.
    /// </summary>
    internal static bool TellsAStory(int canonicalBook) =>
        canonicalBook is >= Genesis and <= Esther or >= Matthew and <= Acts;

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
    /// Every place named in a verse that also names this record, and every place its dated events
    /// happen at or its ties put it in, whether or not a verse names the two together. Each with
    /// how many verses name both, the first of them, what the place was in the story, and whether
    /// it is a place of the story at all: one a role gives or a verse of a book that tells a story
    /// names, before those only named together elsewhere.
    ///
    /// The verses are the ones the record's own list of verses shows, so a count here is a count
    /// of verses a reader can find there. The places named alongside come from one query, a
    /// semi-join of the place layer on the record's verses; the ties and events are two small reads.
    /// </summary>
    internal static async Task<EntityPlacesResponse> StoryPlaces(
        AppDbContext db,
        int entityId,
        string? language,
        string? prose,
        int verses,
        CancellationToken cancellationToken = default)
    {
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
                        var told = shared.Count(address => TellsAStory(address / BookStride));
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

    private static VerseRefResponse VerseAt(int address) =>
        BookReferences.At(address / BookStride, address % BookStride / ChapterStride, address % ChapterStride)!;
}
