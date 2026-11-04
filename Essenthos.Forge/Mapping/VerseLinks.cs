using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// One verse link of a pair, <c>mapping/&lt;pair&gt;/verses</c>: which verses of the first text are which of the second.
/// <c>from</c> and <c>to</c> are own verse keys in key order, space-separated.
/// </summary>
internal sealed record VerseLinkRow(string From, string To, string Relation, string Method, double? Confidence, string Source, string? Note)
{
    public static readonly ProductSchema<VerseLinkRow> Schema = new ProductSchema<VerseLinkRow>()
        .Text("from", r => r.From)
        .Text("to", r => r.To)
        .Text("relation", r => r.Relation)
        .Text("method", r => r.Method)
        .OptionalText("confidence", r => MappingText.Confidence(r.Confidence))
        .Text("source", r => r.Source)
        .OptionalText("note", r => r.Note)
        .Reads(row => new VerseLinkRow(row.Text(0), row.Text(1), row.Text(2), row.Text(3),
            MappingText.Confidence(row.TextOrNull(4)), row.Text(5), row.TextOrNull(6)));
}

/// <summary>A text's verses in print order, each with the frame addresses it stands at, the first its primary one.</summary>
internal sealed class TextFrame
{
    public TextFrame(string slug, IReadOnlyList<VerseKey> verses, IReadOnlyDictionary<VerseKey, IReadOnlyList<(int Book, int Chapter, int Verse)>> addresses)
    {
        Slug = slug;
        Verses = verses;
        Addresses = addresses;
        Index = verses.Select((verse, i) => (verse, i)).ToDictionary(v => v.verse, v => v.i);
    }

    public string Slug { get; }

    public IReadOnlyList<VerseKey> Verses { get; }

    /// <summary>Every verse's addresses; empty for a text the frame does not place.</summary>
    public IReadOnlyDictionary<VerseKey, IReadOnlyList<(int Book, int Chapter, int Verse)>> Addresses { get; }

    public IReadOnlyDictionary<VerseKey, int> Index { get; }

    public (int Book, int Chapter, int Verse)? Primary(VerseKey verse) =>
        Addresses.TryGetValue(verse, out var addresses) ? addresses[0] : null;

    /// <summary>The text's verses and frame products: a verse its frame does not list stands at its own address.</summary>
    public static TextFrame Read(ProductStore store, string slug)
    {
        var verses = new List<VerseKey>();
        using (var reader = store.Open(VerseManifestRow.ProductOf(slug), VerseManifestRow.Schema))
        {
            verses.AddRange(reader.Rows().Select(row => row.Own));
        }

        var addresses = new Dictionary<VerseKey, IReadOnlyList<(int, int, int)>>(verses.Count);
        if (store.Exists(FrameFile.Product(slug)))
        {
            using var reader = store.Open(FrameFile.Product(slug), FrameRow.Schema);
            var listed = reader.Rows().SelectMany(row => row.Verses()).ToDictionary(verse => verse.Own, verse => verse.Canonical);
            foreach (var verse in verses)
            {
                addresses[verse] = listed.TryGetValue(verse, out var canonical)
                    ? [.. canonical.Select(a => (a.Book, a.Chapter, a.Verse))]
                    : [(verse.Book, verse.Chapter, verse.Verse)];
            }
        }

        return new TextFrame(slug, verses, addresses);
    }
}

/// <summary>What the verse links of a pair are drawn from besides its final: the two frames and how each direction is declared.</summary>
/// <param name="ReverseLinked">Whether the pair the other way round is linked word by word: its verse links count as joining too.</param>
internal sealed record PairFrames(TextFrame From, TextFrame To, bool ReverseLinked)
{
    public DeclaredVersePair? Declared => VerseLinks.Declaration(From.Slug, To.Slug);

    public DeclaredVersePair? ReverseDeclared => VerseLinks.Declaration(To.Slug, From.Slug);
}

/// <summary>
/// The verse links of a pair from the two texts' frames and the pair's final, reading no database: the rule
/// <see cref="VerseLinkLoader"/> applies to a corpus, as a function of files (DOC-0224 §5).
/// <list type="number">
/// <item>Verses standing at one primary address (under the name <see cref="TwinPassages"/> joins it at) are one
/// link, taken as connected components; a declared pair leaves out the books its declaration names and, where it
/// says so, the chapters the two print otherwise. A linked pair is joined wherever both texts stand.</item>
/// <item>The Ge'ez's chapters placed by its reading of the Greek are that reading's links, and its books divided
/// its own way are joined through the verse map, each with the line's confidence.</item>
/// <item>A verse covering a further address joins what stands there primarily in the other text.</item>
/// <item>A verse pair a source's own word links cross, or a transposition, where no verse link joins it, is a
/// link of its own; then the covering again, and a link stated twice is one.</item>
/// </list>
/// </summary>
internal static class VerseLinks
{
    private const string StatedNote = "the verse this source's own word links put against that one";

    private const string TransposedNote = "the passage these word links find written at another place";

    public static DeclaredVersePair? Declaration(string from, string to) =>
        VerseLinkLoader.DeclaredPairs.FirstOrDefault(pair => pair.From == from && pair.To == to);

    /// <summary>A link while it is drawn: each text's verses by their place in it, counted from 1.</summary>
    private sealed class Draft(HashSet<int> from, HashSet<int> to, string relation, string method, double? confidence, string source, string? note)
    {
        public HashSet<int> From { get; } = from;
        public HashSet<int> To { get; } = to;
        public string Relation { get; } = relation;
        public string Method { get; } = method;
        public double? Confidence { get; } = confidence;
        public string Source { get; } = source;
        public string? Note { get; } = note;
    }

    /// <summary>The pair's verse links: none when it is neither linked in <paramref name="final"/> nor declared.</summary>
    public static List<VerseLinkRow> Derive(PairFrames frames, IReadOnlyList<FinalRow> final)
    {
        var drafts = Draw(frames, final);
        var rows = drafts
            .Select(d => new VerseLinkRow(Keys(frames.From, d.From), Keys(frames.To, d.To), d.Relation, d.Method, d.Confidence, d.Source, d.Note))
            .DistinctBy(r => (r.From, r.To, r.Relation, r.Method, r.Confidence, r.Source, r.Note))
            .ToList();
        rows.Sort((a, b) =>
        {
            var order = Compare(frames.From, a.From, b.From);
            if (order == 0) order = Compare(frames.To, a.To, b.To);
            if (order == 0) order = string.CompareOrdinal(a.Method, b.Method);
            if (order == 0) order = string.CompareOrdinal(a.Source, b.Source);
            if (order == 0) order = string.CompareOrdinal(a.Relation, b.Relation);
            return order;
        });
        return rows;
    }

    /// <summary>
    /// Whether a verse link of the pair, either way round, joins a verse of the first text to one of the second:
    /// the links <see cref="Derive"/> writes.
    /// </summary>
    public static Func<VerseKey, VerseKey, bool> Joins(PairFrames frames, IReadOnlyList<FinalRow> final)
    {
        var drafts = Draw(frames, final);
        drafts.AddRange(Reverse(frames));
        var (fromLinks, toLinks) = Memberships(drafts, frames);
        return (from, to) => frames.From.Index.TryGetValue(from, out var f) && frames.To.Index.TryGetValue(to, out var t)
                             && fromLinks.TryGetValue(f + 1, out var a) && toLinks.TryGetValue(t + 1, out var b) && a.Overlaps(b);
    }

    private static List<Draft> Draw(PairFrames frames, IReadOnlyList<FinalRow> final)
    {
        var linked = final.Any(row => row.Settled);
        var drafts = Structure(frames.From, frames.To, linked, frames.Declared);
        if (drafts.Count == 0 && !linked)
        {
            return drafts;
        }

        Cover(drafts, frames.From, frames.To);
        var (fromLinks, toLinks) = Memberships([.. drafts, .. Reverse(frames)], frames);
        drafts.AddRange(Stated(frames, final, (f, t) => fromLinks.TryGetValue(f, out var a) && toLinks.TryGetValue(t, out var b) && a.Overlaps(b)));
        Cover(drafts, frames.From, frames.To);
        return drafts;
    }

    /// <summary>The pair the other way round, drawn from the frames alone and turned to this pair's sides.</summary>
    private static IEnumerable<Draft> Reverse(PairFrames frames)
    {
        var reverse = Structure(frames.To, frames.From, frames.ReverseLinked, frames.ReverseDeclared);
        Cover(reverse, frames.To, frames.From);
        return reverse.Select(d => new Draft(d.To, d.From, d.Relation, d.Method, d.Confidence, d.Source, d.Note));
    }

    /// <summary>Each verse's links, by verse index, on each side.</summary>
    private static (Dictionary<int, HashSet<int>> From, Dictionary<int, HashSet<int>> To) Memberships(List<Draft> drafts, PairFrames frames)
    {
        var from = new Dictionary<int, HashSet<int>>();
        var to = new Dictionary<int, HashSet<int>>();
        for (var i = 0; i < drafts.Count; i++)
        {
            foreach (var verse in drafts[i].From)
            {
                (from.TryGetValue(verse, out var set) ? set : from[verse] = []).Add(i);
            }

            foreach (var verse in drafts[i].To)
            {
                (to.TryGetValue(verse, out var set) ? set : to[verse] = []).Add(i);
            }
        }

        return (from, to);
    }

    /// <summary>
    /// The links the frame draws (<see cref="VerseLinkLoader"/>'s join on a corpus with none yet): components of the
    /// primary addresses, the Ge'ez's reading and verse map. Nothing for a pair neither linked nor declared.
    /// </summary>
    private static List<Draft> Structure(TextFrame from, TextFrame to, bool linked, DeclaredVersePair? declaration)
    {
        var drafts = new List<Draft>();
        if (!linked && declaration is null)
        {
            return drafts;
        }

        var here = Addressed(from);
        var there = Addressed(to);
        if (declaration is not null)
        {
            var left = VerseLinkLoader.Outside(declaration, linked);
            here = here.Where(address => !left.Contains(address.Key.Item1)).ToDictionary(address => address.Key, address => address.Value);
            if (declaration.AgreeingChaptersOnly)
            {
                (here, there) = VerseLinkLoader.Agreeing(here, there, Covered(from), Covered(to), declaration.Without);
            }
        }

        var read = from.Slug == GeezTextSource.Slug ? Read(from) : [];
        if (read.Count > 0)
        {
            // A verse the Greek has nothing for stands beside the verse before it, and answers nothing there.
            here = here
                .Select(address => (address.Key, Verses: address.Value.Where(v => !read.TryGetValue(v, out var line) || line.Greek.Count > 0).ToList()))
                .Where(address => address.Verses.Count > 0)
                .ToDictionary(address => address.Key, address => address.Verses);
        }

        var alone = 0;
        var frameMethod = EnumSpelling.Of(LinkMethod.StatedBySource);
        var equals = EnumSpelling.Of(LinkRelation.Equals);
        foreach (var component in VerseLinkLoader.Components(here, there, ref alone))
        {
            var reading = component.From.Where(read.ContainsKey).ToList();
            drafts.Add(reading.Count == 0
                ? new Draft([.. component.From], [.. component.To], equals, frameMethod, null, VerseLinkLoader.Source, null)
                : new Draft([.. component.From], [.. component.To], equals, EnumSpelling.Of(LinkMethod.Aligner),
                    GeezVersification.Confidence(reading.Select(v => read[v])), GeezVersification.Source, null));
        }

        if (declaration is not null && from.Slug == GeezTextSource.Slug)
        {
            drafts.AddRange(Mapped(from, to));
        }

        return drafts;
    }

    /// <summary>Every verse by the address it primarily stands at, under the name <see cref="TwinPassages"/> joins it at.</summary>
    private static Dictionary<(int, int, int), List<int>> Addressed(TextFrame text)
    {
        var addressed = new Dictionary<(int, int, int), List<int>>();
        for (var i = 0; i < text.Verses.Count; i++)
        {
            if (text.Addresses.TryGetValue(text.Verses[i], out var addresses))
            {
                var key = TwinPassages.Joined(addresses[0]);
                (addressed.TryGetValue(key, out var list) ? list : addressed[key] = []).Add(i + 1);
            }
        }

        return addressed;
    }

    private static HashSet<(int, int, int)> Covered(TextFrame text) =>
        [.. text.Addresses.Values.SelectMany(addresses => addresses.Skip(1)).Select(TwinPassages.Joined)];

    /// <summary>The Ge'ez verses its reading of the Greek places, by place (<see cref="GeezVersification"/>).</summary>
    private static Dictionary<int, GeezVersificationLine> Read(TextFrame geez)
    {
        var books = GeezVersification.Lines.Select(line => line.Book).ToHashSet();
        var read = new Dictionary<int, GeezVersificationLine>();
        for (var i = 0; i < geez.Verses.Count; i++)
        {
            var verse = geez.Verses[i];
            if (verse.VerseLabel.Length == 0 && books.Contains(verse.Book)
                && GeezVersification.ByVerse.TryGetValue((verse.Book, verse.Chapter, verse.Verse), out var line))
            {
                read[i + 1] = line;
            }
        }

        return read;
    }

    /// <summary>The Ge'ez books divided its own way, joined through the verse map line by line (<see cref="GeezVerseMap"/>).</summary>
    private static IEnumerable<Draft> Mapped(TextFrame geez, TextFrame to)
    {
        var own = new Dictionary<(int, int, int), int>();
        for (var i = 0; i < geez.Verses.Count; i++)
        {
            own.TryAdd((geez.Verses[i].Book, geez.Verses[i].Chapter, geez.Verses[i].Verse), i + 1);
        }

        var there = Addressed(to);
        var method = EnumSpelling.Of(LinkMethod.ModelReading);
        var equals = EnumSpelling.Of(LinkRelation.Equals);
        foreach (var line in GeezVerseMap.Lines)
        {
            var from = line.From.Select(v => own.GetValueOrDefault((line.Book, v.Chapter, v.Verse))).Where(i => i > 0).ToHashSet();
            var verses = line.To
                .Where(address => GeezTextSource.Aligns(to.Slug, address.Book, address.Chapter, address.Verse))
                .SelectMany(address => there.GetValueOrDefault(address) ?? [])
                .ToHashSet();
            if (from.Count > 0 && verses.Count > 0)
            {
                yield return new Draft(from, verses, equals, method, line.Confidence, GeezVerseMap.SourceOf(line.Book), null);
            }
        }
    }

    /// <summary>
    /// Adds to each link, until nothing more is added, the verses of the other text standing primarily at an address a
    /// member covers beyond its own: the frame's statement that one verse spans two rows.
    /// </summary>
    private static void Cover(List<Draft> drafts, TextFrame from, TextFrame to)
    {
        var fromAt = Primaries(from);
        var toAt = Primaries(to);
        bool added;
        do
        {
            added = false;
            foreach (var draft in drafts)
            {
                added |= Covering(draft.From, from, toAt, draft.To);
                added |= Covering(draft.To, to, fromAt, draft.From);
            }
        }
        while (added);

        static bool Covering(HashSet<int> members, TextFrame text, Dictionary<(int, int, int), List<int>> other, HashSet<int> into)
        {
            var added = false;
            foreach (var member in members)
            {
                if (!text.Addresses.TryGetValue(text.Verses[member - 1], out var addresses))
                {
                    continue;
                }

                foreach (var address in addresses.Skip(1))
                {
                    if (other.TryGetValue(address, out var counterparts))
                    {
                        foreach (var counterpart in counterparts)
                        {
                            added |= into.Add(counterpart);
                        }
                    }
                }
            }

            return added;
        }
    }

    /// <summary>Every verse by its primary address as the frame writes it, without the twin's other name.</summary>
    private static Dictionary<(int, int, int), List<int>> Primaries(TextFrame text)
    {
        var at = new Dictionary<(int, int, int), List<int>>();
        for (var i = 0; i < text.Verses.Count; i++)
        {
            if (text.Addresses.TryGetValue(text.Verses[i], out var addresses))
            {
                (at.TryGetValue(addresses[0], out var list) ? list : at[addresses[0]] = []).Add(i + 1);
            }
        }

        return at;
    }

    /// <summary>
    /// The verse pairs a stated source's word links cross, or a transposition's, where nothing joins them yet: a source
    /// saying this word renders that one says the two verses answer each other. A model's link testifies to nothing and
    /// is never one of them (<see cref="VerseLinkLoader"/>'s StatedVerseImport).
    /// </summary>
    private static IEnumerable<Draft> Stated(PairFrames frames, IReadOnlyList<FinalRow> final, Func<int, int, bool> joined)
    {
        var stated = EnumSpelling.Of(LinkMethod.StatedBySource);
        var crossings = new Dictionary<(int From, int To), (string Source, bool Transposed, string? Method, double? Confidence)>();
        foreach (var row in final.Where(r => r.Settled))
        {
            var method = EnumSpelling.Of(MappingMethods.DatabaseMethod(row.Method));
            var transposes = row.Relation == "transposes";
            if (method != stated && !transposes)
            {
                continue;
            }

            foreach (var from in Verses(frames.From, row.From))
            {
                foreach (var to in Verses(frames.To, row.To))
                {
                    var seen = crossings.TryGetValue((from, to), out var c);
                    crossings[(from, to)] = (
                        seen && string.CompareOrdinal(c.Source, row.Source) <= 0 ? c.Source : row.Source,
                        c.Transposed || (transposes && method != stated),
                        transposes && (c.Method is null || string.CompareOrdinal(method, c.Method) < 0) ? method : c.Method,
                        transposes && (c.Confidence is null || row.Confidence < c.Confidence) ? row.Confidence : c.Confidence);
                }
            }
        }

        foreach (var ((from, to), c) in crossings.OrderBy(c => c.Key))
        {
            if (joined(from, to))
            {
                continue;
            }

            yield return c.Transposed
                ? new Draft([from], [to], "transposes", c.Method!, c.Confidence, c.Source, TransposedNote)
                : new Draft([from], [to], "renders", stated, null, c.Source, StatedNote);
        }
    }

    private static IEnumerable<int> Verses(TextFrame text, string keys) =>
        MappingText.Split(keys).Select(key => VerseKey.Parse(key[..key.LastIndexOf('.')])).Distinct()
            .Select(verse => text.Index.TryGetValue(verse, out var i) ? i + 1 : 0).Where(i => i > 0);

    private static string Keys(TextFrame text, HashSet<int> verses) =>
        string.Join(' ', verses.Select(i => text.Verses[i - 1]).Order().Select(v => v.ToString()));

    private static int Compare(TextFrame text, string a, string b)
    {
        var first = a.Length == 0 ? default : VerseKey.Parse(a.Split(' ')[0]);
        var second = b.Length == 0 ? default : VerseKey.Parse(b.Split(' ')[0]);
        var order = first.CompareTo(second);
        return order != 0 ? order : string.CompareOrdinal(a, b);
    }
}
