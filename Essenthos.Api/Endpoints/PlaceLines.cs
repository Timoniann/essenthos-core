using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Where a place is, as its own line says it: by the land it lies in, or by a neighbour better known
/// than itself.
///
/// <para>
/// **A <c>near</c> clause locates the place its verse locates.** The pass reading Jeremiah 41:17,
/// <em>Geruth Chimham, which is near Bethlehem</em>, wrote <em>near</em> on both records, and
/// Bethlehem's line came to send a reader to a place named once. A line says <em>near</em> a place
/// only where that place is named in more verses than the one the line is about, and only where the
/// clause is not doubtful: Luke 10:30's road from Jerusalem down to Jericho puts neither beside the
/// other, and the pass said so with a confidence of one half. The clause stays among the place's
/// relationships either way.
/// </para>
///
/// <para>
/// **The land a place lies in comes first.** A clause putting the place in a land or a city, or one a
/// region states of it, is said instead of any neighbour. A region's <c>region-of</c> naming a town
/// reads the other way round where the town is the one it locates — <em>Bethlehem of Judaea</em>
/// (MAT 2:1) was written on Judaea as <em>a region of Bethlehem</em> — so it stands on the town's line
/// as <em>a city in</em> the region, and leaves the region's. Not where the region is named after the
/// town (<em>the wilderness of Maon</em> belongs to Maon), nor where it is named in under half as many
/// verses as the town, which is then a part of it: Moriah is a height in Jerusalem, not the land
/// Jerusalem lies in.
/// </para>
///
/// <para>
/// **A town the text names only in one tribe's inheritance is in that inheritance.** Joshua lists
/// the towns each tribe received and closes each list with <em>this is the inheritance of the children
/// of Zebulun</em> (JOS 19:16); a town named nowhere else has that and nothing else to place it by.
/// Its line says <em>a town in the inheritance of Zebulun</em>, from the list, with the verse that
/// names it.
/// </para>
/// </summary>
internal static class PlaceLines
{
    /// <summary>
    /// A town in a tribe's inheritance, as Joshua's lists of towns give it. No record writes this
    /// clause; the line works it out from where the town is named.
    /// </summary>
    public const string AllottedTo = "allotted-to";

    /// <summary>The relations that put a place inside another: a city in a land, a region of one.</summary>
    private static readonly IReadOnlySet<string> Containing = new HashSet<string>(StringComparer.Ordinal)
    {
        DescriptorRelations.CityIn, DescriptorRelations.RegionOf, DescriptorRelations.MountainIn, DescriptorRelations.RiverOf,
    };

    /// <summary>
    /// A region named in fewer than this share of a town's verses is a part of the town, not the land
    /// it lies in.
    /// </summary>
    private const double RegionShare = 0.5;

    private const string Settlement = "settlement";

    private const string Region = "region";

    private const int Joshua = 6;

    /// <summary>Whom a clause worked out from Joshua's lists is credited to.</summary>
    private const string ByTheLists = "Essenthos, from the towns Joshua lists in each tribe's inheritance";

    /// <summary>
    /// Each tribe's list of towns in Joshua, by the record of the son of Jacob it is named after. The
    /// borders are left out: a border runs past places that are not the tribe's.
    /// </summary>
    private static readonly (string Tribe, int Chapter, int From, int To)[] Inheritances =
    [
        ("reuben", 13, 15, 23),
        ("gad", 13, 24, 28),
        ("judah", 15, 21, 62),
        ("benjamin", 18, 21, 28),
        ("simeon", 19, 1, 9),
        ("zebulun", 19, 10, 16),
        ("issachar", 19, 17, 23),
        ("asher", 19, 24, 31),
        ("naphtali", 19, 32, 39),
        ("dan", 19, 40, 48),
    ];

    /// <summary>How each language says <see cref="AllottedTo"/>, and the case it puts the tribe in.</summary>
    public static IReadOnlyDictionary<string, Phrasing> Phrasings { get; } = new Dictionary<string, Phrasing>(StringComparer.Ordinal)
    {
        [DescriptorPhrasings.English] = new("a town in the inheritance of ", GrammaticalCases.Nominative),
        [DescriptorPhrasings.Ukrainian] = new("місто в наділі ", GrammaticalCases.Genitive),
        [DescriptorPhrasings.German] = new("eine Stadt im Erbteil ", GrammaticalCases.Genitive),
        [DescriptorPhrasings.Spanish] = new("una ciudad en la heredad de ", GrammaticalCases.Nominative),
    };

    /// <summary>
    /// The clauses with each place's line made to say where it is, as the rules above have it. Clauses
    /// of anything else are returned as they were.
    /// </summary>
    /// <param name="subjects">The places among the lines asked for.</param>
    public static async Task<List<Descriptors.Clause>> Located(
        AppDbContext db,
        List<Descriptors.Clause> clauses,
        IReadOnlyCollection<int> subjects,
        CancellationToken cancellationToken)
    {
        if (subjects.Count == 0)
        {
            return clauses;
        }

        var places = await db.Entities
            .Where(e => subjects.Contains(e.Id) && e.Kind == EntityKind.Place)
            .Select(e => new Place(e.Id, e.Slug, e.Name, e.PlaceKind))
            .ToListAsync(cancellationToken);
        if (places.Count == 0)
        {
            return clauses;
        }

        var ids = places.Select(p => p.Id).ToList();
        var stated = await db.EntityDescriptors
            .Where(d => ids.Contains(d.TargetEntityId) && d.Relation == DescriptorRelations.RegionOf
                        && d.Entity!.Kind == EntityKind.Place && d.EntityId != d.TargetEntityId)
            .Select(d => new
            {
                Region = new Place(d.EntityId, d.Entity!.Slug, d.Entity.Name, d.Entity.PlaceKind),
                d.Entity.Distinguisher,
                d.TargetEntityId,
                d.Ordinal,
                d.CanonicalBook,
                d.CanonicalChapter,
                d.CanonicalVerse,
                d.Method,
                d.Confidence,
                d.Source,
                d.Citation,
                d.Witness,
                d.Original,
            })
            .ToListAsync(cancellationToken);
        var regionsOfOurs = clauses
            .Where(c => ids.Contains(c.SubjectId) && c.Relation == DescriptorRelations.RegionOf)
            .Select(c => c.TargetEntityId)
            .ToList();

        var counted = ids
            .Concat(clauses.Where(c => ids.Contains(c.SubjectId)).Select(c => c.TargetEntityId))
            .Concat(stated.Select(s => s.Region.Id))
            .Distinct()
            .ToList();
        var verses = await db.EntityVerses.Shown()
            .Where(v => counted.Contains(v.EntityId))
            .Select(v => new { v.EntityId, v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse })
            .Distinct()
            .ToListAsync(cancellationToken);
        var named = verses.GroupBy(v => v.EntityId).ToDictionary(g => g.Key, g => g.Count());
        int Named(int id) => named.GetValueOrDefault(id);

        var towns = await db.Entities
            .Where(e => regionsOfOurs.Contains(e.Id))
            .Select(e => new Place(e.Id, e.Slug, e.Name, e.PlaceKind))
            .ToDictionaryAsync(e => e.Id, cancellationToken);
        var byId = places.ToDictionary(p => p.Id);

        bool Turns(Place region, Place town) =>
            Is(region, Region) && !Is(region, Settlement) && Is(town, Settlement)
            && !NameFolding.Fold(region.Name).Contains(NameFolding.Fold(town.Name), StringComparison.Ordinal)
            && Named(region.Id) >= Named(town.Id) * RegionShare;

        var turned = stated
            .Where(s => Turns(s.Region, byId[s.TargetEntityId]))
            .Select(s => new Descriptors.Clause(
                byId[s.TargetEntityId].Slug,
                s.Ordinal,
                DescriptorRelations.CityIn,
                s.Region.Id,
                EntityKind.Place,
                s.Region.Slug,
                s.Region.Name,
                s.Distinguisher,
                s.CanonicalBook,
                s.CanonicalChapter,
                s.CanonicalVerse,
                s.Method,
                s.Confidence,
                s.Source)
            {
                Citation = s.Citation,
                Witness = s.Witness,
                Original = s.Original,
                SubjectId = s.TargetEntityId,
                Stated = DescriptorRelations.RegionOf,
            })
            .ToList();

        var tribes = Inheritances.Select(i => i.Tribe).Distinct().ToList();
        var tribeRecords = await db.Entities
            .Where(e => tribes.Contains(e.Slug))
            .Select(e => new { e.Id, e.Slug, e.Name, e.Distinguisher, e.Sex })
            .ToDictionaryAsync(e => e.Slug, cancellationToken);

        var located = new List<Descriptors.Clause>(clauses.Count);
        foreach (var group in clauses.GroupBy(c => c.SubjectId))
        {
            if (!byId.TryGetValue(group.Key, out var place))
            {
                located.AddRange(group);
                continue;
            }

            located.AddRange(Line(place, [.. group]));
        }

        foreach (var place in places.Where(p => clauses.All(c => c.SubjectId != p.Id)))
        {
            located.AddRange(Line(place, []));
        }

        return located;

        IEnumerable<Descriptors.Clause> Line(Place place, List<Descriptors.Clause> own)
        {
            // A region-of this region states of a town it is the land of reads on the town's line.
            own.RemoveAll(c => c.Relation == DescriptorRelations.RegionOf
                               && towns.TryGetValue(c.TargetEntityId, out var town)
                               && Turns(place, town));

            var inside = own.Where(c => Containing.Contains(c.Relation) && c.TargetKind == EntityKind.Place)
                .Concat(turned.Where(c => c.SubjectId == place.Id))
                .ToList();
            var others = own.Where(c => !Containing.Contains(c.Relation) && c.Relation != DescriptorRelations.Near).ToList();
            var near = own.Where(c => c.Relation == DescriptorRelations.Near
                                      && !c.Doubtful
                                      && Named(c.TargetEntityId) > Named(place.Id))
                .ToList();
            var plain = inside.Where(c => !c.Doubtful).ToList();

            List<Descriptors.Clause> where = plain.Count > 0 ? plain
                : near.Count > 0 ? near
                : Is(place, Settlement) && Allotted(place) is { } allotted ? [allotted]
                : inside;
            return [.. where, .. others];
        }

        Descriptors.Clause? Allotted(Place place)
        {
            var where = verses.Where(v => v.EntityId == place.Id).ToList();
            if (where.Count == 0)
            {
                return null;
            }

            var lists = where
                .Select(v => Inheritances.FirstOrDefault(i => v.CanonicalBook == Joshua && v.CanonicalChapter == i.Chapter
                                                              && v.CanonicalVerse >= i.From && v.CanonicalVerse <= i.To).Tribe)
                .Distinct()
                .ToList();
            if (lists.Count != 1 || lists[0] is not { } tribe || !tribeRecords.TryGetValue(tribe, out var record))
            {
                return null;
            }

            var first = where.OrderBy(v => v.CanonicalChapter).ThenBy(v => v.CanonicalVerse).First();
            return new Descriptors.Clause(
                place.Slug,
                0,
                AllottedTo,
                record.Id,
                EntityKind.Person,
                record.Slug,
                record.Name,
                record.Distinguisher,
                first.CanonicalBook,
                first.CanonicalChapter,
                first.CanonicalVerse,
                LinkMethod.RuleBased,
                1.0,
                ByTheLists)
            {
                SubjectId = place.Id,
                TargetSex = record.Sex,
            };
        }
    }

    private static bool Is(Place place, string kind) =>
        place.PlaceKind is { } kinds && kinds.Contains(kind, StringComparison.OrdinalIgnoreCase);

    private sealed record Place(int Id, string Slug, string Name, string? PlaceKind);
}
