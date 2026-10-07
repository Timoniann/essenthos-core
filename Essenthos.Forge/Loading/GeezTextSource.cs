using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;

namespace Essenthos.Core.Loading;

/// <summary>
/// The Ethiopic Bible in Ge'ez, all eighty-one books of the Ethiopian Orthodox Tewahedo canon, from
/// the TEI files of Beta maṣāḥǝft in Hamburg.
///
/// <para>
/// **It is one witness assembled from two kinds of source, and each book says which.** For
/// sixty-six books the file carries the text of the church's printed Bible, typed for Beta maṣāḥǝft
/// in 2025. For fifteen it carries an older digitisation of a nineteenth-century scholarly edition:
/// Dillmann's Octateuch and Books of Kingdoms and Ludolf's Psalter as Ran HaCohen typed them, and
/// Dillmann's Wisdom and 4 Baruch as Michal Jerabek typed them. Those are the fifteen the files hold
/// as verses; Enoch has Dillmann's text only as whole chapters beside the church's in verses, and for
/// Sirach, Amos, Joel and Obadiah the files hold the church's text alone. Where a file holds a
/// copyrighted edition beside the church's — VanderKam's Jubilees, Mercer's Ecclesiastes — the
/// church's is the one read.
/// </para>
///
/// <para>
/// The Old Testament was translated from the Septuagint, so the edition numbers as the Greek does
/// in most books and the frame's own tests decide the rest passage by passage. Three books are
/// divided in a way of their own that no mapping to another text's verses has been established for —
/// Esther, the Letter of Jeremiah and Dillmann's Wisdom — so they are placed at their own numbers
/// and joined to no other text verse by verse. Enoch, Jubilees, the three books of Meqabyan and 4
/// Baruch have no counterpart in the corpus at all, and Proverbs stands here as the two books the
/// church counts, Messale and Tägsas, which answer to Proverbs by chapter and not yet by verse.
/// </para>
///
/// <para>
/// **Its identifier is this project's own**, <c>GEEZ81</c>. CrossWire serves HaCohen's Octateuch and
/// Psalter as <c>Geez</c>, which is sixteen of these books and not this text.
/// </para>
/// </summary>
internal static class GeezTextSource
{
    public const string Slug = Sources.GeezSlug;

    public const string Folder = "BetaMasaheft";

    /// <summary>Where each book's words come from, which is what a reader has to be told about it.</summary>
    internal enum Origin
    {
        /// <summary>The church's printed Bible, typed for Beta maṣāḥǝft.</summary>
        ChurchPrintedBible,

        /// <summary>Dillmann's edition (1853-1871) as Ran HaCohen digitised it.</summary>
        DillmannByHaCohen,

        /// <summary>Ludolf's Psalter (1701) as Ran HaCohen digitised it.</summary>
        LudolfByHaCohen,

        /// <summary>Dillmann's edition as Michal Jerabek typed it for his Library of Ethiopian Texts (1995).</summary>
        DillmannByJerabek,
    }

    /// <param name="Edition">The edition's <c>xml:id</c> where the file holds more than one.</param>
    /// <param name="ChapterShift">
    /// What the file's chapter numbers are moved by to stand at the frame's: 3 Ezra and Ezra Sutuel
    /// leave their first chapter empty and begin at 2.
    /// </param>
    /// <param name="OwnDivision">
    /// Divided in a way no versification scheme describes, so the frame's rows, which place it at its
    /// own numbers, are not where its verses answer the Greek: none of them is joined to another text's
    /// through the frame, only through <see cref="GeezVerseMap"/>'s reading of it.
    /// </param>
    internal sealed record GeezBook(
        string File,
        int Canonical,
        Origin Origin,
        string? Edition = null,
        GeezLayout Layout = GeezLayout.Verses,
        int ChapterShift = 0,
        bool OwnDivision = false);

    private const int Esther = 17;

    private const int Wisdom = 75;

    private const int LetterOfJeremiah = 76;

    /// <summary>The eighty-one, in the order the church's Bible prints them.</summary>
    public static IReadOnlyList<GeezBook> Books { get; } =
    [
        new("LIT1546Genesi.xml", 1, Origin.DillmannByHaCohen),
        new("LIT1367Exodus.xml", 2, Origin.DillmannByHaCohen),
        new("LIT1793Leviti.xml", 3, Origin.DillmannByHaCohen),
        new("LIT2075Number.xml", 4, Origin.DillmannByHaCohen),
        new("LIT2637Deuteronomy.xml", 5, Origin.DillmannByHaCohen),
        new("LIT1696Joshua.xml", 6, Origin.DillmannByHaCohen),
        new("LIT1700Judges.xml", 7, Origin.DillmannByHaCohen),
        new("LIT2229RuthBo.xml", 8, Origin.DillmannByHaCohen),
        new("LIT2697Sam.xml", 9, Origin.DillmannByHaCohen),
        new("LIT2698Sam.xml", 10, Origin.DillmannByHaCohen),
        new("LIT2699Kings.xml", 11, Origin.DillmannByHaCohen),
        new("LIT2700Kings.xml", 12, Origin.DillmannByHaCohen),
        new("LIT3499Chroni.xml", 13, Origin.ChurchPrintedBible),
        new("LIT3500Chroni.xml", 14, Origin.ChurchPrintedBible),
        new("LIT1697Jubilees.xml", 86, Origin.ChurchPrintedBible, Edition: "EOTCed"),
        new("LIT1340EnochE.xml", 85, Origin.ChurchPrintedBible, Edition: "EOTCed"),
        new("LIT3581Bookof.xml", 15, Origin.ChurchPrintedBible),
        new("LIT1374Bookof.xml", 16, Origin.ChurchPrintedBible),
        new("LIT1376Apocal.xml", 68, Origin.ChurchPrintedBible, ChapterShift: -1),
        new("LIT1377Bookof.xml", 69, Origin.ChurchPrintedBible, ChapterShift: 1),
        new("LIT2473TobitB.xml", 70, Origin.ChurchPrintedBible),
        new("LIT1701Judith.xml", 71, Origin.ChurchPrintedBible),
        new("LIT1362Esther.xml", Esther, Origin.ChurchPrintedBible, OwnDivision: true),
        new("LIT1819Maccab.xml", 87, Origin.ChurchPrintedBible),
        new("LIT5840SecondEthioMaccabees.xml", 88, Origin.ChurchPrintedBible),
        new("LIT5839ThirdEthioMaccabees.xml", 89, Origin.ChurchPrintedBible),
        new("LIT1688Job.xml", 18, Origin.ChurchPrintedBible, OwnDivision: true),
        new("LIT2000Mazmur.xml", 19, Origin.LudolfByHaCohen, Layout: GeezLayout.Psalter, OwnDivision: true),
        new("LIT3927Messale.xml", 91, Origin.ChurchPrintedBible),
        new("LIT2396Tagsas.xml", 92, Origin.ChurchPrintedBible),
        new("LIT2516Wisdom.xml", Wisdom, Origin.DillmannByJerabek, Edition: "ed1", OwnDivision: true),
        new("LIT1320Eccles.xml", 21, Origin.ChurchPrintedBible, Edition: "EOTCed"),
        new("LIT2362Songof.xml", 22, Origin.ChurchPrintedBible, Edition: "EOTCed", OwnDivision: true),
        new("LIT2358Sirach.xml", 72, Origin.ChurchPrintedBible),
        new("LIT1672Isaiah.xml", 23, Origin.ChurchPrintedBible),
        new("LIT1685Bookof.xml", 24, Origin.ChurchPrintedBible),
        new("LIT1202Bookof.xml", 67, Origin.ChurchPrintedBible),
        new("LIT1753Lament.xml", 25, Origin.ChurchPrintedBible),
        new("LIT1686Epistl.xml", LetterOfJeremiah, Origin.ChurchPrintedBible, OwnDivision: true),
        new("LIT2167Parali.xml", 90, Origin.DillmannByJerabek, Layout: GeezLayout.Chapters),
        new("LIT5802EzekII.xml", 26, Origin.ChurchPrintedBible),
        new("LIT3529Daniel.xml", 27, Origin.ChurchPrintedBible),
        new("LIT3144Hosea.xml", 28, Origin.ChurchPrintedBible),
        new("LIT3145Amos.xml", 30, Origin.ChurchPrintedBible),
        new("LIT3146Micah.xml", 33, Origin.ChurchPrintedBible),
        new("LIT1689Joel.xml", 29, Origin.ChurchPrintedBible),
        new("LIT3147Obadiah.xml", 31, Origin.ChurchPrintedBible),
        new("LIT1694Jonah.xml", 32, Origin.ChurchPrintedBible),
        new("LIT2057Bookof.xml", 34, Origin.ChurchPrintedBible),
        new("LIT1567Bookof.xml", 35, Origin.ChurchPrintedBible),
        new("LIT3148Zephan.xml", 36, Origin.ChurchPrintedBible),
        new("LIT3149Haggai.xml", 37, Origin.ChurchPrintedBible),
        new("LIT3150Zechar.xml", 38, Origin.ChurchPrintedBible),
        new("LIT3151Malachi.xml", 39, Origin.ChurchPrintedBible),
        new("LIT2709Matthew.xml", 40, Origin.ChurchPrintedBible),
        new("LIT2711Mark.xml", 41, Origin.ChurchPrintedBible),
        new("LIT2713Luke.xml", 42, Origin.ChurchPrintedBible),
        new("LIT2715John.xml", 43, Origin.ChurchPrintedBible),
        new("LIT1019Actsof.xml", 44, Origin.ChurchPrintedBible),
        new("LIT3515Epistle.xml", 45, Origin.ChurchPrintedBible),
        new("LIT3516Epistle.xml", 46, Origin.ChurchPrintedBible),
        new("LIT3517Epistle.xml", 47, Origin.ChurchPrintedBible),
        new("LIT3518Epistle.xml", 48, Origin.ChurchPrintedBible),
        new("LIT3519Epistle.xml", 49, Origin.ChurchPrintedBible),
        new("LIT3520Epistle.xml", 50, Origin.ChurchPrintedBible),
        new("LIT3521Epistle.xml", 51, Origin.ChurchPrintedBible),
        new("LIT3522Epistle.xml", 52, Origin.ChurchPrintedBible),
        new("LIT3523Epistle.xml", 53, Origin.ChurchPrintedBible),
        new("LIT3525Epistle.xml", 54, Origin.ChurchPrintedBible),
        new("LIT3526Epistle.xml", 55, Origin.ChurchPrintedBible),
        new("LIT3527Epistle.xml", 56, Origin.ChurchPrintedBible),
        new("LIT3528Epistle.xml", 57, Origin.ChurchPrintedBible),
        new("LIT3524Epistle.xml", 58, Origin.ChurchPrintedBible),
        new("LIT3512Epistle.xml", 59, Origin.ChurchPrintedBible),
        new("LIT3507Epistle.xml", 60, Origin.ChurchPrintedBible),
        new("LIT3508Epistle.xml", 61, Origin.ChurchPrintedBible),
        new("LIT3509Epistle.xml", 62, Origin.ChurchPrintedBible),
        new("LIT3510Epistle.xml", 63, Origin.ChurchPrintedBible),
        new("LIT3511Epistle.xml", 64, Origin.ChurchPrintedBible),
        new("LIT3513Epistle.xml", 65, Origin.ChurchPrintedBible),
        new("LIT3179Revela.xml", 66, Origin.ChurchPrintedBible),
    ];

    /// <summary>
    /// The texts every verse of this one is joined to where the frame puts the two at one address:
    /// the Hebrew it was not translated from but is read beside, the two Septuagints it was, and the
    /// Greek New Testament. Books with no counterpart in a text simply produce nothing for it.
    /// </summary>
    public static IReadOnlyList<DeclaredVersePair> VersePairs =>
    [
        .. new[]
        {
            BhsaTextSource.Slug, SeptuagintTextSource.Slug, SweteTextSource.Slug, NestleTextSource.Slug,
            ByzantineTextSource.Slug,
        }.Select(to => new DeclaredVersePair(Slug, to, UnlinkedBooks)),
    ];

    /// <summary>
    /// The Greek its words are aligned against, as one model: the two Septuagints for the Old
    /// Testament it was translated from, and the critical and the Byzantine New Testament, because
    /// which Greek the Ge'ez agrees with is what it witnesses to. Not BHSA — the Ge'ez reaches the
    /// Hebrew only through the Greek it was made from.
    /// </summary>
    public static IReadOnlyList<string> AlignedWith { get; } =
    [
        SweteTextSource.Slug, SeptuagintTextSource.Slug, NestleTextSource.Slug, ByzantineTextSource.Slug,
    ];

    /// <summary>
    /// The chapters where Swete stands at other rows of the frame than Brenton does, so that the
    /// Greek at a Ge'ez verse's row in Swete is some other passage: the Letter of Jeremiah is a row
    /// out.
    /// Found by comparing the two Septuagints row by row, where they share under a third of their
    /// words. Esther is out whole: Swete letters its additions as verses of the chapter they stand
    /// in, and the frame puts them at those verses' rows rather than at the verse they follow.
    /// Brenton answers for these chapters alone until Swete is placed as the frame places it.
    /// </summary>
    private static readonly HashSet<(int Book, int Chapter)> SweteOffTheFrame =
    [
        (2, 39), (11, 6), (19, 92), (68, 1), (68, 2), (68, 6), (70, 6),
        (LetterOfJeremiah, 1), (81, 8),
        .. Enumerable.Range(1, 10).Select(chapter => (Esther, chapter)),
    ];

    /// <summary>Whether a Ge'ez verse read at this row of the frame is aligned against this text.</summary>
    public static bool Aligns(string to, int book, int chapter, int verse) =>
        to != SweteTextSource.Slug || !SweteOffTheFrame.Contains((book, chapter));

    /// <summary>The books placed at their own numbers, joined verse by verse through the map and never through the frame.</summary>
    public static IReadOnlySet<int> UnlinkedBooks { get; } =
        Books.Where(book => book.OwnDivision).Select(book => book.Canonical).ToHashSet();

    /// <summary>
    /// A line the file puts in the wrong place, moved or renumbered where the file's own neighbours
    /// say where it belongs. Each is checked against the words it opens with, so a file that has
    /// changed is refused rather than repaired in the wrong place.
    /// </summary>
    /// <param name="ToFile">The book the line belongs to, where it is not the one it stands in.</param>
    private sealed record LineRepair(
        string File,
        int Chapter,
        int Number,
        string Opening,
        int ToNumber,
        string? ToFile = null,
        int? ToChapter = null);

    private static readonly LineRepair[] Repairs =
    [
        // The last line of 1 Samuel is numbered 1 and is the first verse of 2 Samuel, which the next
        // file lacks: it opens at 1:2.
        new("LIT2697Sam.xml", 31, 1, "ወእምዝ፡ እምድኅረ፡ ሞተ፡ ሳኦል", 1, ToFile: "LIT2698Sam.xml", ToChapter: 1),

        // Matthew 7 runs 1 to 28 and ends on a line numbered 24, which is the chapter's last verse.
        new("LIT2709Matthew.xml", 7, 24, "እስመ፡ ከመ፡ መኰንን፡ ይሜህሮሙ", 29),
    ];

    /// <summary>
    /// What stands on the witness page about the rights. The church's text carries the file's CC BY-SA
    /// statement and nothing from the church; HaCohen's and Jerabek's digitisations carry their own
    /// non-commercial notices, which are quoted in Resources/BetaMasaheft/LICENCE.md verbatim.
    /// </summary>
    public static TextDefinition Definition => new(
        Slug: Slug,
        Name: "The Ethiopic Bible (Ge'ez)",
        NameNative: "መጽሐፍ ቅዱስ",
        Kind: TextKind.PrintedEdition,
        Language: "gez",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.Septuagint,
        PublishedYear: null,
        SourceUrl: "https://github.com/BetaMasaheft/Works/tree/1d96713016d8c11becc50a3287c955b4cb67a5d1",
        RightsHolder: "Beta maṣāḥǝft, Akademie der Wissenschaften in Hamburg, over the files; Ran HaCohen over "
                      + "his digitisation of Dillmann's Octateuch and Books of Kingdoms and of Ludolf's Psalter; "
                      + "Michal Jerabek over his typing of Dillmann's Wisdom and 4 Baruch. The files state no "
                      + "claim of the Ethiopian Orthodox Tewahedo Church over its printed text.",
        Licence: "CC-BY-SA-4.0",
        LicenceUrl: "https://creativecommons.org/licenses/by-sa/4.0/",
        Redistribution: Redistribution.ShareAlike,
        TextualFamily: "Septuagint")
    {
        Editors = "Beta maṣāḥǝft (general editor Alessandro Bausi); August Dillmann (1823-1894) and Hiob "
                  + "Ludolf (1624-1704) for the fifteen books from their editions",
        Edition = "The Ethiopian Orthodox Tewahedo Church's printed Bible for sixty-six books, as typed for "
                  + "Beta maṣāḥǝft in 2025; Dillmann's Biblia Veteris Testamenti Aethiopica (1853-1871) for "
                  + "Genesis to Ruth and the four books of Kingdoms, and Ludolf's Psalterium (1701), as Ran "
                  + "HaCohen digitised them; Dillmann's Wisdom and 4 Baruch as Michal Jerabek typed them in 1995",
        About = "The Bible of the Ethiopian Orthodox Tewahedo Church in Ge'ez, its ancient liturgical language: "
                + "all eighty-one books the church counts, among them 1 Enoch and Jubilees, which survive whole "
                + "only in Ge'ez, and the three books of Meqabyan. The Old Testament was translated from the "
                + "Greek Septuagint between the fourth and the sixth century and the New from the Greek, so this "
                + "is a daughter of the Greek Bible and a witness to it. Most books are a digital transcription "
                + "of the church's printed Bible, made by hand in 2025, and it has typing errors: a word cut "
                + "short, two words run together, a verse numbered twice. Genesis to Ruth and the four books of "
                + "Kingdoms are Dillmann's nineteenth-century edition and the Psalms are Ludolf's of 1701, as Ran "
                + "HaCohen digitised them; Wisdom and 4 Baruch are Dillmann's text as Michal Jerabek typed it. "
                + "Those editions print a later form of the text than the first translation. 4 Baruch is divided "
                + "into chapters only, so each chapter stands as one passage. Esther, the Letter of Jeremiah and "
                + "Wisdom are divided into verses in their own way, and are not set verse by verse beside another "
                + "text. Proverbs is two books here, as the church counts it: Messale, chapters 1 to 24 and 30, "
                + "and Tägsas, chapters 25 to 29 and the end of 31. The section headings the printed Bible sets "
                + "between verses, and the prologue of Jubilees, are not included.",
        RightsNote = "Every file is published by Beta maṣāḥǝft under Creative Commons Attribution-ShareAlike "
                     + "4.0. The fifteen books from Dillmann's and Ludolf's editions carry more: Ran HaCohen's "
                     + "copyright over his transcription, and for Wisdom and 4 Baruch Michal Jerabek's notice, "
                     + "which permits non-commercial use only, with his notice kept, and says the text cannot be "
                     + "sold. The editions themselves are out of copyright. Neither the church nor the Bible "
                     + "Society of Ethiopia states any terms for the printed text, and neither has been asked.",
        Citation = "Beta maṣāḥǝft: Manuscripts of Ethiopia and Eritrea, Akademie der Wissenschaften in Hamburg, "
                   + "github.com/BetaMasaheft/Works at 1d96713, CC BY-SA 4.0; with Ran HaCohen's digitisation "
                   + "(tau.ac.il/~hacohen/Biblia.html) and Michal Jerabek's Library of Ethiopian Texts (1995).",
    };

    public static TextSource Read(string folder)
    {
        var raw = new Dictionary<string, List<GeezRawChapter>>(StringComparer.Ordinal);
        foreach (var book in Books)
        {
            var path = Path.Combine(folder, book.File);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"{Slug} is missing {book.File}. Run scripts/fetch-betamasaheft.ps1 rather than loading part of "
                    + "the Bible as though it were the whole of it — a book quietly absent from a witness reads "
                    + "as a book the witness does not contain.");
            }

            raw[book.File] = [.. GeezReader.Read(path, book.Edition, book.Layout, out _)];
        }

        Repair(raw);

        var books = new List<BookDraft>(Books.Count);
        for (var i = 0; i < Books.Count; i++)
        {
            var book = Books[i];
            books.Add(new BookDraft(
                CanonicalOrdinal: book.Canonical,
                Position: i + 1,
                Name: BookReferences.Name(book.Canonical),
                Slug: BookReferences.Slug(book.Canonical),
                Abbreviation: BookReferences.Abbreviation(book.Canonical),
                Chapters:
                [
                    // 3 Ezra and Ezra Sutuel open with a chapter 1 that holds no line; their text begins
                    // at the file's chapter 2.
                    .. raw[book.File]
                        .Where(chapter => chapter.Lines.Any(line => line.Text.Length > 0))
                        .Select(chapter => Chapter(book, chapter)),
                ]));
        }

        return new TextSource(Definition, books);
    }

    /// <summary>The verses of one chapter, as the corpus addresses them.</summary>
    private static ChapterDraft Chapter(GeezBook book, GeezRawChapter chapter)
    {
        var verses = book.Layout == GeezLayout.Chapters
            ? [new GeezVerse(1, chapter.Lines[0].Text)]
            : GeezVerses.Sequence(chapter.Lines, book.Layout == GeezLayout.Psalter).Verses;

        // A chapter the file counts from 0 — the Letter of Jeremiah's title, Bel's first verse — is
        // counted from 1 here, since the frame has no address before the first verse.
        var fromZero = verses.Count > 0 && verses[0].Number == 0 ? 1 : 0;
        var number = chapter.Number + book.ChapterShift;
        var renumbered = fromZero != 0 || book.ChapterShift != 0;

        return new ChapterDraft(number,
        [
            .. verses.Select((verse, index) =>
            {
                var text = index == 0 && chapter.Title is { } title
                    ? title + GeezReader.LineBreak + verse.Text
                    : verse.Text;
                return new VerseDraft(
                    verse.Number + fromZero, GeezWords.Words(text, book.Origin == Origin.DillmannByHaCohen))
                {
                    Stated = renumbered ? [new StatedNumberDraft(chapter.Number, verse.Number)] : [],
                    MarksASuperscription = index == 0 && chapter.Title is not null,
                };
            }),
        ]);
    }

    private static void Repair(Dictionary<string, List<GeezRawChapter>> raw)
    {
        foreach (var repair in Repairs)
        {
            var chapters = raw[repair.File];
            var at = chapters.FindIndex(chapter => chapter.Number == repair.Chapter);
            var line = at < 0
                ? null
                : chapters[at].Lines.LastOrDefault(line =>
                    line.Number == repair.Number && line.Text.StartsWith(repair.Opening, StringComparison.Ordinal));
            if (line is null)
            {
                throw new InvalidOperationException(
                    $"{repair.File} {repair.Chapter} has no line numbered {repair.Number} opening \"{repair.Opening}\". "
                    + "The file has changed since this repair was written; read the chapter again and repair it "
                    + "as it now stands, or drop the repair if the file has been corrected.");
            }

            var moved = line with { Number = repair.ToNumber };
            if (repair.ToFile is null)
            {
                chapters[at] = chapters[at] with
                {
                    Lines = [.. chapters[at].Lines.Select(other => ReferenceEquals(other, line) ? moved : other)],
                };
                continue;
            }

            chapters[at] = chapters[at] with { Lines = [.. chapters[at].Lines.Where(other => !ReferenceEquals(other, line))] };
            var target = raw[repair.ToFile];
            var into = target.FindIndex(chapter => chapter.Number == repair.ToChapter);
            target[into] = target[into] with { Lines = [moved, .. target[into].Lines] };
        }
    }
}
