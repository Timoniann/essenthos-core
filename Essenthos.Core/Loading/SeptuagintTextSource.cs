using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Septuagint;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading;

/// <summary>
/// Brenton's Septuagint, 1851.
///
/// The witness the old schema could not hold at any price, and the reason the model was rebuilt.
/// It is a Greek translation of a Hebrew text, so it is neither an original nor a translation in
/// the old vocabulary — it is a witness that renders another witness, which is a sentence the
/// witness model can say and the previous one could not.
///
/// It carries no annotation whatsoever: no morphology, no lemmas, no Strong numbers, no alignment
/// to the Hebrew. That is the trade DOC-0085 records and the owner had already chosen — every
/// annotated Septuagint in existence descends from CATSS and is NonCommercial or worse, and this
/// one is public domain outright. What alignment there is to be had will be built here.
///
/// Sixteen of its fifty-two files had no ordinal until FTR-0091, which is why this could not be
/// loaded before. Two of them are not extra books at all: Greek Esther and Greek Daniel are Esther
/// and Daniel, longer, so they take those books' canonical ordinals and their own versification.
/// One file is two books: Esdras B is Ezra and Nehemiah together, and is split on load.
/// </summary>
internal static class SeptuagintTextSource
{
    public const string Slug = "lxx-brenton";

    /// <summary>
    /// The books Brenton prints, in the order the files come in, which is his own, with each
    /// one's place in the shared canon.
    ///
    /// The codes are USFM's, which is the scheme eBible names its files by. They are kept here
    /// rather than added to <see cref="BibleBookAbbreviation"/> as aliases, because that table
    /// matches alternatives by scanning and the first match wins — putting a second naming scheme
    /// into it silently changes what an existing alias resolves to.
    ///
    /// Two are not extra books. <c>ESG</c> and <c>DAG</c> are the Greek Esther and the Greek
    /// Daniel: the same books, longer, and a witness holding its own longer edition at the same
    /// canonical ordinal is exactly what the model is for. Giving them ordinals of their own would
    /// put one book in the canon twice under two names.
    ///
    /// There is no <c>NEH</c> file. The Greek tradition prints Ezra and Nehemiah together as
    /// Esdras B, twenty-three chapters under one heading, so <c>EZR</c> carries both and is split
    /// on load — see <see cref="SecondEsdras"/>.
    /// </summary>
    private static readonly (string Code, int Canonical)[] Canon =
    [
        ("GEN", 1), ("EXO", 2), ("LEV", 3), ("NUM", 4), ("DEU", 5), ("JOS", 6), ("JDG", 7), ("RUT", 8),
        ("1SA", 9), ("2SA", 10), ("1KI", 11), ("2KI", 12), ("1CH", 13), ("2CH", 14), ("EZR", 15),
        ("JOB", 18), ("PSA", 19), ("PRO", 20), ("ECC", 21), ("SNG", 22), ("ISA", 23), ("JER", 24),
        ("LAM", 25), ("EZK", 26), ("HOS", 28), ("JOL", 29), ("AMO", 30), ("OBA", 31), ("JON", 32),
        ("MIC", 33), ("NAM", 34), ("HAB", 35), ("ZEP", 36), ("HAG", 37), ("ZEC", 38), ("MAL", 39),
        ("TOB", 70), ("JDT", 71), ("ESG", 17), ("WIS", 75), ("SIR", 72), ("BAR", 67), ("LJE", 76),
        ("SUS", 77), ("BEL", 78), ("1MA", 73), ("2MA", 74), ("1ES", 68), ("MAN", 79), ("3MA", 80),
        ("4MA", 81), ("DAG", 27),
    ];

    /// <summary>
    /// The copyright page shipped beside the data says <c>Public Domain</c> twice, and eBible's
    /// machine-readable catalogue says <c>Copyright = "public domain"</c> and
    /// <c>Redistributable = "True"</c>. Brenton died in 1862. Kept beside the data as
    /// <c>copr.htm</c>, because a licence that lives only at a URL is one nobody can check offline.
    /// </summary>
    public static TextDefinition Definition() => new(
        Slug: Slug,
        Name: "Brenton's Septuagint",
        NameNative: "Η ΠΑΛΑΙΑ ΔΙΑΘΗΚΗ ΚΑΤΑ ΤΟΥΣ ΕΒΔΟΜΗΚΟΝΤΑ",
        Kind: TextKind.PrintedEdition,
        Language: "grc",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.Septuagint,
        PublishedYear: 1851,
        SourceUrl: "https://ebible.org/find/details.php?id=grcbrent",
        RightsHolder: null,
        Licence: "Public Domain",
        LicenceUrl: "https://ebible.org/find/details.php?id=grcbrent",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: "Septuagint")
    {
        Editors = "Sir Lancelot Charles Lee Brenton",
        Edition = "The Greek Brenton printed facing his English translation, following Codex Vaticanus",
        About = "The Greek here is not Brenton's work in the way the English beside it is: he printed "
                + "a text following Codex Vaticanus, and what he translated was that. Who first put "
                + "these books into Greek is not known — the translation was made in Alexandria "
                + "between roughly the third and the first century BC, by different hands book by "
                + "book, which is why its books differ so much from one another in manner. Samuel "
                + "Bagster and Sons published Brenton's edition in London in 1844 and added the "
                + "Apocrypha in 1851. It arrived here with no annotation at all; its lemmas come from "
                + "GLAUx.",
    };

    /// <summary>
    /// Esdras B, which is Ezra and Nehemiah under one heading: chapters 1 to 10 are Ezra and 11 to
    /// 23 are Nehemiah 1 to 13.
    ///
    /// The split is made here rather than left to the frame because the versification data itself
    /// numbers Greek Nehemiah from one — it says Greek Nehemiah 3:33 is the standard 4:1, and that
    /// rule cannot be found by a verse that calls itself Ezra 13:33. Kept as one book, thirteen
    /// chapters would have no address in the shared frame at all and Nehemiah would be missing from
    /// this witness while its Greek sat in the database.
    /// </summary>
    private static class SecondEsdras
    {
        public const string Code = "EZR";

        public const int Ezra = 15;

        public const int Nehemiah = 16;

        /// <summary>The last chapter of Esdras B that belongs to Ezra.</summary>
        public const int LastEzraChapter = 10;
    }

    public static TextSource Read(string folder)
    {
        var files = Directory.GetFiles(folder, "*.usfm")
            .ToDictionary(path => Code(Path.GetFileName(path)), path => path);

        var books = new List<BookDraft>(Canon.Length + 1);
        var position = 0;

        foreach (var (code, canonical) in Canon)
        {
            if (!files.TryGetValue(code, out var path))
            {
                continue;
            }

            var read = UsfmReader.Read(File.ReadAllText(path));
            if (read.Book != code)
            {
                throw new InvalidOperationException(
                    $"The file named {code} says it is {read.Book}. One of the two is wrong, and loading it " +
                    "under either name would put a book of the Septuagint where it does not belong.");
            }

            var chapters = read.Chapters.Select(Chapter).ToList();

            if (code == SecondEsdras.Code)
            {
                books.Add(Book(SecondEsdras.Ezra, ++position,
                    [.. chapters.Where(chapter => chapter.Number <= SecondEsdras.LastEzraChapter)]));
                books.Add(Book(SecondEsdras.Nehemiah, ++position,
                    [.. chapters
                        .Where(chapter => chapter.Number > SecondEsdras.LastEzraChapter)
                        .Select(chapter => chapter with
                        {
                            Number = chapter.Number - SecondEsdras.LastEzraChapter,
                        })]));
                continue;
            }

            books.Add(Book(canonical, ++position, chapters));
        }

        return new TextSource(Definition(), books);
    }

    private static BookDraft Book(int canonical, int position, IReadOnlyList<ChapterDraft> chapters) => new(
        CanonicalOrdinal: canonical,
        Position: position,
        Name: BookReferences.Name(canonical),
        Slug: BookReferences.Slug(canonical),
        Abbreviation: BookReferences.Abbreviation(canonical),
        Chapters: chapters);

    private static ChapterDraft Chapter(UsfmChapter chapter) => new(
        chapter.Number,
        [.. chapter.Verses.Select(verse => new VerseDraft(
            verse.Number,
            [.. verse.Words.Select(word => new WordDraft(word.Surface, word.Trailer))],
            verse.Label))]);

    /// <summary>
    /// Where a file of this edition starts in the shared canon. <c>EZR</c> is Esdras B and covers
    /// two books, so this answers with the first of them; <see cref="SecondEsdras"/> has the rest.
    /// </summary>
    public static int Canonical(string code) =>
        Canon.FirstOrDefault(entry => entry.Code == code) is { Canonical: > 0 } found
            ? found.Canonical
            : throw new InvalidOperationException(
                $"The Septuagint has no book \"{code}\". If Brenton prints one this list does not name, " +
                $"give it an ordinal in {nameof(BibleBookAbbreviation)} and a place in {nameof(Canons)} — " +
                "until then it cannot be addressed, which is not the same as it not existing.");

    /// <summary>
    /// The book code out of a filename like <c>41-TOBgrcbrent.usfm</c>: two digits, a hyphen, the
    /// code, then the edition's own suffix.
    /// </summary>
    private static string Code(string fileName)
    {
        var hyphen = fileName.IndexOf('-');
        var stem = hyphen < 0 ? fileName : fileName[(hyphen + 1)..];
        var suffix = stem.IndexOf("grcbrent", StringComparison.Ordinal);
        return suffix < 0 ? Path.GetFileNameWithoutExtension(stem) : stem[..suffix];
    }
}
