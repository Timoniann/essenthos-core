using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Products;
using Microsoft.Extensions.Logging.Abstractions;

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
        .. Images(from, into),
        Wikipedia(from, into),
    ];

    /// <summary>
    /// The pictures of the records and their captions (<see cref="EntityImageLoader.Pictures"/>): the Forge's
    /// curated list, our own generated pictures as their manifest and the owner's word on each stand, the pictures
    /// drawn from the models, the gazetteer's thumbnails, and the owner's choices in <c>image-choices.json</c>, every
    /// file read for its size and digest under <c>Images/</c>. These are the files the console writes, so a choice
    /// or an approval made there reaches the site through this file.
    /// </summary>
    public static IEnumerable<(string Product, long Rows)> Images(ProductStore from, ProductStore into)
    {
        var resources = from.ResourcesPath;
        if (!Directory.Exists(Path.Combine(resources, EntityImageLoader.Folder)))
        {
            throw new DirectoryNotFoundException(
                $"No {EntityImageLoader.Folder}/ under {resources}: without the picture files every picture would be left out. " +
                "Junction it from the main checkout, or run scripts/fetch-images.ps1.");
        }

        var files = RecordFiles.All.Where(f => f.Product == ImagesProduct || IsCaptionFile(f.Product)).ToList();
        var inputs = from.Inputs([
            .. Paths(from, "records/records", "records/external-ids", "records/folds"),
            EntityImageLoader.Folder,
            .. new[]
            {
                $"{ImageChoices.Folder}/{ImageChoices.FileName}",
                $"OpenBible/{OpenBibleThumbnails.AncientFile}",
                $"OpenBible/{OpenBibleThumbnails.ImageFile}",
            }.Where(path => System.IO.File.Exists(Path.Combine(resources, path))),
        ]);
        var lists = Embedded([EntityImageLoader.CuratedResource, PortraitFaces.Resource]);

        var external = Read(from, "records/external-ids").Where(r => r[3] == "same-as").ToList();
        var bibleData = First(external.Where(r => r[1] == "bibledata"));
        var openBible = First(external.Where(r => r[1] == "openbible"));
        var records = Read(from, "records/records").Select((r, i) => new PicturedRecord(
            i, r[0]!, EnumSpelling.ToEntityKind(r[1]!),
            bibleData.GetValueOrDefault(r[0]!) ?? (r[0] == DivineRecords.HolySpiritSlug ? DivineRecords.HolySpiritSourceId : string.Empty),
            openBible.GetValueOrDefault(r[0]!))).ToList();
        var ids = records.ToDictionary(r => r.Slug, r => r.Id, StringComparer.Ordinal);
        var folded = Read(from, "records/folds").Where(r => ids.ContainsKey(r[1]!)).Select(r => (r[0]!, ids[r[1]!]));
        var (rows, outcome) = EntityImageLoader.Pictures(resources, records, folded, NullLogger.Instance);

        void Notes(ProductHeader header)
        {
            header.Notes.Add(new("lists", lists));
            header.Notes.Add(new("pictured",
                $"{outcome.Places} from the gazetteer, {outcome.Curated} curated, {outcome.Generated} ours, {outcome.Rendered} drawn from models; " +
                $"left out: {outcome.Unlicensed} unlicensed, {outcome.Uncredited} uncredited, {outcome.Missing} with no file, " +
                $"{outcome.Unheld} of no record, {outcome.Refused} refused, {outcome.Withheld} not approved, {outcome.Hidden} hidden by the owner, " +
                $"{outcome.Unthings} drawn for what is not a thing"));
        }

        string Slug(EntityImage image) => records[image.EntityId].Slug;
        var written = new List<(string, long)>();
        foreach (var file in files)
        {
            IEnumerable<string?[]> lines;
            if (file.Product == ImagesProduct)
            {
                lines = rows.Select(i => new string?[]
                {
                    Slug(i), i.Kind, i.Role, Integer(i.Ordinal), i.File, i.Digest, Integer(i.Width), Integer(i.Height), i.Credit, i.CreditUrl,
                    i.Licence, i.LicenceUrl, i.Source, Number(i.FocusX), Number(i.FocusY), Number(i.BustX), Number(i.BustY),
                    Number(i.BustWidth), Number(i.BustHeight),
                });
            }
            else
            {
                var language = file.Product.Split('/')[2];
                lines = rows.SelectMany(i => (language == "eng"
                        ? i.Caption is { } caption ? [caption] : Array.Empty<string>()
                        : i.Captions.Where(c => c.Language == language).Select(c => c.Caption))
                    .Select(caption => new string?[] { Slug(i), i.Kind, i.Role, Integer(i.Ordinal), caption }));
            }

            written.Add((file.Product, Write(into, file, lines, Producer + " images", inputs, Notes)));
        }

        return written;
    }

    /// <summary>
    /// Each record's Wikipedia articles: the item the owner chose for it in his console, else the item the matching
    /// tied it to on its own, both as <c>review/wikipedia-matches.json</c> holds them
    /// (<see cref="WikipediaReviewList.Links"/>), and that item's articles as the Wikidata items give them, linked as
    /// the load links them (<see cref="WikipediaLinkLoader.Links"/>).
    /// </summary>
    public static (string Product, long Rows) Wikipedia(ProductStore from, ProductStore into)
    {
        var resources = from.ResourcesPath;
        var list = ReviewLists.Owners(resources, WikipediaReviewList.FileName);
        if (!System.IO.File.Exists(list))
        {
            throw new FileNotFoundException(
                $"The owner's Wikipedia list is not at {list}: it holds his answers and the matching's ties, without which " +
                "every record would lose its articles. Restore it from git.", list);
        }

        var file = File(WikipediaProduct);
        var inputs = from.Inputs([
            .. Paths(from, "records/records"),
            RelativeToResources(from, list),
            .. new[] { WikidataItems.ItemsFile, WikidataItems.ReferencedFile }
                .Select(name => $"{WikidataItems.Folder}/{name}")
                .Where(path => System.IO.File.Exists(Path.Combine(resources, path))),
        ]);
        var wikidata = WikidataItems.Read(resources);
        var slugs = Slugs(from);
        var rows = new List<string?[]>();
        int owner = 0, tied = 0, gone = 0, unknown = 0;
        foreach (var (slug, (qid, by)) in WikipediaReviewList.Links(list))
        {
            if (!slugs.Contains(slug))
            {
                gone++;
                continue;
            }

            if (!wikidata.TryGet(qid, out var item))
            {
                unknown++;
                continue;
            }

            if (!item.HasArticle)
            {
                continue;
            }

            if (by == EntityWikipedia.Evidence.Owner)
            {
                owner++;
            }
            else
            {
                tied++;
            }

            rows.AddRange(WikipediaLinkLoader.Links(item, by)
                .Select(link => new string?[] { slug, link.Language, link.Title, link.Qid, link.MatchedBy, Number(link.Confidence) }));
        }

        var written = Write(into, file, rows, Producer + " wikipedia", inputs, header => header.Notes.Add(new("linked",
            $"{owner} records by the owner's answer, {tied} by the matching; {gone} listed records no longer held, " +
            $"{unknown} items the Wikidata items do not hold")));
        return (file.Product, written);
    }

    private const string ImagesProduct = "records/facts/images";

    private const string WikipediaProduct = "records/facts/wikipedia";

    private static bool IsCaptionFile(string product) =>
        product.StartsWith("records/i18n/", StringComparison.Ordinal) && product.EndsWith("/images", StringComparison.Ordinal);

    /// <summary>A dataset's id of each record, the first where a file states two.</summary>
    private static Dictionary<string, string> First(IEnumerable<string?[]> rows)
    {
        var first = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            first.TryAdd(r[0]!, r[2]!);
        }

        return first;
    }

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

    /// <summary>A number as Postgres writes a double precision: the shortest text that reads back the same, its exponent a small e.</summary>
    private static string? Number(double? value) => value?.ToString("R", CultureInfo.InvariantCulture).Replace('E', 'e');

    private static string? Integer(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);
}
