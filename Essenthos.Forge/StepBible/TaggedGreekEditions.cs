using System.Text.RegularExpressions;
using Essenthos.Core.Conversion;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;

namespace Essenthos.Core.StepBible;

/// <summary>
/// One word of TAGNT, STEPBible's amalgamated Greek New Testament, as it says which printed editions hold it: its reference,
/// its Strong number and the editions column as TAGNT writes it (<c>NA28+NA27+Tyn+SBL+WH+Treg+TR+Byz</c>). Its Greek is not
/// kept: TAGNT spells every word as NA28 does.
/// </summary>
internal sealed record TaggedGreekWord(string Reference, VerseKey Verse, string? Number, string Editions)
{
    /// <summary>The editions TAGNT names, without the marks of a word one of them prints elsewhere in the verse (<c>TR»</c>).</summary>
    public IReadOnlySet<string> Flags { get; } = Editions.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(flag => flag.TrimEnd('»', '«')).Where(flag => flag.Length > 0).ToHashSet(StringComparer.Ordinal);
}

/// <summary>
/// One row of <c>editions/tagnt</c>: a TAGNT word, the editions TAGNT says hold it, and the word of each edition the corpus holds
/// that it was joined to (the edition's own word key), where the join found one.
/// </summary>
internal sealed record TaggedEditionRow(string Reference, VerseKey Verse, string? Number, string Editions, IReadOnlyList<string?> Words)
{
    public static readonly ProductSchema<TaggedEditionRow> Schema = new ProductSchema<TaggedEditionRow>()
        .Text("tagnt", r => r.Reference)
        .Text("verse", r => r.Verse.ToString())
        .OptionalText("strong", r => r.Number)
        .Text("editions", r => r.Editions)
        .OptionalText("sblgnt", r => r.Words[0])
        .OptionalText("wh1881", r => r.Words[1])
        .OptionalText("rp2018", r => r.Words[2])
        .OptionalText("tr1550", r => r.Words[3])
        .Reads(row => new TaggedEditionRow(row.Text(0), VerseKey.Parse(row.Text(1)), row.TextOrNull(2), row.Text(3),
            [row.TextOrNull(4), row.TextOrNull(5), row.TextOrNull(6), row.TextOrNull(7)]));
}

/// <summary>
/// TAGNT's edition flags laid on the words of the Greek editions the corpus holds (TSK-0242). TAGNT is not loaded as a text and
/// its Greek is not kept: it spells by NA28. What it gives is which of eight editions holds each word (NA27, NA28, Tyndale House,
/// SBLGNT, Westcott-Hort, Tregelles, the Textus Receptus, the Byzantine), and four of them are texts here.
///
/// <para>
/// Its <c>TR</c> flag follows Stephanus 1550, not Scrivener 1894 as its header says: at the 249 places the two differ and TAGNT
/// can tell, 223 agree with Stephanus (owner, 2026-11-24: the flag goes to the edition it matches). <c>Byz</c> is Robinson and
/// Pierpont's 2005 text, which RP2018 continues.
/// </para>
///
/// <para>
/// The join is the edition's words against TAGNT's words it flags, verse by verse, by Strong number and the longest common
/// subsequence, so a word the two dispute is left unjoined rather than given to whatever stands in its place, and a word an
/// edition prints in another order is unjoined too.
/// </para>
/// </summary>
internal static partial class TaggedGreekEditions
{
    public const string Product = "editions/tagnt";
    public const string Folder = "STEPBible";
    public const string Producer = "forge tagnt-editions";

    /// <summary>The editions joined, in the product's column order, with the flag TAGNT gives each.</summary>
    public static readonly IReadOnlyList<(string Slug, string Flag)> Editions =
    [
        ("SBLGNT", "SBL"),
        ("WH1881", "WH"),
        ("RP2018", "Byz"),
        ("TR1550", "TR"),
    ];

    private static readonly string[] Books =
        ["Mat", "Mrk", "Luk", "Jhn", "Act", "Rom", "1Co", "2Co", "Gal", "Eph", "Php", "Col", "1Th", "2Th", "1Ti", "2Ti", "Tit", "Phm", "Heb",
        "Jas", "1Pe", "2Pe", "1Jn", "2Jn", "3Jn", "Jud", "Rev"];

    [GeneratedRegex(@"^([1-3]?[A-Z][a-z]{1,2})\.(\d+)\.(\d+)(?:\([^)]*\))?#(\d+)")]
    private static partial Regex WordReference();

    [GeneratedRegex(@"^G0*(\d+)")]
    private static partial Regex StrongNumber();

    /// <summary>Every word row of the TAGNT volumes in a folder, in the order they are printed.</summary>
    public static List<TaggedGreekWord> Read(string folder)
    {
        var words = new List<TaggedGreekWord>(150_000);
        var files = Directory.GetFiles(folder, "TAGNT *.txt").Order(StringComparer.Ordinal).ToList();
        if (files.Count == 0)
        {
            throw new FileNotFoundException($"No TAGNT volume is in {folder}; run avioniq services run fetch-stepbible.");
        }

        foreach (var file in files)
        {
            foreach (var line in File.ReadLines(file))
            {
                if (Word(line) is { } word)
                {
                    words.Add(word);
                }
            }
        }

        return words;
    }

    /// <summary>A line of a TAGNT volume as a word, or null for a heading, a verse line or a note.</summary>
    public static TaggedGreekWord? Word(string line)
    {
        var match = WordReference().Match(line);
        var book = match.Success ? Array.IndexOf(Books, match.Groups[1].Value) : -1;
        if (book < 0)
        {
            return null;
        }

        var columns = line.Split('\t');
        if (columns.Length < 6)
        {
            return null;
        }

        var reference = columns[0].Split('=')[0];
        var number = StrongNumber().Match(columns[3]) is { Success: true } strong ? "G" + strong.Groups[1].Value : null;
        var editions = string.Join('+', columns[5].Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var verse = VerseKey.Parse($"{40 + book}.{match.Groups[2].Value}:{match.Groups[3].Value}");
        return new TaggedGreekWord(reference, verse, number, editions);
    }

    /// <summary>
    /// TAGNT's words an edition holds joined to the edition's words, one verse at a time: the index of each TAGNT word joined and
    /// the edition word's place in its text.
    /// </summary>
    public static Dictionary<int, int> Join(IReadOnlyList<TaggedGreekWord> tagged, ProductText edition, string flag)
    {
        var joined = new Dictionary<int, int>();
        foreach (var verse in tagged.Select((word, at) => (word, at)).Where(entry => entry.word.Flags.Contains(flag)).GroupBy(entry => entry.word.Verse))
        {
            if (!edition.Has(verse.Key))
            {
                continue;
            }

            var mine = verse.ToList();
            var theirs = edition.InVerse(verse.Key).ToList();
            foreach (var (i, j) in Common([.. mine.Select(entry => entry.word.Number)], [.. theirs.Select(id => edition.Words[id].Strong)]))
            {
                joined[mine[i].at] = theirs[j];
            }
        }

        return joined;
    }

    /// <summary>The longest common subsequence of two lists of Strong numbers, as index pairs; a word with no number joins nothing.</summary>
    public static List<(int, int)> Common(IReadOnlyList<string?> first, IReadOnlyList<string?> second)
    {
        var lengths = new int[first.Count + 1, second.Count + 1];
        for (var i = first.Count - 1; i >= 0; i--)
        {
            for (var j = second.Count - 1; j >= 0; j--)
            {
                lengths[i, j] = Same(first[i], second[j]) ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var pairs = new List<(int, int)>();
        for (int i = 0, j = 0; i < first.Count && j < second.Count;)
        {
            if (Same(first[i], second[j]))
            {
                pairs.Add((i++, j++));
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return pairs;
    }

    private static bool Same(string? one, string? other) => one is not null && other is not null && Normal(one) == Normal(other);

    /// <summary>A Strong number as both sides write it: <c>G0976</c>, <c>G976</c> and <c>G2424G</c> are the same entry's.</summary>
    private static string Normal(string number) => StrongNumber().Match(number) is { Success: true } match ? match.Groups[1].Value : number;

    /// <summary>Writes <c>editions/tagnt</c> where TAGNT or one of the editions' words moved; the rows written, or null when current.</summary>
    public static (long Rows, IReadOnlyList<(string Slug, int Joined, int Flagged)> Joins)? Make(ProductStore store, bool force)
    {
        List<ProductInput> inputs = [store.Input(Folder), .. Editions.Select(edition => store.ProductInput(TextWord.ProductOf(edition.Slug)))];
        if (!force && !store.NeedsBuild(Product, ProductKind.Machine, inputs))
        {
            return null;
        }

        var tagged = Read(Path.Combine(store.ResourcesPath, Folder));
        var texts = new ProductTexts(store);
        var words = new string?[Editions.Count][];
        var joins = new List<(string, int, int)>();
        for (var e = 0; e < Editions.Count; e++)
        {
            var (slug, flag) = Editions[e];
            var edition = texts[slug];
            var joined = Join(tagged, edition, flag);
            words[e] = new string?[tagged.Count];
            foreach (var (at, id) in joined)
            {
                words[e][at] = edition.Key(id).Own.ToString();
            }

            joins.Add((slug, joined.Count, tagged.Count(word => word.Flags.Contains(flag))));
        }

        using var writer = store.Create(Product, ProductKind.Machine, TaggedEditionRow.Schema, ProductHeader.DescribeProducer(Producer), inputs);
        for (var at = 0; at < tagged.Count; at++)
        {
            var word = tagged[at];
            writer.Write(new TaggedEditionRow(word.Reference, word.Verse, word.Number, word.Editions, [.. words.Select(column => column[at])]));
        }

        writer.Complete();
        return (writer.Rows, joins);
    }
}
