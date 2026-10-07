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
/// which leaves most of his verses one behind the standard's. The Synodal and the Vulgate print
/// seventy-two as well, each divided in a way of its own that the data's Latin and Greek columns do
/// not describe, and each was read verse against verse in the same way.
/// </para>
/// <para>
/// In the Song of the Three both follow the order of Vaticanus, which is not the order the data's
/// Greek and Latin columns number: the depths come before the throne and the heavens before the
/// angels, and where the Latin has six lines of weather both have four in another order. Brenton
/// letters the last two of his as 3:72a and 3:72b, and has the seas before the springs. Each of
/// their verses is placed by the verse of the song it prints.
/// </para>
/// <para>
/// Brenton prints Sirach 30:25-36:16 in the order of the Greek manuscripts, where two quires of their
/// archetype were bound the wrong way round, under their own chapter numbers: his 30:25-40 is the
/// standard's 33:16-31, his 31-33 its 34, 35 and 36:1-11, his 34-36 its 31, 32 and 33:1-15, and a
/// verse at each seam prints the halves of two. His English is the King James's Apocrypha divided
/// as his Greek is, which is how it was read against the standard, verse for verse; where his
/// Greek divides otherwise, as at 33:7, it was read against Swete. Swete prints those chapters in
/// the standard's order but numbers 34-36 as the NRSV does, which the data's Sirach passages map,
/// and gives the Greek manuscripts' verse numbers to 33:16-31.
/// </para>
/// </summary>
internal static class LetteredEditions
{
    private const int Esther = 17;

    private const int LetterOfJeremiah = 76;

    private const int Baruch = 67;

    private const int PrayerOfManasseh = 79;

    private const int Sirach = 72;

    private const int Daniel = 27;

    private const int Genesis = 1;

    private const int Exodus = 2;

    private const int FirstChronicles = 13;

    private const int Nehemiah = 16;

    private const int Psalms = 19;

    private const char None = '\0';

    /// <param name="Tests">What tells the edition apart, in the versification data's own form.</param>
    /// <param name="Verses">
    /// Each lettered verse of Esther with what it prints: <c>A:2</c> for a verse of an addition,
    /// <c>1:1</c> for a verse of the book itself, several where the edition runs them together.
    /// </param>
    /// <param name="Elsewhere">
    /// Verses of other books, each with the standard verses it prints, the one it prints most of first.
    /// </param>
    private sealed record Edition(
        string Name,
        string Tests,
        IReadOnlyList<(int Chapter, int Verse, string Label, string[] Prints)> Verses,
        IReadOnlyList<(PrintedAddress Verse, CanonicalReference[] Prints)>? Elsewhere = null);

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
        ],
        [
            Song(54, 32), Song(55, 33), Song(58, 36), Song(59, 37), Song(72, 45, "a"), Song(72, 50, "b"),
            Song(77, 56), Song(78, 55),
            Printed(Sirach, 30, 13, "a", (30, 11)),
            Printed(Sirach, 30, 13, "b", (30, 12)),
            Printed(Sirach, 30, 25, (33, 16)),
            Printed(Sirach, 30, 26, (33, 17), (33, 16)),
            .. Moved(Sirach, 30, 27, 40, 33, 18),
            .. Moved(Sirach, 31, 1, 26, 34, 1),
            .. Moved(Sirach, 32, 1, 20, 35, 1),
            .. Moved(Sirach, 33, 1, 11, 36, 1),
            Printed(Sirach, 33, 12, (30, 25)),
            .. Moved(Sirach, 34, 1, 31, 31, 1),
            .. Moved(Sirach, 35, 1, 24, 32, 1),
            .. Moved(Sirach, 36, 1, 15, 33, 1),
            Printed(Sirach, 36, 16, (36, 11), (33, 16)),
            .. Moved(Sirach, 36, 17, 31, 36, 12),
            .. NextVerseLettered(),
        ]);

    /// <summary>
    /// Five verses eBible prints with the letter <c>a</c> after the verse before them, in Brenton's Greek and
    /// his English alike, where the standard numbering and the Hebrew have the same words as the next verse
    /// and the edition no verse of that number: Laban's heap and pillar as witness (Genesis 31:51), the oil
    /// for the light (Exodus 25:6), declaring his glory among the nations (1 Chronicles 16:24), Melatiah the
    /// Gibeonite at the wall (Nehemiah 3:7) and the vows paid before all his people (Psalm 116:14, the
    /// Greek's 115:5). Each stands where its words do, not at the verse whose number it borrows.
    /// </summary>
    private static IEnumerable<(PrintedAddress, CanonicalReference[])> NextVerseLettered() =>
    [
        Printed(Genesis, 31, 50, "a", (31, 51)),
        Printed(Exodus, 25, 5, "a", (25, 6)),
        Printed(FirstChronicles, 16, 23, "a", (16, 24)),
        Printed(Nehemiah, 3, 6, "a", (3, 7)),
        Printed(Psalms, 115, 4, "a", (116, 14)),
    ];

    /// <summary>
    /// Brenton's Greek where it divides otherwise than his English: its Sirach 33:7 runs the end of the
    /// standard's 36:6 into 36:7, which his English leaves in 33:6. The English prints 1 Samuel
    /// 17:12-31, which his Greek leaves out with Vaticanus.
    /// </summary>
    private static readonly Edition BrentonsGreek = new(
        "Brenton's Greek",
        $"{Brenton.Tests} & 1Sa.17:12=NotExist",
        [],
        [Printed(Sirach, 33, 7, (36, 7), (36, 6))]);

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
        [
            .. SweteLetterOfJeremiah(),
            Song(54, 32), Song(55, 33), Song(58, 36), Song(59, 37), Song(67, 47), Song(68, 48), Song(69, 45),
            Song(70, 50),
            Printed(Sirach, 30, 13, "b", (30, 25)),
            Printed(Sirach, 33, 16, "a", (33, 16)),
            Printed(Sirach, 33, 25, (33, 16)),
            .. Renumbered(Sirach, 33, 26, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 27),
            Printed(Sirach, 33, 38, (33, 28), (33, 29)),
            Printed(Sirach, 33, 39, (33, 30), (33, 31)),
            Printed(Sirach, 33, 40, (33, 31)),
            .. Renumbered(Sirach, 34, 10,
                10, 10, 11, 12, 13, 13, 14, 15, 15, 16, 17, 18, 18, 19, 20, 21, 22, 22, 23, 24, 25, 26),
            .. Renumbered(Sirach, 35, 1,
                1, 1, 2, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 12, 13, 14, 15, 15, 16, 17, 18, 18, 19, 19, 20),
            .. Renumbered(Sirach, 36, 7, 6, 7, 7, 8, 9, 10),
            Printed(Sirach, 36, 13, "a", (36, 11)),
            Printed(Sirach, 36, 16, "b", (36, 11)),
            .. Moved(Sirach, 36, 17, 21, 36, 12),
            Printed(Sirach, 36, 22, (36, 17)),
            .. Moved(Sirach, 36, 23, 31, 36, 18),
        ]);

    /// <summary>
    /// The church's Ge'ez, which has the Latin's six lines of weather in the song but in an order of
    /// its own, and the depths and the heavens first as Vaticanus has them. Its Daniel keeps Susanna
    /// and Bel as chapters 13 and 14, which tells it apart from the Latin texts that also end Daniel 3
    /// at verse 100. The line it prints as 3:71 is placed by elimination: the others name night and
    /// day, dew, cold and frost, light and darkness, and hail.
    /// </summary>
    private static readonly Edition Geez = new(
        "Ge'ez",
        "Dan.3:100=Last & Dan.13:1=Exist",
        [],
        [
            Song(54, 32), Song(55, 33), Song(58, 36), Song(59, 37), Song(67, 47), Song(70, 48), Song(71, 45),
            Song(72, 50),
            .. GeezSirach(),
        ]);

    /// <summary>
    /// The Ge'ez Sirach 30:25-36:31, which keeps the Greek manuscripts' order of the chapters, as
    /// Brenton prints them, and divides every one of them verse for verse as Swete does: its 30:25-40
    /// are Swete's 33:25-40, its 31, 32 and 33 Swete's 34, 35 and 36:1-13, its 34 and 35 Swete's 31
    /// and 32, and its 36 Swete's 33:1-16 and 36:17-31. Each chapter counts exactly as Swete's does,
    /// and the lengths of its verses follow Swete's there as closely as in the chapters both print
    /// in one order, and not at all at the rows its own numbers name. Each verse stands where the
    /// verse of Swete's it prints stands.
    /// </summary>
    private static IEnumerable<(PrintedAddress, CanonicalReference[])> GeezSirach()
    {
        var swete = (Swete.Elsewhere ?? [])
            .Where(read => read.Verse.Address.Book == Sirach)
            .ToDictionary(read => read.Verse, read => read.Prints);

        (int Geez, int Swete, int First, int Last)[] chapters =
        [
            (30, 33, 25, 40), (31, 34, 1, 31), (32, 35, 1, 26), (33, 36, 1, 13), (34, 31, 1, 31),
            (35, 32, 1, 24), (36, 33, 1, 16), (36, 36, 17, 31),
        ];

        foreach (var (geez, printed, first, last) in chapters)
        {
            for (var verse = first; verse <= last; verse++)
            {
                var own = new PrintedAddress(new CanonicalReference(Sirach, printed, verse), string.Empty);
                var lettered = new PrintedAddress(own.Address, "a");
                var standard = swete.TryGetValue(own, out var read) ? read
                    : swete.TryGetValue(lettered, out var half) ? half
                    : [own.Address];
                yield return (new PrintedAddress(new CanonicalReference(Sirach, geez, verse), string.Empty), standard);
            }
        }
    }

    /// <summary>
    /// The Synodal as Russian Wikisource transcribes its non-canonical books and the Greek additions it
    /// prints inside canonical ones: the Letter of Jeremiah in seventy-two verses of its own division;
    /// the additions to Esther it sets in brackets inside six verses, each the lettered piece of its
    /// verse and each the whole of an addition, the audience divided between 5:1 and 5:2 as Brenton
    /// divides it; its 3:52, which holds the song's 29 and 30; and the Prayer of Manasseh in twelve
    /// verses, read against the King James's fifteen. Its Esther prints 5:1, which tells it from Swete's.
    /// </summary>
    private static readonly Edition Synodal = new(
        "Synodal",
        "LJe.1:72=Last & Est.5:1=Exist",
        [
            Whole(1, 1, 'A', 1, 17),
            Whole(3, 13, 'B', 1, 7),
            Whole(4, 17, 'C', 1, 30),
            Whole(5, 1, 'D', 1, 11),
            Whole(5, 2, 'D', 12, 16),
            Whole(8, 12, 'E', 1, 24),
            Whole(10, 3, 'F', 1, 11),
        ],
        [
            .. SynodalLetterOfJeremiah(),
            Printed(SongOfTheThreeBook.Book, 1, 29, (1, 29), (1, 30)),
            Printed(PrayerOfManasseh, 1, 1, (1, 1)),
            Printed(PrayerOfManasseh, 1, 2, (1, 3), (1, 2), (1, 4), (1, 5)),
            Printed(PrayerOfManasseh, 1, 3, (1, 5)),
            Printed(PrayerOfManasseh, 1, 4, (1, 6)),
            Printed(PrayerOfManasseh, 1, 5, (1, 7)),
            Printed(PrayerOfManasseh, 1, 6, (1, 7), (1, 8)),
            Printed(PrayerOfManasseh, 1, 7, (1, 8), (1, 9)),
            Printed(PrayerOfManasseh, 1, 8, (1, 9), (1, 10)),
            Printed(PrayerOfManasseh, 1, 9, (1, 10)),
            Printed(PrayerOfManasseh, 1, 10, (1, 10), (1, 11)),
            Printed(PrayerOfManasseh, 1, 11, (1, 13), (1, 12), (1, 14), (1, 15)),
            Printed(PrayerOfManasseh, 1, 12, (1, 15)),
        ]);

    /// <summary>The Clementine Vulgate and the Douay-Rheims made from it, which print the letter as Baruch 6.</summary>
    private static readonly Edition Vulgate = new(
        "Vulgate",
        "Bar.6:72=Last",
        [],
        [.. VulgateLetterOfJeremiah()]);

    private static readonly Edition[] All = [Brenton, BrentonsGreek, Swete, Geez, Synodal, Vulgate];

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
                placements[verse] = prints;
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
    private static IEnumerable<(PrintedAddress, CanonicalReference[])> SweteLetterOfJeremiah() =>
        LetterRead(LetterOfJeremiah, 1, verses: 72, onStandard: verse => verse is >= 44 and <= 49, new()
        {
            [1] = [1, 2],
            [14] = [15, 16],
            [15] = [17],
            [16] = [17],
            [42] = [43],
            [43] = [43],
            [50] = [50, 51],
        });

    /// <summary>
    /// The Synodal's Letter of Jeremiah, read against Brenton's English, which is numbered as the
    /// standard is: the Synodal runs 5-6, 15-16 and 50-51 into one verse each and divides 17 and 43,
    /// and carries a clause over a verse's end at 25, 39-40, 46-47 and 53-54.
    /// </summary>
    private static IEnumerable<(PrintedAddress, CanonicalReference[])> SynodalLetterOfJeremiah() =>
        LetterRead(LetterOfJeremiah, 1, verses: 72, onStandard: verse => verse is <= 4 or (>= 44 and <= 49), new()
        {
            [5] = [5, 6],
            [14] = [15, 16],
            [15] = [17],
            [16] = [17],
            [25] = [26, 27],
            [39] = [40],
            [40] = [41, 40],
            [42] = [43],
            [43] = [43],
            [46] = [46],
            [47] = [47, 46],
            [50] = [50, 51],
            [53] = [54],
            [54] = [55, 54],
        });

    /// <summary>
    /// The Vulgate's Baruch 6, in the Clementine and the Douay-Rheims alike, read against the King
    /// James: it prints the letter's title as a heading and not as a verse, so its first verse is the
    /// standard's second, and it divides the verses around 9, 12, 14-19, 25 and 40-43 otherwise.
    /// </summary>
    private static IEnumerable<(PrintedAddress, CanonicalReference[])> VulgateLetterOfJeremiah() =>
        LetterRead(Baruch, 6, verses: 72, onStandard: verse => verse is >= 44 and <= 49, new()
        {
            [5] = [6, 5],
            [9] = [10, 9],
            [12] = [13, 12],
            [14] = [15, 16],
            [15] = [17, 16],
            [16] = [17],
            [18] = [19, 20],
            [19] = [20],
            [25] = [26, 27],
            [39] = [40],
            [40] = [41, 40],
            [41] = [42, 41],
            [42] = [43],
            [43] = [43],
            [50] = [50, 51],
            [56] = [57, 58],
        });

    /// <summary>
    /// An edition's Letter of Jeremiah against the standard's seventy-three verses: each verse listed
    /// prints those, the one it prints most of first, and every other verse prints the standard's
    /// next, or its own number where <paramref name="onStandard"/> says the edition is level there.
    /// </summary>
    private static IEnumerable<(PrintedAddress, CanonicalReference[])> LetterRead(
        int book,
        int chapter,
        int verses,
        Func<int, bool> onStandard,
        Dictionary<int, int[]> listed)
    {
        for (var verse = 1; verse <= verses; verse++)
        {
            var standard = listed.TryGetValue(verse, out var read) ? read
                : onStandard(verse) ? [verse]
                : [verse + 1];
            yield return (new PrintedAddress(new CanonicalReference(book, chapter, verse), string.Empty),
                [.. standard.Select(number => new CanonicalReference(book, chapter, number))]);
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

    /// <summary>A lettered piece of a verse of Esther holding the whole of one addition's verses.</summary>
    private static (int, int, string, string[]) Whole(int chapter, int verse, char section, int first, int last) =>
        (chapter, verse, "a", [.. Enumerable.Range(first, last - first + 1).Select(number => $"{section}:{number}")]);

    /// <summary>An addition numbered from one under a chapter of the book and marked by a letter.</summary>
    private static IEnumerable<(int Chapter, int Verse, string Label, string[] Prints)> Numbered(
        int chapter,
        string label,
        char section,
        int first,
        int last) =>
        Enumerable.Range(first, last - first + 1)
            .Select(verse => (chapter, verse, label, new[] { $"{section}:{verse}" }));

    /// <summary>A verse of Daniel 3 and the verse of the song it prints, in the standard's numbering.</summary>
    private static (PrintedAddress, CanonicalReference[]) Song(int verse, int standard, string label = "") =>
        (new PrintedAddress(new CanonicalReference(Daniel, 3, verse), label), [SongOfTheThree.Verse(standard)]);

    /// <summary>A verse and the verses of the same book it prints, as chapter and verse.</summary>
    private static (PrintedAddress, CanonicalReference[]) Printed(
        int book,
        int chapter,
        int verse,
        params (int Chapter, int Verse)[] prints) =>
        Printed(book, chapter, verse, string.Empty, prints);

    private static (PrintedAddress, CanonicalReference[]) Printed(
        int book,
        int chapter,
        int verse,
        string label,
        params (int Chapter, int Verse)[] prints) =>
        (new PrintedAddress(new CanonicalReference(book, chapter, verse), label),
            [.. prints.Select(printed => new CanonicalReference(book, printed.Chapter, printed.Verse))]);

    /// <summary>Consecutive verses standing at as many consecutive verses of another chapter.</summary>
    private static IEnumerable<(PrintedAddress, CanonicalReference[])> Moved(
        int book,
        int chapter,
        int first,
        int last,
        int toChapter,
        int toFirst) =>
        Enumerable.Range(first, last - first + 1)
            .Select(verse => Printed(book, chapter, verse, (toChapter, toFirst + verse - first)));

    /// <summary>The verses of a chapter from <paramref name="first"/> on, each at the verse listed for it.</summary>
    private static IEnumerable<(PrintedAddress, CanonicalReference[])> Renumbered(
        int book,
        int chapter,
        int first,
        params int[] standard) =>
        standard.Select((to, index) => Printed(book, chapter, first + index, (chapter, to)));
}
