using System.Globalization;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Loading.Frame;

/// <summary>
/// A verse of one of the additions the Greek Esther carries, in the lettered chapters the Latin
/// prints them under: <c>Est.A:1</c> is the first verse of Mordecai's dream. The versification data
/// states where each stands in the standard numbering — A:1 is Esther 11:2 — which is the address the
/// Latin and English Bibles print it at, in chapters 10 to 16.
/// </summary>
internal readonly record struct AdditionVerse(int Book, char Section, int Verse)
{
    public override string ToString() => $"{Book}.{Section}:{Verse}";

    /// <summary>
    /// Parses <c>Est.A:1</c>. A chapter that is a number is not an addition and does not parse here.
    /// </summary>
    public static bool TryParse(string value, out AdditionVerse verse)
    {
        verse = default;
        var text = value.Trim();
        var dot = text.IndexOf('.');
        var colon = text.IndexOf(':', dot + 1);
        if (dot <= 0 || colon != dot + 2 || !char.IsAsciiLetterUpper(text[dot + 1]) ||
            !BookCodes.TryGetOrdinal(text[..dot], out var book) ||
            !int.TryParse(text.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        verse = new AdditionVerse(book, text[dot + 1], number);
        return true;
    }
}

/// <summary>
/// Where an edition's verses stand, in passages it numbers in a way of its own that the
/// versification data does not describe, read verse against verse: the lettered verses of the Greek
/// Esther, the Letter of Jeremiah, the Song of the Three, Brenton's and the Ge'ez's Sirach and the
/// Prayer of Manasseh, as <c>Resources/Essenthos/versification/edition-readings.tsv</c> writes them
/// with what was read for each. Those are this project's reading, made by comparing the editions word
/// for word.
///
/// The versification data places the Greek Esther by the letters of the edition it describes, and
/// neither Greek edition here letters them so. The additions are the same verses in every edition,
/// though, and the data says where each of them stands, so what is written down is only which verse
/// of which addition each lettered verse prints.
/// </summary>
internal sealed class LetteredEditions
{
    /// <summary>One standard verse a row names: an addition to Esther, placed by the data, or an address.</summary>
    private readonly record struct Standard(AdditionVerse? Addition, CanonicalReference Address);

    /// <param name="Verses">The edition's verses the row names, in order.</param>
    /// <param name="Prints">The standard verses they print, in order, the one printed most of first.</param>
    private sealed record Row(int Line, IReadOnlyList<PrintedAddress> Verses, IReadOnlyList<Standard> Prints);

    /// <param name="Tests">What tells the edition apart, in the versification data's own form.</param>
    private sealed record Edition(string Name, VersificationConditions Tests, List<Row> Rows);

    private readonly IReadOnlyList<Edition> _all;

    private LetteredEditions(IReadOnlyList<Edition> all) => _all = all;

    /// <summary>
    /// The places of the lettered verses of whichever of these editions this one is, or nothing.
    /// </summary>
    public IReadOnlyDictionary<PrintedAddress, IReadOnlyList<CanonicalReference>>? For(
        EditionShape edition,
        IReadOnlyDictionary<AdditionVerse, CanonicalReference> additions)
    {
        var matched = _all.Where(candidate => candidate.Tests.Answer(edition) is true).ToList();
        if (matched.Count == 0)
        {
            return null;
        }

        var placements = new Dictionary<PrintedAddress, IReadOnlyList<CanonicalReference>>();
        foreach (var candidate in matched)
        {
            foreach (var row in candidate.Rows)
            {
                var prints = row.Prints.Select(standard => Place(candidate, standard, additions)).ToList();
                foreach (var (verse, placed) in Pair(candidate, row, prints))
                {
                    placements[verse] = placed;
                }
            }
        }

        return placements;
    }

    /// <summary>
    /// A run against a run of the same length is verse against verse; one verse against several prints
    /// all of them, and several verses against one put each of them there.
    /// </summary>
    private static IEnumerable<(PrintedAddress, IReadOnlyList<CanonicalReference>)> Pair(
        Edition edition,
        Row row,
        IReadOnlyList<CanonicalReference> prints)
    {
        if (row.Verses.Count == 1)
        {
            return [(row.Verses[0], prints)];
        }

        if (row.Verses.Count == prints.Count)
        {
            return row.Verses.Select((verse, index) => (verse, (IReadOnlyList<CanonicalReference>)[prints[index]]));
        }

        return prints.Count == 1
            ? row.Verses.Select(verse => (verse, prints))
            : throw new FormatException(
                $"edition-readings.tsv row {row.Line}: {edition.Name}'s {row.Verses.Count} verses against " +
                $"{prints.Count} standard ones are neither the same length nor a single verse on either side.");
    }

    private static CanonicalReference Place(
        Edition edition,
        Standard standard,
        IReadOnlyDictionary<AdditionVerse, CanonicalReference> additions)
    {
        if (standard.Addition is not { } addition)
        {
            return standard.Address;
        }

        return additions.TryGetValue(addition, out var placed)
            ? placed
            : throw new InvalidOperationException(
                $"{edition.Name}'s Esther prints the addition verse {addition.Section}:{addition.Verse}, which the " +
                "versification data does not place. The data names the additions A to F in the Latin rows of its " +
                "Esther passages; check that TVTMS.txt is the release those rows were read from.");
    }

    public static LetteredEditions Read(VersificationData data)
    {
        var editions = new List<Edition>();

        // The tests each edition's first row states, which every other row of it has to repeat.
        var stated = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in data.Rows(VersificationData.EditionReadingsProduct,
                     "edition", "source", "standard", "tests", "why"))
        {
            var name = line.Required("edition");
            var tests = line.Required("tests");
            var edition = editions.Find(candidate => candidate.Name == name);
            if (edition is null)
            {
                edition = new Edition(
                    name,
                    VersificationTest.ParseAll(tests) ?? throw line.Fail(
                        $"the tests \"{tests}\" say nothing this corpus can answer; write them as the versification " +
                        "data does, as in Bar.6:72=Last"),
                    []);
                editions.Add(edition);
                stated[name] = tests;
            }
            else if (stated[name] != tests)
            {
                throw line.Fail($"{name}'s tests are \"{stated[name]}\" above and \"{tests}\" here");
            }

            var verses = Verses(line);
            edition.Rows.Add(new Row(line.Row, verses, Standards(line, inTheBook: verses[0].Address.Book == SongOfTheThreeBook.Book)));
        }

        return new LetteredEditions(editions);
    }

    /// <summary>
    /// <c>Est.1:1b</c>, <c>Est.4:1a1</c>, a run with one letter <c>Est.1:1a-17a</c>, or <c>Sir.30:27-40</c>; the
    /// letter is whatever follows the verse's number. <c>S3Y</c> is the Song of the Three printed as a book.
    /// </summary>
    private static List<PrintedAddress> Verses(VersificationLine line)
    {
        var cell = line.Required("source");
        var dot = cell.IndexOf('.');
        var colon = cell.IndexOf(':', dot + 1);
        var code = dot > 0 ? cell[..dot] : string.Empty;
        var book = code == SongOfTheThree.Code ? SongOfTheThreeBook.Book
            : BookCodes.TryGetOrdinal(code, out var ordinal) ? ordinal
            : throw line.Fail($"\"{cell}\" names no book of the versification data");
        if (colon < 0 ||
            !int.TryParse(cell.AsSpan(dot + 1, colon - dot - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var chapter))
        {
            throw line.Fail($"\"{cell}\" is not book.chapter:verse");
        }

        var range = cell[(colon + 1)..].Split('-');
        var (first, label) = Numbered(line, range[0]);
        var (last, ending) = range.Length == 2 ? Numbered(line, range[1]) : (first, label);
        if (range.Length > 2 || ending != label || last < first)
        {
            throw line.Fail($"\"{cell}\" is not one verse or a run of verses with one letter");
        }

        return [.. Enumerable.Range(first, last - first + 1).Select(verse =>
            new PrintedAddress(new CanonicalReference(book, chapter, verse), label))];
    }

    private static (int Verse, string Label) Numbered(VersificationLine line, string written)
    {
        var digits = written.TakeWhile(char.IsAsciiDigit).Count();
        var label = written[digits..];
        return digits > 0 && (label.Length == 0 || char.IsAsciiLetterLower(label[0]))
            ? (int.Parse(written.AsSpan(0, digits), CultureInfo.InvariantCulture), label)
            : throw line.Fail($"\"{written}\" is not a verse number with an optional letter");
    }

    /// <summary>
    /// Pieces separated by <c>;</c>: an addition to Esther or a run of one (<c>Est.D:12-16</c>), the song's
    /// verses (<c>S3Y.1:29-30</c>), or verses in the data's notation (<c>Sir.33:18-31</c>). The song's verses
    /// stand in Daniel 3, except for a verse of an edition that prints the song as a book of its own
    /// (<paramref name="inTheBook"/>), whose verses stand in that book.
    /// </summary>
    private static List<Standard> Standards(VersificationLine line, bool inTheBook)
    {
        var standards = new List<Standard>();
        foreach (var piece in line.Required("standard").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var dash = piece.IndexOf('-');
            if (AdditionVerse.TryParse(dash < 0 ? piece : piece[..dash], out var addition))
            {
                var last = dash < 0 ? addition.Verse
                    : int.TryParse(piece.AsSpan(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var end) ? end
                    : throw line.Fail($"\"{piece}\" is not a run of an addition's verses");
                standards.AddRange(Enumerable.Range(addition.Verse, last - addition.Verse + 1)
                    .Select(verse => new Standard(addition with { Verse = verse }, default)));
                continue;
            }

            IReadOnlyList<CanonicalReference> references = !SongOfTheThree.TryParseAll(piece, out var song) ? CanonicalReference.ParseAll(piece)
                : inTheBook ? [.. song.Select(verse => new CanonicalReference(SongOfTheThreeBook.Book,
                    SongOfTheThreeBook.Chapter, verse.Verse - SongOfTheThreeBook.DanielVerses))]
                : song;
            if (references.Count == 0)
            {
                throw line.Fail($"\"{piece}\" is not a verse the versification data could hold");
            }

            standards.AddRange(references.Select(reference => new Standard(null, reference)));
        }

        return standards;
    }
}
