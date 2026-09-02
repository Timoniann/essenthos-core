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
/// Sixteen of its fifty-two books had no ordinal until FTR-0091, which is why this could not be
/// loaded before. Two of them are not extra books at all: Greek Esther and Greek Daniel are Esther
/// and Daniel, longer, so they take those books' canonical ordinals and their own versification.
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
    /// There is no Nehemiah. The Greek tradition prints Ezra and Nehemiah together as Esdras B,
    /// so what is here as <c>EZR</c> covers both, and the second half of it has no address in the
    /// frame yet. That is a versification question (TSK-0011), not a missing file.
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
    private static TextDefinition Definition() => new(
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
        TextualFamily: "Septuagint");

    public static TextSource Read(string folder)
    {
        var files = Directory.GetFiles(folder, "*.usfm")
            .ToDictionary(path => Code(Path.GetFileName(path)), path => path);

        var books = new List<BookDraft>(Canon.Length);
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

            position++;

            books.Add(new BookDraft(
                CanonicalOrdinal: canonical,
                Position: position,
                Name: BookReferences.Name(canonical),
                Slug: BookReferences.Slug(canonical),
                Abbreviation: BookReferences.Abbreviation(canonical),
                Chapters: [.. read.Chapters.Select(chapter => new ChapterDraft(
                    chapter.Number,
                    [.. chapter.Verses.Select(verse => new VerseDraft(
                        verse.Number,
                        [.. verse.Words.Select(word => new WordDraft(word.Surface, word.Trailer))],
                        verse.Label))]))]));
        }

        return new TextSource(Definition(), books);
    }

    /// <summary>Where a book of this edition stands in the shared canon.</summary>
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
