using Essenthos.Core.Conversion;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Products;
using DeclaredField = Essenthos.Core.Conversion.TextField;

namespace Essenthos.Core.Loading.Frame;

/// <summary>
/// Every text's frame file made from products alone: the verses of its manifest, their letters counted in its words, the
/// verses its titles product lists, its versification from its declaration, and the versification data. No database is
/// read, so <c>forge build</c> makes the frame again whenever one of those moved.
/// </summary>
internal static class FrameProducts
{
    public const string Producer = "frame-files";

    /// <param name="Written">False when the file was current and left as it was.</param>
    public sealed record Outcome(string Slug, int Verses, int Lines, int Titles, bool Written);

    /// <summary>The texts the store holds a manifest, words and declaration of, in slug order.</summary>
    public static List<string> Texts(ProductStore store) =>
    [
        .. TextCatalogue.All.Select(text => text.Slug)
            .Where(slug => store.Exists(VerseManifestRow.ProductOf(slug)) && store.Exists(TextWord.ProductOf(slug))
                                                                          && store.Exists(DeclaredField.ProductOf(slug)))
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>The products a text's frame is made from, besides the versification data.</summary>
    public static IEnumerable<string> Products(string slug) =>
        [VerseManifestRow.ProductOf(slug), TextWord.ProductOf(slug), TextTitle.ProductOf(slug)];

    public static Versification VersificationOf(ProductStore store, string slug)
    {
        using var reader = store.Open(DeclaredField.ProductOf(slug), DeclaredField.Schema);
        var field = reader.Rows().FirstOrDefault(f => f.Field == "versification")?.Value
                    ?? throw new InvalidDataException($"{DeclaredField.ProductOf(slug)} names no versification.");
        return EnumSpelling.ToVersification(field);
    }

    /// <summary>
    /// Writes the frame file of each text named, or of every text, whose inputs moved since it was written (all of them
    /// with <paramref name="force"/>). A text whose versification the data does not describe gets no frame file.
    /// </summary>
    public static List<Outcome> Write(ProductStore store, VersificationRules rules, IReadOnlySet<string>? only, bool force, int parallel)
    {
        var titled = TitledChapters(store);
        var outcomes = new List<Outcome>();
        var texts = Texts(store).Where(slug => only is null || only.Contains(slug)).ToList();
        Parallel.ForEach(texts, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel) }, slug =>
        {
            var tradition = VersificationOf(store, slug);
            if (!rules.Covers(tradition))
            {
                return;
            }

            var inputs = Inputs(store, slug);
            if (!force && !store.NeedsBuild(FrameFile.Product(slug), ProductKind.Machine, inputs))
            {
                lock (outcomes)
                {
                    outcomes.Add(new Outcome(slug, 0, 0, 0, false));
                }

                return;
            }

            var framed = Framed(store, slug, tradition, rules, titled);
            CanonicalFrameLoader.Write(new Text { Slug = slug, Name = slug, Language = string.Empty, Versification = tradition }, rules, store,
                framed, "products");
            lock (outcomes)
            {
                outcomes.Add(new Outcome(slug, framed.Verses.Count, framed.Rows.Count, framed.Titles, true));
            }
        });

        return [.. outcomes.OrderBy(o => o.Slug, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<ProductInput> Inputs(ProductStore store, string slug) =>
        store.Inputs([
            "Versification/TVTMS.txt",
            .. Products(slug).Select(product => store.Find(product)).OfType<string>(),
            .. VersificationData.Products.Select(product => store.Find(product)
                ?? throw new FileNotFoundException($"{product} is not under {store.ResourcesPath}.")),
            store.Find(TitledPsalms) ?? throw new FileNotFoundException($"{TitledPsalms} is not under {store.ResourcesPath}."),
            .. slug == GeezTextSource.Slug ? ["Essenthos/versification/geez.tsv"] : Array.Empty<string>(),
        ]);

    /// <summary>The hand file naming the psalms whose title the frame numbers apart, as verse 0.</summary>
    public const string TitledPsalms = "versification/titled-psalms";

    /// <summary>The chapters <see cref="TitledPsalms"/> names.</summary>
    public static HashSet<(int Book, int Chapter)> TitledChapters(ProductStore store)
    {
        using var reader = store.Open(TitledPsalms, TitledRow.Schema);
        return [.. reader.Rows().Select(row => (row.Book, row.Chapter))];
    }

    private sealed record TitledRow(int Book, int Chapter)
    {
        public static readonly ProductSchema<TitledRow> Schema = new ProductSchema<TitledRow>()
            .Integer("book", r => r.Book)
            .Integer("chapter", r => r.Chapter)
            .Reads(row => new TitledRow(row.Int32(0), row.Int32(1)));
    }

    /// <summary>
    /// The chapters any frame file in the store places a verse at the title of as its primary place, set against
    /// <see cref="TitledPsalms"/>: what the frames hold that the list does not name, and what the list names that none holds.
    /// </summary>
    public static (List<(int Book, int Chapter)> Unlisted, List<(int Book, int Chapter)> Unheld) CheckTitled(ProductStore store, IEnumerable<string> texts)
    {
        var held = new HashSet<(int, int)>();
        foreach (var slug in texts.Where(slug => store.Exists(FrameFile.Product(slug))))
        {
            using var reader = store.Open(FrameFile.Product(slug), FrameRow.Schema);
            foreach (var (_, canonical) in reader.Rows().SelectMany(row => row.Verses()))
            {
                if (canonical[0].Verse == CanonicalReference.TitleVerse)
                {
                    held.Add((canonical[0].Book, canonical[0].Chapter));
                }
            }
        }

        var listed = TitledChapters(store);
        return ([.. held.Except(listed).Order()], [.. listed.Except(held).Order()]);
    }

    /// <summary>
    /// A text's frame computed from its products: where the rules put each verse of its manifest, and the title's address
    /// for a verse its titles product lists where that verse stands first in a psalm whose title the frame numbers apart,
    /// unless a reading of the edition (<see cref="CanonicalFrameLoader.Read"/>) placed the verse.
    /// </summary>
    internal static CanonicalFrameLoader.FramedText Framed(
        ProductStore store,
        string slug,
        Versification tradition,
        VersificationRules rules,
        IReadOnlySet<(int Book, int Chapter)> titled)
    {
        var letters = TextConverter.Lengths(store, slug);
        var verses = new List<PlacedVerse>();
        using (var reader = store.Open(VerseManifestRow.ProductOf(slug), VerseManifestRow.Schema))
        {
            foreach (var row in reader.Rows())
            {
                var own = row.Own;
                verses.Add(new PlacedVerse(verses.Count + 1, own.Book, own.Chapter, own.Verse, own.VerseLabel, letters.GetValueOrDefault(own)));
            }
        }

        var marked = new HashSet<VerseKey>();
        if (store.Exists(TextTitle.ProductOf(slug)))
        {
            using var reader = store.Open(TextTitle.ProductOf(slug), TextTitle.Schema);
            marked.UnionWith(reader.Rows().Select(title => title.Own));
        }

        var decisions = new List<(Versification, PassageDecision)>();
        var read = CanonicalFrameLoader.Read(slug);
        var expected = CanonicalFrameLoader.Expected(rules, tradition, verses, rules.Data.Books(slug), read,
            (t, decision) => decisions.Add((t, decision)));
        var placed = expected.GroupBy(reference => reference.VerseId)
            .ToDictionary(group => group.Key, group => group.Select(reference => new VerseKey(reference.Book, reference.Chapter, reference.Verse)).ToList());

        var titles = 0;
        var covered = placed.Values.SelectMany(addresses => addresses)
            .Where(address => address.Verse == CanonicalReference.TitleVerse)
            .Select(address => (address.Book, address.Chapter))
            .ToHashSet();
        foreach (var verse in verses)
        {
            var addresses = placed[verse.Id];
            var first = addresses[0];
            if (marked.Contains(new VerseKey(verse.Book, verse.Chapter, verse.Number, verse.Label))
                && read?.ContainsKey((verse.Book, verse.Chapter, verse.Number)) != true
                && first.Verse == 1 && titled.Contains((first.Book, first.Chapter)) && covered.Add((first.Book, first.Chapter)))
            {
                addresses.Add(new VerseKey(first.Book, first.Chapter, CanonicalReference.TitleVerse));
                titles++;
            }
        }

        var rows = FrameFile.Rows(verses.Select(verse => (
            new VerseKey(verse.Book, verse.Chapter, verse.Number, verse.Label),
            (IReadOnlyList<VerseKey>)placed[verse.Id])));
        return new CanonicalFrameLoader.FramedText(verses, [], rows, decisions, titles, store.Find(TextWord.ProductOf(slug)))
        {
            Manifest = store.Find(VerseManifestRow.ProductOf(slug)),
            Titled = store.Find(TextTitle.ProductOf(slug)),
        };
    }
}
