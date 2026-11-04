using Essenthos.Core.Corpus;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Names;
using Essenthos.Core.Products;

namespace Essenthos.Core.Records;

/// <summary>
/// One canon that attests a record: the words of the held texts that name it in a verse of that canon (DOC-0224 §7.2, §8.1;
/// the owner's D4), <c>records/canon</c>. A record named in no word has no row and is hidden by no canon mode.
/// </summary>
/// <param name="Canon">See <see cref="RecordCanons"/>.</param>
/// <param name="Words">The words of every held text that name the record in a verse of the canon.</param>
/// <param name="Texts">The texts those words belong to.</param>
/// <param name="First">The first of those words by text and key (<c>BHSA 1.2:19.12</c>), for a reader to check.</param>
internal sealed record RecordCanonRow(string Slug, string Canon, long Words, long Texts, string First)
{
    public static readonly ProductSchema<RecordCanonRow> Schema = new ProductSchema<RecordCanonRow>()
        .Text("slug", r => r.Slug)
        .Text("canon", r => r.Canon)
        .Integer("words", r => r.Words)
        .Integer("texts", r => r.Texts)
        .Text("first", r => r.First)
        .Reads(row => new RecordCanonRow(row.Text(0), row.Text(1), row.Integer(2), row.Integer(3), row.Text(4)));
}

/// <summary>
/// Which canons attest each record, derived from the names the projection puts on the words of every held text
/// (<c>names/projected</c>) and from where each word's verse stands (<c>texts/*/frame</c>):
/// <list type="bullet">
/// <item><c>hebrew</c>: a verse of Genesis to Malachi in a chapter the Hebrew Bible holds (BHSA's verses, at their frame
/// addresses), and Messale and Tägsas, the two books the Ethiopian Bible makes of Proverbs;</item>
/// <item><c>greek-nt</c>: a verse of Matthew to Revelation;</item>
/// <item><c>deuterocanon</c>: a book a Greek, Latin or Slavonic canon receives beyond those (Tobit to the Psalms of Solomon),
/// a chapter of a Hebrew book the Hebrew does not hold (Brenton's Esther 11-16), or a verse an edition sets apart by a
/// letter at an address it shares with the Hebrew and its frame does not place elsewhere (Esther 1:1a-s, 3 Kingdoms
/// 16:28a-h);</item>
/// <item><c>ethiopic</c>: a book only the Ethiopian canon receives (Enoch, Jubilees, Meqabyan, 4 Baruch).</item>
/// </list>
/// An edition's own verse numbers inside a Hebrew chapter (the Ge'ez Esther, a range like 3:9-10) stay the Hebrew's.
/// A base reading beside a differs is the base text's name, not the word's, and attests nothing.
/// </summary>
internal static class RecordCanons
{
    public const string Product = "records/canon";

    public const string Producer = "forge records-canon";

    public const string Version = "2";

    public const string VersionNote = "canon-version";

    public const string Hebrew = "hebrew";

    public const string GreekNt = "greek-nt";

    public const string Deuterocanon = "deuterocanon";

    public const string Ethiopic = "ethiopic";

    /// <summary>The canons in the order a record's rows are written.</summary>
    public static readonly string[] All = [Hebrew, GreekNt, Deuterocanon, Ethiopic];

    private const string HebrewText = "BHSA";

    private static readonly HashSet<int> HebrewBooks = [.. Canons.List.Single(c => c.Slug == "tanakh").Ordinals];

    /// <summary>Messale and Tägsas: Proverbs as the Ethiopian Bible divides it.</summary>
    private static readonly HashSet<int> ProverbsInTwo = [91, 92];

    private static readonly HashSet<int> NewTestamentBooks =
        [.. Canons.List.Single(c => c.Slug == "protestant").Sections.Single(s => s.Slug == "new-testament").Ordinals];

    private static readonly HashSet<int> WiderBooks =
    [
        .. Canons.List.Where(c => c.Slug != "ethiopian").SelectMany(c => c.Ordinals)
            .Where(book => !HebrewBooks.Contains(book) && !NewTestamentBooks.Contains(book)),
    ];

    private static readonly HashSet<int> EthiopicBooks =
    [
        .. Canons.List.Single(c => c.Slug == "ethiopian").Ordinals
            .Where(book => !HebrewBooks.Contains(book) && !NewTestamentBooks.Contains(book) && !WiderBooks.Contains(book)
                           && !ProverbsInTwo.Contains(book)),
    ];

    public sealed record Outcome(bool Written, long Rows, long Records, long Names, long Unplaced);

    /// <summary>Writes <c>records/canon</c> when an input moved or the rule's version did, or when forced.</summary>
    public static Outcome Write(ProductStore store, bool force)
    {
        var texts = Texts(store);
        string[] read =
        [
            .. texts.Select(NameProducts.Projected),
            .. texts.Append(HebrewText).Distinct(StringComparer.Ordinal).Select(FrameFile.Product),
            VerseManifestRow.ProductOf(HebrewText),
            "records/records",
        ];
        var inputs = store.Inputs(read.Select(store.Find).OfType<string>());
        if (!force && !NeedsBuild(store, inputs))
        {
            return new Outcome(false, 0, 0, 0, 0);
        }

        var records = Slugs(store);
        var hebrew = HebrewChapters(store);
        var attested = new Dictionary<(string Slug, string Canon), (long Words, HashSet<string> Texts, string FirstText, WordKey First)>();
        long names = 0, unplaced = 0;
        foreach (var text in texts)
        {
            var frame = Frame(store, text);
            using var reader = store.Open(NameProducts.Projected(text), ProjectedName.Schema(text));
            foreach (var name in reader.Rows().Where(n => NameOrigins.IsOwn(n.Origin)))
            {
                if (!records.Contains(name.Record))
                {
                    continue;
                }

                names++;
                if (Of(name.Own.Verse, frame, hebrew) is not { } canon)
                {
                    unplaced++;
                    continue;
                }

                if (!attested.TryGetValue((name.Record, canon), out var held))
                {
                    attested[(name.Record, canon)] = (1, [text], text, name.Own);
                    continue;
                }

                held.Texts.Add(text);
                var earlier = string.CompareOrdinal(text, held.FirstText) is var order && (order < 0 || (order == 0 && name.Own.CompareTo(held.First) < 0));
                attested[(name.Record, canon)] = earlier ? (held.Words + 1, held.Texts, text, name.Own) : held with { Words = held.Words + 1 };
            }
        }

        var rows = attested
            .Select(a => new RecordCanonRow(a.Key.Slug, a.Key.Canon, a.Value.Words, a.Value.Texts.Count, $"{a.Value.FirstText} {a.Value.First.Own}"))
            .OrderBy(r => r.Slug, StringComparer.Ordinal)
            .ThenBy(r => Array.IndexOf(All, r.Canon))
            .ToList();
        using var writer = store.Create(Product, ProductKind.Machine, RecordCanonRow.Schema, ProductHeader.DescribeProducer(Producer), inputs,
            header =>
            {
                header.Notes.Add(new(VersionNote, Version));
                header.Comments.Add("hebrew, greek-nt, deuterocanon, ethiopic: the canons whose verses name the record in a word of a held text; "
                                    + "a record with no row is named in no word");
            });
        writer.WriteAll(rows);
        writer.Complete();
        return new Outcome(true, rows.Count, rows.Select(r => r.Slug).Distinct().Count(), names, unplaced);
    }

    /// <summary>
    /// The canon of a verse, by its frame address (the first, where it has several), else its own. Null for a book no canon
    /// here names.
    /// </summary>
    /// <param name="hebrew">The chapters the Hebrew Bible holds, at their frame addresses.</param>
    public static string? Of(VerseKey own, IReadOnlyDictionary<VerseKey, VerseKey> frame, IReadOnlySet<(int Book, int Chapter)> hebrew)
    {
        var placed = frame.TryGetValue(own, out var address);
        var canonical = placed ? address : FrameFile.AtOwn(own);
        if (NewTestamentBooks.Contains(canonical.Book))
        {
            return GreekNt;
        }

        if (ProverbsInTwo.Contains(canonical.Book))
        {
            return Hebrew;
        }

        if (HebrewBooks.Contains(canonical.Book))
        {
            var apart = !placed && own.VerseLabel.Length > 0 && char.IsLetter(own.VerseLabel[0]);
            return hebrew.Contains((canonical.Book, canonical.Chapter)) && !apart ? Hebrew : Deuterocanon;
        }

        return WiderBooks.Contains(canonical.Book) ? Deuterocanon : EthiopicBooks.Contains(canonical.Book) ? Ethiopic : null;
    }

    private static bool NeedsBuild(ProductStore store, IReadOnlyList<ProductInput> inputs)
    {
        if (store.NeedsBuild(Product, ProductKind.Machine, inputs))
        {
            return true;
        }

        using var reader = store.Open(Product);
        return reader.Header?.Notes.LastOrDefault(note => note.Key == VersionNote).Value != Version;
    }

    /// <summary>The texts with a names projection, in name order.</summary>
    private static List<string> Texts(ProductStore store)
    {
        var folder = Path.Combine(store.ResourcesPath, ProductStore.EssenthosFolder, "names", "projected");
        return Directory.Exists(folder)
            ? [.. Directory.GetFiles(folder).Where(path => !Path.GetFileName(path).StartsWith('.'))
                .Select(Path.GetFileNameWithoutExtension).OfType<string>().Distinct().Order(StringComparer.Ordinal)]
            : [];
    }

    /// <summary>The first frame address of every verse a text's frame places away from its own address.</summary>
    private static Dictionary<VerseKey, VerseKey> Frame(ProductStore store, string text)
    {
        var frame = new Dictionary<VerseKey, VerseKey>();
        if (!store.Exists(FrameFile.Product(text)))
        {
            return frame;
        }

        using var reader = store.Open(FrameFile.Product(text), FrameRow.Schema);
        foreach (var row in reader.Rows())
        {
            foreach (var (own, canonical) in row.Verses())
            {
                frame[own] = canonical[0];
            }
        }

        return frame;
    }

    /// <summary>The chapters of the Hebrew Bible's verses, at their frame addresses.</summary>
    private static HashSet<(int Book, int Chapter)> HebrewChapters(ProductStore store)
    {
        var frame = Frame(store, HebrewText);
        using var reader = store.Open(VerseManifestRow.ProductOf(HebrewText), VerseManifestRow.Schema);
        return [.. reader.Rows().Select(row => frame.TryGetValue(row.Own, out var address) ? address : FrameFile.AtOwn(row.Own))
            .Select(address => (address.Book, address.Chapter))];
    }

    private static HashSet<string> Slugs(ProductStore store)
    {
        using var reader = store.Open("records/records");
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in reader.Rows())
        {
            slugs.Add(row[0]!);
        }

        return slugs;
    }
}
