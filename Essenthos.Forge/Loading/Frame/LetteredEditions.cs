using System.Globalization;

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
/// versification data does not describe, read verse against verse.
///
/// The versification data places the Greek Esther by the letters of the edition it describes —
/// <c>1:1a</c> to <c>1:1r</c> for Mordecai's dream, thirty pieces at 4:17 for his prayer and Esther's —
/// and neither Greek edition here letters them so. The additions are the same verses in every
/// edition, though, and the data says where each of them stands, so what has to be written down is
/// only which verse of which addition each lettered verse prints. Those are this project's reading,
/// made by comparing the two Greek editions word for word.
///
/// <para>
/// Brenton prints the first verse of the dream as 1:1 and continues it as 1:1b to 1:1r, skipping
/// <em>j</em>, so that the verse the Hebrew calls 1:1 is his 1:1s. He runs some verses of the prayers
/// and of the king's letters together — his 4:17c is the prayer's third and fourth verse — and lets
/// 5:1 and 5:2 carry the opening of Esther's audience with the king, which the Greek writes in place
/// of the Hebrew's two verses. His English numbers every verse as his Greek does.
/// </para>
/// <para>
/// Swete numbers each addition from one and marks it with a letter in the chapter it follows: 1:1a
/// to 1:17a are the dream, 4:1a to 4:30a the prayers and 4:1b to 4:16b the audience, with the
/// prayers' eleventh verse printed as 4:1a1. At the end of the book the letter is on the other side:
/// 10:1a to 10:3a are the Hebrew's last three verses, and the unlettered 10:1 to 10:11 are the
/// interpretation of the dream and the colophon.
/// </para>
/// <para>
/// Swete prints the Letter of Jeremiah in seventy-two verses where the standard numbering, which
/// Brenton and the English Bibles follow in Baruch 6, has seventy-three: his first verse is the
/// title and the letter's opening, and he runs 15-16 into one verse, 43 into 42 and 50-51 into one,
/// which leaves most of his verses one behind the standard's.
/// </para>
/// </summary>
internal static class LetteredEditions
{
    private const int Esther = 17;

    private const int LetterOfJeremiah = 76;

    private const char None = '\0';

    /// <param name="Tests">What tells the edition apart, in the versification data's own form.</param>
    /// <param name="Verses">
    /// Each lettered verse of Esther with what it prints: <c>A:2</c> for a verse of an addition,
    /// <c>1:1</c> for a verse of the book itself, several where the edition runs them together.
    /// </param>
    /// <param name="Elsewhere">Verses of other books, each with the standard verses it prints.</param>
    private sealed record Edition(
        string Name,
        string Tests,
        IReadOnlyList<(int Chapter, int Verse, string Label, string[] Prints)> Verses,
        IReadOnlyList<(CanonicalReference Verse, CanonicalReference[] Prints)>? Elsewhere = null);

    private static readonly Edition Brenton = new(
        "Brenton",
        "Est.1:1.18=Exist & Est.4:17.25=Exist & Est.10:3.12=Exist",
        [
            (1, 1, string.Empty, ["A:1"]),
            .. Run(1, 1, "bcdefghiklmnopqr", 'A', 2),
            (1, 1, "s", ["1:1"]),
            .. Run(3, 13, "abcdefg", 'B', 1),
            (4, 17, "a", ["C:1"]),
            (4, 17, "b", ["C:2"]),
            (4, 17, "c", ["C:3", "C:4"]),
            (4, 17, "d", ["C:5", "C:6"]),
            .. Run(4, 17, "efghi", 'C', 7),
            (4, 17, "k", ["C:12", "C:13"]),
            (4, 17, "l", ["C:14", "C:15"]),
            (4, 17, "m", ["C:16"]),
            (4, 17, "n", ["C:17", "C:18"]),
            (4, 17, "o", ["C:19", "C:20"]),
            .. Run(4, 17, "pqrstuwxyz", 'C', 21),
            (5, 1, string.Empty, ["5:1", "D:1"]),
            (5, 1, "a", ["D:2", "D:3", "D:4"]),
            .. Run(5, 1, "bcde", 'D', 5),
            (5, 1, "f", ["D:9", "D:10", "D:11"]),
            (5, 2, string.Empty, ["5:2", "D:12"]),
            (5, 2, "a", ["D:13", "D:14"]),
            (5, 2, "b", ["D:15", "D:16"]),
            (8, 12, "a", ["E:1"]),
            (8, 12, "b", ["E:1"]),
            (8, 12, "c", ["E:2", "E:3"]),
            .. Run(8, 12, "defghiklmnopq", 'E', 4),
            (8, 12, "r", ["E:17", "E:18"]),
            (8, 12, "s", ["E:19", "E:20"]),
            (8, 12, "t", ["E:21"]),
            (8, 12, "u", ["E:22", "E:23"]),
            (8, 12, "x", ["E:24"]),
            .. Run(10, 3, "abcdefghikl", 'F', 1),
        ]);

    private static readonly Edition Swete = new(
        "Swete",
        "Est.4:1.4=Exist & Est.5:1=NotExist & Est.1:17.2=Exist",
        [
            .. Numbered(1, "a", 'A', 1, 17),
            .. Numbered(3, "a", 'B', 1, 7),
            .. Numbered(4, "a", 'C', 1, 30).Where(verse => verse.Verse != 11),
            (4, 1, "a1", ["C:11"]),
            .. Numbered(4, "b", 'D', 1, 16),
            .. Numbered(8, "a", 'E', 1, 24),
            .. Enumerable.Range(1, 3).Select(verse => (10, verse, "a", new[] { $"10:{verse}" })),
            .. Numbered(10, string.Empty, 'F', 1, 11),
        ],
        [.. SweteLetterOfJeremiah()]);

    private static readonly Edition[] All = [Brenton, Swete];

    /// <summary>
    /// The places of the lettered verses of whichever of these editions this one is, or nothing.
    /// </summary>
    public static IReadOnlyDictionary<PrintedAddress, IReadOnlyList<CanonicalReference>>? For(
        EditionShape edition,
        IReadOnlyDictionary<AdditionVerse, CanonicalReference> additions)
    {
        var matched = All.Where(candidate => VersificationTest.ParseAll(candidate.Tests)?.Answer(edition) is true)
            .ToList();
        if (matched.Count == 0)
        {
            return null;
        }

        var placements = new Dictionary<PrintedAddress, IReadOnlyList<CanonicalReference>>();
        foreach (var candidate in matched)
        {
            foreach (var (chapter, verse, label, prints) in candidate.Verses)
            {
                placements[new PrintedAddress(new CanonicalReference(Esther, chapter, verse), label)] =
                    [.. prints.Select(printed => Place(candidate, printed, additions))];
            }

            foreach (var (verse, prints) in candidate.Elsewhere ?? [])
            {
                placements[new PrintedAddress(verse, string.Empty)] = prints;
            }
        }

        return placements;
    }

    private static CanonicalReference Place(
        Edition edition,
        string printed,
        IReadOnlyDictionary<AdditionVerse, CanonicalReference> additions)
    {
        var colon = printed.IndexOf(':');
        var number = int.Parse(printed.AsSpan(colon + 1), CultureInfo.InvariantCulture);
        var section = char.IsAsciiLetter(printed[0]) ? printed[0] : None;
        if (section == None)
        {
            return new CanonicalReference(Esther, int.Parse(printed.AsSpan(0, colon), CultureInfo.InvariantCulture),
                number);
        }

        return additions.TryGetValue(new AdditionVerse(Esther, section, number), out var standard)
            ? standard
            : throw new InvalidOperationException(
                $"{edition.Name}'s Esther prints the addition verse {section}:{number}, which the versification " +
                "data does not place. The data names the additions A to F in the Latin rows of its Esther " +
                "passages; check that TVTMS.txt is the release those rows were read from.");
    }

    /// <summary>
    /// Swete's Letter of Jeremiah against the standard's verses, which is where he stands one behind:
    /// read against Brenton's Greek, which is numbered as the standard is.
    /// </summary>
    private static IEnumerable<(CanonicalReference, CanonicalReference[])> SweteLetterOfJeremiah()
    {
        const int Verses = 72;
        var joined = new Dictionary<int, int[]>
        {
            [1] = [1, 2],
            [14] = [15, 16],
            [15] = [17],
            [16] = [17],
            [42] = [43],
            [43] = [43],
            [50] = [50, 51],
        };

        for (var verse = 1; verse <= Verses; verse++)
        {
            var standard = joined.TryGetValue(verse, out var listed) ? listed
                : verse is >= 44 and <= 49 ? [verse]
                : [verse + 1];
            yield return (new CanonicalReference(LetterOfJeremiah, 1, verse),
                [.. standard.Select(number => new CanonicalReference(LetterOfJeremiah, 1, number))]);
        }
    }

    /// <summary>Consecutive letters at one address printing consecutive verses of an addition.</summary>
    private static IEnumerable<(int, int, string, string[])> Run(
        int chapter,
        int verse,
        string letters,
        char section,
        int first) =>
        letters.Select((letter, index) =>
            (chapter, verse, letter.ToString(), new[] { $"{section}:{first + index}" }));

    /// <summary>An addition numbered from one under a chapter of the book and marked by a letter.</summary>
    private static IEnumerable<(int Chapter, int Verse, string Label, string[] Prints)> Numbered(
        int chapter,
        string label,
        char section,
        int first,
        int last) =>
        Enumerable.Range(first, last - first + 1)
            .Select(verse => (chapter, verse, label, new[] { $"{section}:{verse}" }));
}
