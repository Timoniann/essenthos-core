using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Products;

namespace Essenthos.Core.Records;

/// <summary>
/// The derived record files made from the hand files instead of the database (DOC-0226 §3.4): each
/// producer reads its inputs through the product store and writes its file in the order the export
/// writes it, so the two can be compared byte for byte.
/// </summary>
internal static class RecordProducers
{
    public const string Producer = "forge records-derive";

    /// <summary>
    /// Writes every derived file whose inputs are files, from <paramref name="from"/> into
    /// <paramref name="into"/> (the same store when they are one). <paramref name="openBible"/> is the
    /// gazetteer's folder.
    /// </summary>
    public static List<(string Product, long Rows)> Derive(ProductStore from, ProductStore into, string openBible) =>
    [
        Relationships(from, into),
        .. Locations(from, into, openBible),
    ];

    /// <summary>
    /// The relationships a page draws: every clause settled pair by pair
    /// (<see cref="OwnRelationshipLoader.Settle"/>), less the clauses held back by name, and the
    /// relations the things' files state.
    /// </summary>
    public static (string Product, long Rows) Relationships(ProductStore from, ProductStore into)
    {
        var file = File("records/facts/relationships");
        var slugs = Slugs(from);
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var bySlug = new List<string>();
        int Id(string slug)
        {
            if (!ids.TryGetValue(slug, out var id))
            {
                ids[slug] = id = bySlug.Count;
                bySlug.Add(slug);
            }

            return id;
        }

        var held = Read(from, "records/facts/withheld-clauses").Select(r => (r[0]!, r[1]!, r[2]!)).ToHashSet();
        var clauses = new List<OwnRelationshipLoader.Clause>();
        var readHeld = 0;
        // Two clauses saying one relation of a pair equally surely are written once, and the pick keeps
        // the first it meets: the first by who read it, so the row does not depend on the file's order.
        foreach (var r in Read(from, "records/facts/clauses").OrderBy(r => r[10], StringComparer.Ordinal))
        {
            if (held.Contains((r[0]!, r[2]!, r[3]!)))
            {
                readHeld++;
                continue;
            }

            var verse = VerseKey.Parse(r[4]);
            clauses.Add(new OwnRelationshipLoader.Clause(
                Id(r[0]!), Id(r[3]!), r[2]!, verse.Book, verse.Chapter, verse.Verse,
                EnumSpelling.ToLinkMethod(r[8]!), Confidence(r[9]), r[10]!, r[12])
            {
                Citation = r[5],
            });
        }

        var (settled, _, _) = OwnRelationshipLoader.Settle(clauses);
        var rows = settled.Select(row => Row(bySlug[row.FromEntityId], bySlug[row.ToEntityId], row)).ToList();
        foreach (var set in new[] { ThingSet.MadeAndKept, ThingSet.Narratives })
        {
            foreach (var record in ThingFiles.Read(set).Records.Where(r => slugs.Contains(r.Slug)))
            {
                rows.AddRange(ThingLoader.Relations(set, record, slugs.Contains)
                    .Where(r => r.Row is not null).Select(r => Row(record.Slug, r.Relation.To, r.Row!)));
            }
        }

        var written = Write(into, file, rows, Producer + " relationships",
            from.Inputs(Paths(from, "records/records", "records/facts/clauses", "records/facts/withheld-clauses")),
            header =>
            {
                header.Notes.Add(new("things", Embedded(ThingSet.MadeAndKept.Resources.Concat(ThingSet.Narratives.Resources))));
                header.Notes.Add(new("held", readHeld.ToString(CultureInfo.InvariantCulture)));
            });
        return (file.Product, written);
    }

    /// <summary>
    /// Where each place is and the sites proposed for it, from the gazetteer's files, the location
    /// supplements and the records' gazetteer ids (<see cref="OpenBibleLocationLoader.Locate"/>).
    /// </summary>
    public static IEnumerable<(string Product, long Rows)> Locations(ProductStore from, ProductStore into, string openBible)
    {
        var ancient = Path.Combine(openBible, "ancient.jsonl");
        var modern = Path.Combine(openBible, "modern.jsonl");
        var supplement = Supplement(from);
        var read = supplement.Apply(OpenBibleLocationLoader.Read(ancient, modern));
        var regions = supplement.Regions.Select(r => r.Slug).ToHashSet(StringComparer.Ordinal);
        var laterTowns = SharedSites.All.Select(s => s.Later).ToHashSet(StringComparer.Ordinal);
        var openBibleIds = Read(from, "records/external-ids")
            .Where(r => r[1] == "openbible" && r[3] == "same-as")
            .ToDictionary(r => r[0]!, r => r[2]!, StringComparer.Ordinal);

        var places = new List<OpenBibleLocationLoader.Place>();
        foreach (var r in Read(from, "records/records").Where(r => r[1] == EnumSpelling.Of(EntityKind.Place)))
        {
            var openBibleId = openBibleIds.GetValueOrDefault(r[0]!);
            if (openBibleId is not null || regions.Contains(r[0]!) || laterTowns.Contains(r[0]!))
            {
                places.Add(new OpenBibleLocationLoader.Place(places.Count, r[0]!, openBibleId));
            }
        }

        var (locations, candidates, _) = OpenBibleLocationLoader.Locate(read, supplement, places);
        var inputs = from.Inputs([.. Paths(from, "records/records", "records/external-ids", "records/facts/location-supplements"),
            RelativeToResources(from, ancient), RelativeToResources(from, modern)]);
        void Shared(ProductHeader header) => header.Notes.Add(new("shared-sites", string.Join(" ", SharedSites.All.Select(s => $"{s.Earlier}>{s.Later}"))));

        var locationFile = File("records/facts/locations");
        var located = Write(into, locationFile,
            locations.Select(l => new string?[]
            {
                places[l.EntityId].Slug, Number(l.Longitude), Number(l.Latitude), l.Kind, Integer(l.Score), l.ModernId,
                l.CoordinatesSource, l.CoordinatesDataset, l.Source,
            }),
            Producer + " locations", inputs, Shared);
        var candidateFile = File("records/facts/location-candidates");
        var proposed = Write(into, candidateFile,
            candidates.Select(c => new string?[]
            {
                places[c.EntityId].Slug, Integer(c.Rank), Integer(c.Score), c.ModernId, c.Site, Number(c.Longitude), Number(c.Latitude),
                c.Kind, Integer(c.RadiusMeters), c.SiteType, c.Relation, c.ThroughOpenBibleId, c.Special, c.Withheld,
                string.Join('|', c.Grounds), c.CoordinatesSource, c.CoordinatesDataset, c.Source,
            }),
            Producer + " locations", inputs, Shared);
        return [(locationFile.Product, located), (candidateFile.Product, proposed)];
    }

    /// <summary>The location supplements as the hand file states them.</summary>
    private static LocationSupplement Supplement(ProductStore from)
    {
        var rows = Read(from, "records/facts/location-supplements");
        return LocationSupplement.Declared(new LocationSupplement(
            [.. rows.Where(r => r[5] is null).Select(r => new IndependentPoint(r[0]!, r[2]!, Double(r[3]!), Double(r[4]!), r[1]!, r[6]!))],
            [.. rows.Where(r => r[5] is not null).Select(r => new IndependentRegion(r[5]!, r[2]!, r[0]!, Double(r[3]!), Double(r[4]!), r[1]!, r[6]!))]));
    }

    private static string?[] Row(string from, string to, EntityRelationship row) =>
    [
        from, row.Type, to,
        row.CanonicalBook is { } book ? new VerseKey(book, row.CanonicalChapter!.Value, row.CanonicalVerse!.Value).ToString() : null,
        row.Category, row.Citation, EnumSpelling.Of(row.Method), Number(row.Confidence), row.Source, row.Notes,
    ];

    private static RecordFile File(string product) => RecordFiles.All.Single(f => f.Product == product);

    private static HashSet<string> Slugs(ProductStore from) =>
        Read(from, "records/records").Select(r => r[0]!).ToHashSet(StringComparer.Ordinal);

    private static List<string?[]> Read(ProductStore from, string product)
    {
        using var reader = from.Open(product, File(product).Schema());
        return [.. reader.ReadAll()];
    }

    private static long Write(
        ProductStore into, RecordFile file, IEnumerable<string?[]> rows, string producer, IReadOnlyList<ProductInput> inputs,
        Action<ProductHeader>? annotate = null)
    {
        using var writer = into.Create(file.Product, file.Kind, file.Schema(), ProductHeader.DescribeProducer(producer), inputs,
            header =>
            {
                annotate?.Invoke(header);
                if (file.About is { } about)
                {
                    header.Comments.Add(about);
                }
            });
        writer.WriteAll(file.Sorted(rows, file.Order(file.Export)));
        writer.Complete();
        return writer.Rows;
    }

    private static string[] Paths(ProductStore store, params string[] products) =>
        [.. products.Select(p => store.RelativePath(p, File(p).Kind))];

    private static string RelativeToResources(ProductStore store, string path) =>
        Path.GetRelativePath(store.ResourcesPath, path).Replace('\\', '/');

    /// <summary>The SHA-256 of the Forge's embedded files a producer reads, as one note.</summary>
    private static string Embedded(IEnumerable<string> resources) =>
        string.Join(" ", resources.Select(resource =>
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                               ?? throw new InvalidOperationException($"{resource} is not embedded in the Forge assembly.");
            return $"{resource["Essenthos.Core.Loading.Encyclopedia.".Length..]}@{Convert.ToHexStringLower(SHA256.HashData(stream))}";
        }));

    private static double? Confidence(string? text) => text is null ? null : Double(text);

    private static double Double(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    /// <summary>A number as Postgres writes a double precision: the shortest text that reads back the same.</summary>
    private static string? Number(double? value) => value?.ToString("R", CultureInfo.InvariantCulture);

    private static string? Integer(int? value) => value?.ToString(CultureInfo.InvariantCulture);
}
