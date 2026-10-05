using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Usfm;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading;

/// <summary>
/// The books the Greek and Latin Bibles hold beyond the sixty-six, where they come from: the eleven
/// the Synodal prints and marks as non-canonical, the Apocrypha of the King James, the World English
/// Bible's deuterocanon, and three texts that hold them as a matter of course — Brenton's English
/// Septuagint, the Douay-Rheims and the Clementine Vulgate.
///
/// Three of the six are books a text already loaded gains, not texts of their own. The Synodal, the
/// King James and the World English Bible are each the edition the corpus already serves, and their
/// deuterocanonical books are written into that text rather than beside it — see
/// <see cref="Extend"/> for what established that each is the same edition. The King James's Rest of
/// Esther is written into its Esther at the chapters and verses it prints, 10:4 to 16:24, which are
/// the standard's, and its Song of the Three is a book of its own, as it prints it, which the frame
/// also stands inside Daniel 3. The Synodal's additions come from the pages of the canonical books it
/// prints them inside: the Song of the Three and Susanna and Bel from Daniel, each a book of its own
/// here with the verse number it is printed under as its stated address, as the Vulgate's Susanna and
/// Bel are; the additions it sets in brackets inside six verses of Esther, each as the lettered piece
/// of the verse it stands in; and the Prayer of Manasseh after 2 Chronicles 36. The World English
/// Bible's Greek Esther and Daniel, whole books in a numbering of their own, are not taken.
///
/// Which Ezra is which is the trap in all of them, and it is settled by content, not by name. The
/// Greek 1 Esdras is ordinal 68 wherever it stands: the Synodal calls it the second book of Ezra, the
/// King James 1 Esdras. The Latin apocalypse is ordinal 69: the Synodal's third book of Ezra, the
/// King James's 2 Esdras, the Vulgate's 4 Ezra. Both Vulgate texts here leave it out, as Clement's
/// edition put it in an appendix.
/// </summary>
internal static class DeuterocanonTextSource
{
    /// <summary>
    /// Brenton's English translation of the Septuagint, by the name e-Sword and theWord serve it
    /// under. <see cref="Sources.BrentonSeptuagintSlug"/> is his Greek.
    /// </summary>
    public const string BrentonEnglish = "BRENTON";

    /// <summary>The Douay-Rheims, as Bible Gateway spells the 1899 American edition.</summary>
    public const string DouayRheims = "DRA";

    /// <summary>
    /// The Clementine Vulgate. <c>VULGATE</c> alone is taken by the Stuttgart critical edition in
    /// every Bible software that serves both, and the two are different texts.
    /// </summary>
    public const string ClementineVulgate = "VULGCLEM";

    public const string BrentonFolder = "BrentonEnglish";

    public const string DouayRheimsFolder = "DouayRheims1899";

    public const string VulgateFolder = "VulgataClementina";

    /// <summary>The Synodal's non-canonical books, as the fetch script writes them from ru.wikisource.</summary>
    public const string SynodalFolder = "SynodalWikisource";

    /// <summary>
    /// The World English Bible Classic, whose deuterocanon is the only one the World English Bible
    /// publishes under a package the owner approved; its sixty-six are not read.
    /// </summary>
    public const string WorldEnglishFolder = "WorldEnglishClassic";

    /// <summary>The King James with its Apocrypha, of which only the Apocrypha is read.</summary>
    public const string KingJamesFolder = "KingJamesApocrypha";

    /// <summary>
    /// Every USFM code these editions print a book under, with its canonical ordinal. The Greek
    /// Esther and the Greek Daniel are Esther and Daniel, longer, as they are for Brenton's Greek.
    /// </summary>
    private static readonly Dictionary<string, int> Ordinals = new(StringComparer.Ordinal)
    {
        ["TOB"] = 70, ["JDT"] = 71, ["ESG"] = 17, ["WIS"] = 75, ["SIR"] = 72, ["BAR"] = 67, ["LJE"] = 76,
        ["SUS"] = 77, ["BEL"] = 78, ["1MA"] = 73, ["2MA"] = 74, ["1ES"] = 68, ["2ES"] = 69, ["MAN"] = 79,
        ["3MA"] = 80, ["4MA"] = 81, ["PS2"] = 82, ["DAG"] = 27, ["S3Y"] = 93,
    };

    /// <summary>
    /// The books each loaded text gains, by USFM code, in the order its edition prints them.
    ///
    /// The King James's Apocrypha stands between the Testaments as the 1611 printed it. Its Rest of
    /// Esther goes into its Esther (<see cref="Continued"/>); its Song of the Three is a book of its own,
    /// as it prints it. The Synodal's Susanna, Bel and Song come from its Daniel (<see cref="Divided"/>).
    /// The World English Bible's Greek Esther and Greek Daniel are whole books that repeat the
    /// Hebrew ones the corpus already serves from the updated edition, and are not taken either.
    /// Baruch keeps the sixth chapter the King James and the World English Bible print as the
    /// Letter of Jeremiah, where the Synodal and Brenton print it as a book of its own.
    /// </summary>
    private static readonly Dictionary<string, string[]> Gained = new(StringComparer.OrdinalIgnoreCase)
    {
        [Sources.KingJamesSlug] =
            ["1ES", "2ES", "TOB", "JDT", "WIS", "SIR", "BAR", "S3Y", "SUS", "BEL", "MAN", "1MA", "2MA"],
        [Sources.SynodalSlug] =
            ["1ES", "TOB", "JDT", "WIS", "SIR", "LJE", "BAR", "1MA", "2MA", "3MA", "2ES", "MAN"],
        [EnglishTextSource.WorldEnglish] =
            ["TOB", "JDT", "WIS", "SIR", "BAR", "1MA", "2MA", "1ES", "MAN", "PS2", "3MA", "2ES", "4MA"],
    };

    /// <summary>
    /// The books each loaded text continues with another file's verses, by USFM code: the King James
    /// prints the additions to Esther as the Rest of Esther, numbered on from the Hebrew's last verse.
    /// </summary>
    private static readonly Dictionary<string, string[]> Continued = new(StringComparer.OrdinalIgnoreCase)
    {
        [Sources.KingJamesSlug] = ["ESG"],
        [Sources.SynodalSlug] = ["ESG"],
    };

    /// <summary>
    /// The books a loaded text gains out of another file's chapters: the Synodal's Daniel page gives the
    /// Song of the Three from 3:24-90, and Susanna and Bel from chapters 13 and 14.
    /// </summary>
    private static readonly Dictionary<string, string[]> Divided = new(StringComparer.OrdinalIgnoreCase)
    {
        [Sources.SynodalSlug] = ["DAG"],
    };

    private const int SongOfTheThree = 93;

    /// <summary>The books the Synodal's Daniel page is divided into, by canonical ordinal.</summary>
    private static readonly int[] DividedDaniel = [SongOfTheThree, 77, 78];

    /// <summary>
    /// What the Synodal calls each of its non-canonical books in its running heads, in the short form
    /// bible4u gives the books around them — <c>2-я Паралипоменон</c>, <c>Есфирь</c>.
    /// </summary>
    private static readonly Dictionary<int, string> SynodalNames = new()
    {
        [68] = "2-я Ездры", [70] = "Товит", [71] = "Иудифь", [75] = "Премудрость Соломона",
        [72] = "Премудрость Иисуса, сына Сирахова", [76] = "Послание Иеремии", [67] = "Варух",
        [73] = "1-я Маккавейская", [74] = "2-я Маккавейская", [80] = "3-я Маккавейская", [69] = "3-я Ездры",
        [79] = "Молитва Манассии", [93] = "Песнь трех отроков", [77] = "Сусанна", [78] = "Вил и дракон",
    };

    /// <summary>
    /// The books a text gains past the sixty-six, and the books it continues, by canonical ordinal.
    /// Empty for a text that gains none.
    /// </summary>
    public static IReadOnlySet<int> BooksGainedBy(string slug) =>
        (Gained.GetValueOrDefault(slug) ?? []).Concat(Continued.GetValueOrDefault(slug) ?? [])
        .Select(code => Ordinals[code])
        .Concat(Divided.ContainsKey(slug) ? DividedDaniel : [])
        .ToHashSet();

    /// <summary>
    /// The text with the books its edition prints and its file lacks. The loaded text and its source
    /// are the same edition in each case, which is what allows the books into it:
    ///
    /// <list type="bullet">
    /// <item>The Synodal's are the church's own printing of the same translation, the Moscow
    /// Patriarchate's edition of 2000, and bible4u's sixty-six are that translation renumbered to
    /// the King James.</item>
    /// <item>The King James's come from eBible's King James with Apocrypha, whose sixty-six are verse
    /// for verse the 1769 text of eBible's 2006 King James — all 31,102 verses read the same once
    /// markup is set aside — and that edition was compared with the loaded file and found to be the
    /// same standard text transcribed twice.</item>
    /// <item>The World English Bible's come from the Classic edition, which is not the edition loaded:
    /// it prints the divine name as Yahweh where the updated one prints LORD, and 25,276 of the 31,103
    /// verses of the sixty-six read the same. None of the thirteen books taken prints Yahweh.</item>
    /// </list>
    /// </summary>
    public static TextSource Extend(TextSource source, string resources)
    {
        var slug = source.Definition.Slug;
        if (!Gained.TryGetValue(slug, out var codes))
        {
            return source;
        }

        var (folder, part) = slug switch
        {
            Sources.SynodalSlug => (SynodalFolder, SynodalSource),
            Sources.KingJamesSlug => (KingJamesFolder, KingJamesSource),
            _ => (WorldEnglishFolder, WorldEnglishSource),
        };

        var continued = Continued.GetValueOrDefault(slug) ?? [];
        var divided = Divided.GetValueOrDefault(slug) ?? [];
        var files = Books(Path.Combine(resources, folder), [.. codes, .. continued, .. divided],
            editorialHeadings: slug == Sources.KingJamesSlug);
        var bible4u = slug is Sources.SynodalSlug or Sources.KingJamesSlug;
        var gained = codes.Select(code =>
        {
            var ordinal = Ordinals[code];
            var names = BibleBookAbbreviation.GetByOrdinal(ordinal)!;
            return new BookDraft(
                CanonicalOrdinal: ordinal,
                Position: 0,
                Name: bible4u ? names.FullName.Full : BookReferences.Name(ordinal),
                Slug: bible4u ? Slugs.Of(names.StandardAbbreviation.Full) : BookReferences.Slug(ordinal),
                Chapters: [.. files[code].Chapters.Select(chapter => Chapter(chapter, notes: true))],
                NameNative: slug switch
                {
                    Sources.SynodalSlug => SynodalNames[ordinal],
                    Sources.KingJamesSlug => names.FullName.Full,
                    _ => files[code].Book.Name,
                },
                Abbreviation: bible4u ? names.StandardAbbreviation.Full : BookReferences.Abbreviation(ordinal));
        }).ToList();
        gained.AddRange(divided.SelectMany(code => SynodalDaniel(files[code].Book)));

        var order = PrintedOrder(slug, gained.Select(book => book.CanonicalOrdinal));
        // The headings the file sets over the additions say where the Greek places each of them, which
        // is the editor's note and not the text.
        var longer = continued.ToDictionary(
            code => Ordinals[code],
            code => UsfmReader.Read(File.ReadAllText(files[code].File), editorialHeadings: true).Chapters);
        var books = source.Books
            .Select(book => longer.TryGetValue(book.CanonicalOrdinal, out var more) ? Continue(book, more) : book)
            .Concat(gained)
            .OrderBy(book => order.IndexOf(book.CanonicalOrdinal))
            .Select((book, index) => book with { Position = index + 1 })
            .ToList();

        TextPartSource[] parts = slug switch
        {
            Sources.KingJamesSlug => [part, KingJamesEstherSource],
            Sources.SynodalSlug => [part, SynodalAdditionsSource],
            _ => [part],
        };
        return new TextSource(
            source.Definition with { PartSources = [.. source.Definition.PartSources, .. parts] }, books);
    }

    /// <summary>
    /// The Synodal's Daniel page as the books it gives: the Song of the Three, which it prints as Daniel
    /// 3:24-90, and Susanna and Bel, its chapters 13 and 14. Each verse keeps the address it is printed
    /// under as its stated one. Susanna and Bel are numbered as they are printed; the song is numbered as
    /// the King James's book of it is, which is the standard's, read against it verse by verse: the
    /// Synodal's 3:52 holds the song's 29 and 30, it prints the angels before the heavens, and every other
    /// verse is the next in order.
    /// </summary>
    private static IEnumerable<BookDraft> SynodalDaniel(UsfmBook daniel)
    {
        foreach (var ordinal in DividedDaniel)
        {
            var printed = ordinal switch { SongOfTheThree => 3, 77 => 13, _ => 14 };
            var names = BibleBookAbbreviation.GetByOrdinal(ordinal)!;
            var verses = Chapter(daniel.Chapters.Single(chapter => chapter.Number == printed), notes: true).Verses
                .Select(verse => Moved(verse, printed, ordinal == SongOfTheThree ? SongVerse(verse.Number) : verse.Number));
            yield return new BookDraft(ordinal, 0, names.FullName.Full, Slugs.Of(names.StandardAbbreviation.Full),
                [new ChapterDraft(1, [.. verses])], SynodalNames[ordinal], names.StandardAbbreviation.Full);
        }
    }

    /// <summary>The verse of the song the Synodal's Daniel 3:24-90 prints, in the standard numbering.</summary>
    private static int SongVerse(int printed) => printed switch
    {
        < 24 or > 90 => throw new InvalidOperationException(
            $"The Synodal's Daniel 3:{printed} is not a verse of the song, which is 3:24-90; the page was read wrongly."),
        <= 51 => printed - 23,
        52 => 29,
        58 => 37,
        59 => 36,
        _ => printed - 22,
    };

    /// <summary>
    /// A book with the verses another file of its edition prints after it: into the chapter of the
    /// same number after the verses it has, and as chapters of their own after that.
    /// </summary>
    private static BookDraft Continue(BookDraft book, IReadOnlyList<UsfmChapter> more)
    {
        var added = more.Select(chapter => Chapter(chapter, notes: true)).ToDictionary(chapter => chapter.Number);
        var clash = book.Chapters
            .Where(chapter => added.ContainsKey(chapter.Number))
            .SelectMany(chapter => chapter.Verses.Select(verse => (chapter.Number, verse.Number, verse.Label)))
            .Intersect(added.Values.SelectMany(chapter =>
                chapter.Verses.Select(verse => (chapter.Number, verse.Number, verse.Label))))
            .FirstOrDefault();
        if (clash != default)
        {
            throw new InvalidOperationException(
                $"{book.Name} {clash.Item1}:{clash.Item2}{clash.Item3} is printed in both files of the edition, so " +
                "one of them is not the continuation of the other. Check that the folder holds the edition the " +
                "text was loaded from.");
        }

        return book with
        {
            Chapters =
            [
                .. book.Chapters.Select(chapter => added.TryGetValue(chapter.Number, out var tail)
                    ? chapter with { Verses = [.. chapter.Verses, .. tail.Verses] }
                    : chapter),
                .. added.Values.Where(chapter => book.Chapters.All(own => own.Number != chapter.Number)),
            ],
        };
    }

    /// <summary>
    /// The order the edition prints its books in, as canonical ordinals. The Synodal's is its
    /// canon's; the King James and the World English Bible print theirs between the Testaments.
    /// </summary>
    private static List<int> PrintedOrder(string slug, IEnumerable<int> gained) =>
        slug == Sources.SynodalSlug
            ? [.. Canons.Find(Canons.Synodal)!.Ordinals]
            : [.. Enumerable.Range(1, BookReferences.OldTestamentBookCount), .. gained,
                .. Enumerable.Range(BookReferences.OldTestamentBookCount + 1, 27)];

    /// <summary>
    /// The Synodal's non-canonical books: Russian Wikisource's transcription of the Moscow Patriarchate's
    /// edition of 2000, public domain under article 1281 of the Civil Code, as the Synodal's own page
    /// there says.
    /// </summary>
    public static readonly TextPartSource SynodalSource = new(
        "Библия (Синодальный перевод)",
        "The contributors to Russian Wikisource, transcribing the Moscow Patriarchate's edition of 2000",
        "Public Domain",
        null,
        "https://ru.wikisource.org/wiki/Библия_(Синодальный_перевод)",
        "The eleven books the Synodal prints and marks as non-canonical — 2 and 3 Ezra, Tobit, Judith, the "
        + "Wisdom of Solomon, Sirach, the Letter of Jeremiah, Baruch and 1, 2 and 3 Maccabees — which the file "
        + "the text is loaded from does not hold, each page at a fixed revision and in the Synodal's own numbering.");

    /// <summary>
    /// The Greek additions the Synodal prints inside three canonical books, from the same transcription
    /// as its non-canonical books.
    /// </summary>
    public static readonly TextPartSource SynodalAdditionsSource = SynodalSource with
    {
        Covers = "The Greek additions the Synodal prints inside three canonical books and sets in brackets: the "
                 + "Song of the Three and Susanna and Bel from Daniel (3:24-90, 13 and 14), the additions to Esther "
                 + "and the Prayer of Manasseh after 2 Chronicles 36, which the file the text is loaded from leaves "
                 + "out.",
    };

    public static readonly TextPartSource KingJamesSource = new(
        "King James Version + Apocrypha (eng-kjv)",
        "eBible.org, from the standardised 1769 text",
        "Public Domain",
        "https://ebible.org/eng-kjv/copyright.htm",
        "https://ebible.org/find/details.php?id=eng-kjv",
        "The Apocrypha — 1 and 2 Esdras, Tobit, Judith, Wisdom, Sirach, Baruch, Susanna, Bel and the Dragon, "
        + "the Prayer of Manasses and 1 and 2 Maccabees — which the file the text is loaded from does not hold.");

    /// <summary>The Rest of Esther, from the same edition, which continues Esther rather than adding a book.</summary>
    public static readonly TextPartSource KingJamesEstherSource = KingJamesSource with
    {
        Covers = "The Rest of Esther, the additions to Esther the Apocrypha prints as Esther 10:4 to 16:24.",
    };

    /// <summary>
    /// The World English Bible's deuterocanon, from the Classic edition. The updated edition with the
    /// deuterocanon, engwebu, was not fetched, so that these books read as it prints them is likely,
    /// since none of them prints the divine name, and not established.
    /// </summary>
    public static readonly TextPartSource WorldEnglishSource = new(
        "World English Bible Classic (eng-web)",
        "Michael Paul Johnson and the volunteers of eBible.org",
        "Public Domain",
        "https://ebible.org/eng-web/copyright.htm",
        "https://ebible.org/find/details.php?id=eng-web",
        "The deuterocanonical books — Tobit, Judith, Wisdom, Sirach, Baruch, 1 to 4 Maccabees, 1 and 2 Esdras, "
        + "the Prayer of Manasseh and Psalm 151 — from the Classic edition, whose other books are not read.");

    public static readonly TextDefinition BrentonDefinition = new(
        Slug: BrentonEnglish,
        Name: "Brenton's English Septuagint",
        NameNative: null,
        Kind: TextKind.Translation,
        Language: "eng",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.Septuagint,
        PublishedYear: 1844,
        SourceUrl: "https://ebible.org/find/details.php?id=eng-Brenton",
        RightsHolder: null,
        Licence: "Public Domain",
        LicenceUrl: "https://ebible.org/eng-Brenton/copyright.htm",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: "Septuagint")
    {
        Translators = "Sir Lancelot Charles Lee Brenton (1807-1862)",
        Edition =
            "The Old Testament of 1844 with the Apocrypha of 1851, as Samuel Bagster and Sons printed them "
            + "facing the Greek, with eBible's corrections of the 1870 diglot",
        EditionYear = 1851,
        About =
            "The English beside the Greek the corpus already holds as Brenton's Septuagint: Brenton translated "
            + "the Vatican text in Valpy's reprint of the Sixtine edition of 1587, and this is that translation, "
            + "divided verse for verse as the Greek is but in five chapters: four of Nehemiah, which the English "
            + "numbers as the English Bibles do and the Greek as the Hebrew, and 1 Samuel 17, into which eBible "
            + "sets, as supplied words, the twenty verses the Vatican text lacks and Brenton translated from the "
            + "Alexandrian codex in an appendix. It is the first "
            + "English of the Septuagint and for a century and a half the only one, and it translates the "
            + "Greek where the Greek differs from the Hebrew: Jeremiah in the Greek order, Esther and Daniel "
            + "with their additions, Kingdoms for Samuel and Kings. The Apocrypha was added in 1851, and there "
            + "Brenton adapted the Authorized Version rather than translating afresh; its preface says the third "
            + "and fourth books of Maccabees were translated for that edition. The words printed in italics "
            + "as supplied — 9,070 spans — are marked as supplied here. The Psalms of Solomon are not in it.",
        RightsNote =
            "Public domain: Brenton died in 1862, and eBible states it in its catalogue, on its copyright page "
            + "and in the copy of that page inside the archive (\"Published in 1851, and now in the Public "
            + "Domain\"). eBible's transcription corrects the 1870 diglot against the 1844 English and, for the "
            + "Apocrypha, the Authorized Version, and lists every correction in an errata file that is kept "
            + "beside the data and not loaded, with Brenton's two prefaces and his table of Jeremiah's chapters.",
        Citation =
            "The Septuagint Version of the Old Testament and Apocrypha, with an English translation, by Sir "
            + "Lancelot Charles Lee Brenton, London: Samuel Bagster and Sons, 1851, in the digital edition "
            + "eBible.org publishes as eng-Brenton.",
    };

    public static readonly TextDefinition DouayRheimsDefinition = new(
        Slug: DouayRheims,
        Name: "Douay-Rheims Bible",
        NameNative: null,
        Kind: TextKind.Translation,
        Language: "eng",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.Vulgate,
        PublishedYear: 1610,
        SourceUrl: "https://ebible.org/find/details.php?id=engDRA",
        RightsHolder: null,
        Licence: "Public Domain",
        LicenceUrl: "https://ebible.org/engDRA/copyright.htm",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: "Vulgate")
    {
        Translators =
            "Gregory Martin (c. 1542-1582) and the English College at Douai and Rheims; revised by Bishop "
            + "Richard Challoner (1691-1781)",
        Edition = "Challoner's revision of 1749-1752, in the American edition of 1899",
        EditionYear = 1899,
        About =
            "The English Catholic Bible, translated from the Latin Vulgate rather than from the Hebrew and "
            + "Greek: the New Testament at Rheims in 1582 and the Old at Douai in 1609-1610, by English exiles "
            + "under William Allen. What is read today is Challoner's revision of the 1750s, which brought its "
            + "English closer to the King James; this is the American printing of 1899. Because it translates "
            + "the Latin, it follows the Vulgate everywhere the Vulgate differs — the Psalms in the Greek "
            + "numbering, Tobit and Judith in Jerome's own shorter and longer forms, Sirach in the Latin's "
            + "longer text, Esther with the additions gathered at its end in chapters 11 to 16, Daniel with "
            + "the Song of the Three at 3:24-90 — and it holds the seven deuterocanonical books where the "
            + "Vulgate places them. Susanna and Bel, which it prints as Daniel 13 and 14, are read as the "
            + "books they are in the Greek, each verse keeping the number it is printed under. 1 and 2 Esdras "
            + "here are Ezra and Nehemiah, as the Vulgate names them.",
        RightsNote =
            "Public domain, stated so by eBible in its catalogue, on its copyright page and in the copy of that "
            + "page inside the archive. The file tags about half its words with Strong numbers that nobody is "
            + "named for, and in Genesis 2:24 gives \"and\" and \"shall\" the number for man; they are a "
            + "verse's numbers spread over its words rather than a claim about any word, and are not loaded.",
        Citation =
            "The Holy Bible, Douay-Rheims version, revised by Bishop Richard Challoner, John Murphy Company, "
            + "Baltimore, 1899, in the digital edition eBible.org publishes as engDRA.",
    };

    public static readonly TextDefinition VulgateDefinition = new(
        Slug: ClementineVulgate,
        Name: "Clementine Vulgate",
        NameNative: "Biblia Sacra Vulgatae Editionis",
        Kind: TextKind.Translation,
        Language: "lat",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.Vulgate,
        PublishedYear: 1592,
        SourceUrl: "https://ebible.org/find/details.php?id=latVUC",
        RightsHolder: null,
        Licence: "Public Domain",
        LicenceUrl: "https://ebible.org/latVUC/copyright.htm",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: "Vulgate")
    {
        Translators = "Jerome (c. 347-420), from the Hebrew, and the older Latin versions he revised",
        Editors = "The commission of Clement VIII, which published the text in 1592",
        Edition = "The Sixto-Clementine text as reprinted in 1598, from the Migne edition of 1880",
        EditionYear = 1598,
        About =
            "The Latin Bible of the Western Church for a thousand years, in the edition Clement VIII issued in "
            + "1592 to replace Sixtus V's of 1590, and the Catholic Church's official text until the Nova Vulgata "
            + "of 1979. Jerome translated most of the Old Testament from the Hebrew between 390 and 405; the "
            + "Psalms are his earlier revision from the Greek, the Gallican Psalter, numbered as the Greek "
            + "numbers them; Wisdom, Sirach, Baruch and 1 and 2 Maccabees are the Old Latin he left untouched, "
            + "and Tobit and Judith his rapid translations from an Aramaic he had read to him. The New "
            + "Testament is his revision of the Old Latin Gospels and others' of the rest. This file holds the "
            + "seventy-three books of the Catholic canon; the Prayer of Manasseh and 3 and 4 Esdras, which "
            + "Clement's edition prints in an appendix, are not in it. Susanna and Bel, which it prints as "
            + "Daniel 13 and 14, are read as the books they are in the Greek, each verse keeping the number it "
            + "is printed under.",
        RightsNote =
            "Public domain by age, stated so by eBible in its catalogue, on its copyright page and in the copy "
            + "of that page inside the archive. The file carries the Glossa Ordinaria from Migne's edition as "
            + "13,775 footnotes; the Glossa is a medieval commentary and not the Vulgate, and it is not loaded.",
        Citation =
            "Biblia Sacra Vulgatae Editionis, Sixti V Pontificis Maximi iussu recognita et Clementis VIII "
            + "auctoritate edita, in the digital edition eBible.org publishes as latVUC.",
    };

    /// <summary>The three texts read whole, by the folder the fetch writes each into.</summary>
    public static IReadOnlyList<(string Folder, TextDefinition Definition)> Texts =>
    [
        (BrentonFolder, BrentonDefinition),
        (DouayRheimsFolder, DouayRheimsDefinition),
        (VulgateFolder, VulgateDefinition),
    ];

    /// <summary>
    /// One of the three texts read whole. Brenton is in his own order, which is eBible's; the two Latin
    /// texts in the Vulgate's, which is the Catholic canon's.
    /// </summary>
    public static TextSource Read(string folder)
    {
        var name = new DirectoryInfo(folder).Name;
        var (_, definition) = Texts.FirstOrDefault(text => string.Equals(text.Folder, name, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
        {
            throw new ArgumentException(
                $"There is no text definition for the folder \"{name}\". A text cannot be loaded without one: " +
                "its licence and provenance are part of the definition, not something filled in afterwards.",
                nameof(folder));
        }

        var files = Books(folder, null);
        var brenton = definition.Slug == BrentonEnglish;
        List<string> codes = brenton
            ? [.. files.Keys.OrderBy(code => files[code].Order)]
            : [.. VulgateOrder.Where(files.ContainsKey)];

        var expected = brenton ? BrentonBooks : VulgateBooks;
        if (codes.Count != expected || files.Count != expected)
        {
            throw new InvalidOperationException(
                $"{definition.Slug} holds {files.Count} books in {folder} and this edition has {expected}: the fetch "
                + "was partial, or the folder holds a different edition. Run scripts/fetch-ebible.ps1 rather than "
                + "loading part of a text as though it were the whole of one.");
        }

        var books = codes.Select(code =>
        {
            var ordinal = OrdinalOf(code);
            return new BookDraft(
                CanonicalOrdinal: ordinal,
                Position: 0,
                Name: BookReferences.Name(ordinal),
                Slug: BookReferences.Slug(ordinal),
                Chapters: [.. files[code].Chapters.Select(chapter =>
                    Chapter(chapter, notes: definition.Slug != ClementineVulgate))],
                NameNative: files[code].Book.Name,
                Abbreviation: BookReferences.Abbreviation(ordinal));
        });

        return new TextSource(definition, [
            .. (brenton ? books : books.SelectMany(SusannaAndBelApart))
                .Select((book, index) => book with { Position = index + 1 }),
        ]);
    }

    private const int Daniel = 27;

    private const int Susanna = 77;

    private const int Bel = 78;

    /// <summary>
    /// The Vulgate's Daniel as three books: Daniel, and Susanna and Bel, which it prints as chapters 13
    /// and 14 and the corpus holds as books of their own, as Brenton and Swete print them. Where each
    /// verse goes is the versification data's Latin rule, not a count: 13:1-64 is Susanna 1-64, 13:65
    /// is the first verse of Bel, 14:1-40 its verses 2 to 41, and 14:41 and 14:42 the two halves of its
    /// verse 42, the second of them lettered as the Greek's pieces are. Every verse keeps the number it
    /// is printed under as the edition's own. The Song of the Three at 3:24-90 has no book of its own
    /// here and stays in Daniel 3, where the Vulgate prints it, and so do Esther's chapters 11 to 16.
    /// </summary>
    private static IEnumerable<BookDraft> SusannaAndBelApart(BookDraft book)
    {
        if (book.CanonicalOrdinal != Daniel)
        {
            yield return book;
            yield break;
        }

        var thirteen = book.Chapters.Single(chapter => chapter.Number == 13).Verses;
        var fourteen = book.Chapters.Single(chapter => chapter.Number == 14).Verses;

        yield return book with { Chapters = [.. book.Chapters.Where(chapter => chapter.Number < 13)] };
        yield return Apart(Susanna, [.. thirteen.Where(verse => verse.Number <= SusannaVerses)
            .Select(verse => Moved(verse, 13, verse.Number))]);
        yield return Apart(Bel, [
            .. thirteen.Where(verse => verse.Number > SusannaVerses).Select(verse => Moved(verse, 13, 1)),
            .. fourteen.Select(verse => verse.Number < fourteen.Count
                ? Moved(verse, 14, verse.Number + 1)
                : Moved(verse, 14, verse.Number) with { Label = SecondHalf }),
        ]);

        BookDraft Apart(int ordinal, IReadOnlyList<VerseDraft> verses) => new(
            ordinal, 0, BookReferences.Name(ordinal), BookReferences.Slug(ordinal), [new ChapterDraft(1, verses)],
            Abbreviation: BookReferences.Abbreviation(ordinal));
    }

    /// <summary>The verses of Daniel 13 that are Susanna; the one after them is the first of Bel.</summary>
    private const int SusannaVerses = 64;

    /// <summary>The letter the second half of a divided verse takes, as the Greek editions letter theirs.</summary>
    private const string SecondHalf = "a";

    private static VerseDraft Moved(VerseDraft verse, int printedChapter, int number) => verse with
    {
        Number = number,
        Stated = [new StatedNumberDraft(printedChapter, verse.Number)],
    };

    /// <summary>
    /// The Vulgate's order: Tobit and Judith after Nehemiah, which it calls 2 Esdras, Wisdom and Sirach
    /// after the Song, Baruch after Lamentations and the Maccabees after Malachi.
    /// </summary>
    private static readonly string[] VulgateOrder =
    [
        .. EnglishTextSource.Canon[..16], "TOB", "JDT", .. EnglishTextSource.Canon[16..22], "WIS", "SIR",
        .. EnglishTextSource.Canon[22..25], "BAR", .. EnglishTextSource.Canon[25..39], "1MA", "2MA",
        .. EnglishTextSource.Canon[39..],
    ];

    /// <summary>
    /// Brenton's thirty-seven books of the Hebrew canon and his sixteen of the Greek, and the
    /// seventy-three of the Vulgate.
    /// </summary>
    private const int BrentonBooks = 53;

    private const int VulgateBooks = 73;

    private static int OrdinalOf(string code)
    {
        var protocanon = Array.IndexOf(EnglishTextSource.Canon, code);
        return protocanon >= 0
            ? protocanon + 1
            : Ordinals.TryGetValue(code, out var ordinal)
                ? ordinal
                : throw new InvalidOperationException(
                    $"No canonical ordinal is known for the book \"{code}\". Give it one in " +
                    $"{nameof(BibleBookAbbreviation)} and here before loading it.");
    }

    private sealed record ReadBook(UsfmBook Book, string File)
    {
        public IReadOnlyList<UsfmChapter> Chapters => Book.Chapters;

        /// <summary>The number eBible's file name starts with, which is the order the edition prints.</summary>
        public int Order => int.Parse(Path.GetFileName(File).Split('-')[0]);
    }

    /// <summary>
    /// The books of a folder by the code their <c>\id</c> line states, and only scripture: eBible
    /// ships prefaces, tables and errata as USFM files of their own, under codes a book never has.
    /// </summary>
    private static Dictionary<string, ReadBook> Books(
        string folder, IReadOnlyCollection<string>? wanted, bool editorialHeadings = false)
    {
        var books = new Dictionary<string, ReadBook>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(folder, "*.usfm"))
        {
            var content = File.ReadAllText(path);
            var code = content.AsSpan(content.IndexOf("\\id ", StringComparison.Ordinal) + 4).ToString()
                .Split([' ', '\r', '\n'], 2)[0];
            if ((wanted is not null && !wanted.Contains(code)) ||
                (wanted is null && Array.IndexOf(EnglishTextSource.Canon, code) < 0 && !Ordinals.ContainsKey(code)))
            {
                continue;
            }

            books[code] = new ReadBook(UsfmReader.Read(content, editorialHeadings), path);
        }

        if (wanted?.FirstOrDefault(code => !books.ContainsKey(code)) is { } missing)
        {
            throw new InvalidOperationException(
                $"{folder} has no book {missing}. The fetch was partial or wrote another edition; run its fetch "
                + "script again rather than loading the text without it.");
        }

        return books;
    }

    /// <param name="notes">
    /// Whether the edition's notes are its own. The Clementine's are the Glossa Ordinaria, a commentary
    /// Migne printed round the text, and they stay out.
    /// </param>
    private static ChapterDraft Chapter(UsfmChapter chapter, bool notes) => new(
        chapter.Number,
        [.. chapter.Verses.Select(verse => new VerseDraft(
            verse.Number,
            [.. verse.Words.Select(word => new WordDraft(
                word.Surface,
                word.Trailer,
                SuppliedSpan: word.SuppliedSpan,
                Break: word.Break))],
            verse.Label)
        {
            Notes = notes
                ? [.. verse.Notes.Select(note => new VerseNoteDraft(
                    note.Kind == UsfmNoteKind.Footnote ? VerseNoteKind.Footnote : VerseNoteKind.CrossReference,
                    note.Content,
                    note.AnchorWordPosition))]
                : [],
            Stated = [.. verse.Stated.Select(address => new StatedNumberDraft(address.Chapter, address.Number))],
            OpensBeforeItsStatedAddress = verse.OpensBeforeItsStatedAddress,
            MarksASuperscription = verse.MarksASuperscription,
        })]);

    /// <summary>
    /// The texts every verse of these books is joined to where the frame puts the two at one address,
    /// before any word of them is linked: the Greek of the Septuagint for the books it holds, BHSA and
    /// Nestle 1904 for the three texts read whole, the Clementine for the Douay-Rheims translated from
    /// it, and the King James for the Synodal's and the World English Bible's, which is the one other
    /// English text with the Latin 2 Esdras. A book the frame does not place is joined only in the
    /// chapters where the two print the same verses, since there the shared address is all there is.
    /// </summary>
    public static IReadOnlyList<DeclaredVersePair> VersePairs =>
    [
        .. Gained.Keys.SelectMany(slug => (slug == Sources.KingJamesSlug
                ? new[] { Sources.BrentonSeptuagintSlug, Sources.SweteSlug }
                : [Sources.BrentonSeptuagintSlug, Sources.SweteSlug, Sources.KingJamesSlug])
            .Select(to => Only(slug, to, BooksGainedBy(slug)))),
        .. new[] { Sources.BrentonSeptuagintSlug, Sources.SweteSlug, BhsaTextSource.Slug }
            .Select(to => Whole(BrentonEnglish, to)),
        .. new[] { ClementineVulgate, Sources.BrentonSeptuagintSlug, Sources.SweteSlug, BhsaTextSource.Slug, Sources.NestleSlug }
            .Select(to => Whole(DouayRheims, to)),
        .. new[] { Sources.BrentonSeptuagintSlug, Sources.SweteSlug, BhsaTextSource.Slug, Sources.NestleSlug }
            .Select(to => Whole(ClementineVulgate, to)),
    ];

    private static DeclaredVersePair Whole(string from, string to) =>
        new(from, to, new HashSet<int>()) { AgreeingChaptersOnly = true };

    /// <summary>
    /// A pair joined only in the books the first text gains. A passage printed under two names is
    /// joined under one (<see cref="TwinPassages"/>), so a text that gains Baruch with the Letter of
    /// Jeremiah as its sixth chapter gains the letter.
    /// </summary>
    private static DeclaredVersePair Only(string from, string to, IReadOnlySet<int> books) =>
        new(from, to, Enumerable.Range(1, BookReferences.LastOrdinal)
            .Where(book => !books.Contains(book)
                           && !books.Any(gained => TwinPassages.JoinedBook(gained) == book))
            .ToHashSet())
        {
            AgreeingChaptersOnly = true,
        };
}
