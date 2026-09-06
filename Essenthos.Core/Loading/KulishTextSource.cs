using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Usfm;

namespace Essenthos.Core.Loading;

/// <summary>
/// The Kulish–Puliui–Nechui-Levytsky Bible of 1903, the first complete Bible in Ukrainian.
///
/// It is here because of what it costs, which is nothing. DOC-0189 catalogues about twenty
/// Ukrainian translations and every one made after 1918 is owned by a Bible society, a religious
/// order or a mission, or is offered only under a share-alike licence this project cannot take.
/// This one needs nobody's permission: its three translators were all dead by 1918 and it was
/// printed a generation before any of the copyright lines that would otherwise apply.
///
/// One text and not two. eBible publishes a second Ukrainian Bible, <c>ukrfb</c>, and describes it
/// as this translation updated — but of the 33,887 lines in the two archives, 33,855 are identical
/// and most of the rest are words broken in half or letters transposed. It is a damaged copy of
/// this text rather than a revision of it, and a second witness that agrees with the first
/// everywhere except where it is wrong is not a witness. <c>Resources/Kulish/LICENCE.md</c> has the
/// comparison.
///
/// It arrives with no annotation of any kind — no lemmas, no morphology, no Strong numbers and no
/// alignment to anything. Nothing links it yet; that is a separate pass and a much larger one.
/// </summary>
internal static class KulishTextSource
{
    /// <summary>
    /// eBible's identifier, which is what the field spells this translation with.
    ///
    /// It names the Vienna New Testament of 1871 rather than the complete Bible, which makes it a
    /// worse description than a slug of our own would be — and a slug of our own would be one
    /// nobody else uses. The identifier a reader might paste in wins over the identifier that reads
    /// most accurately, and what the number means is said on the row instead.
    /// </summary>
    public const string Slug = "ukr1871";

    /// <summary>
    /// The 66 books in canonical order, by the code each file states in its <c>\id</c> line. The
    /// codes are USFM's, kept here for the same reason Brenton's are kept beside his edition rather
    /// than folded into the abbreviation table: that table matches alternatives by scanning and the
    /// first match wins, so a second naming scheme in it silently changes what an existing alias
    /// resolves to.
    ///
    /// Their order is the canonical one, so a book's place in this list is its ordinal and its
    /// position in the text alike. That is a property of this edition and is checked rather than
    /// assumed — the file names carry eBible's own numbering, which is neither.
    /// </summary>
    private static readonly string[] Canon =
    [
        "GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA", "1KI", "2KI", "1CH",
        "2CH", "EZR", "NEH", "EST", "JOB", "PSA", "PRO", "ECC", "SNG", "ISA", "JER", "LAM", "EZK",
        "DAN", "HOS", "JOL", "AMO", "OBA", "JON", "MIC", "NAM", "HAB", "ZEP", "HAG", "ZEC", "MAL",
        "MAT", "MRK", "LUK", "JHN", "ACT", "ROM", "1CO", "2CO", "GAL", "EPH", "PHP", "COL", "1TH",
        "2TH", "1TI", "2TI", "TIT", "PHM", "HEB", "JAS", "1PE", "2PE", "1JN", "2JN", "3JN", "JUD",
        "REV",
    ];

    /// <summary>
    /// The copyright page shipped inside the archive says <c>Public Domain</c> twice, and eBible's
    /// catalogue says <c>Copyright = "public domain"</c> and <c>Redistributable = "True"</c>.
    /// Neither statement names a rights holder. Kept beside the data as <c>copr.htm</c>, because a
    /// licence that lives only at a URL is one nobody can check offline.
    /// </summary>
    public static TextDefinition Definition => new(
        Slug: Slug,
        Name: "Kulish Bible",
        NameNative: "Сьвяте письмо Старого і Нового Завіту",
        Kind: TextKind.Translation,
        Language: "ukr",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.English,
        PublishedYear: 1903,
        SourceUrl: "https://ebible.org/find/details.php?id=ukr1871",
        RightsHolder: null,
        Licence: "Public Domain",
        LicenceUrl: "https://ebible.org/ukr1871/copyright.htm",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: null)
    {
        Translators =
            "Panteleimon Kulish (1819-1897), Ivan Puliui (1845-1918) and Ivan Nechui-Levytsky (1838-1918)",
        Editors = "The British and Foreign Bible Society, which published the complete Bible in 1903",
        Edition = "The 1905 printing, which is how eBible titles the file; it was not checked against a scan",
        EditionYear = 1905,
        About =
            "The first complete Bible in modern literary Ukrainian, and until 1962 the only one. "
            + "Panteleimon Kulish began it in the 1860s, when whether Ukrainian was a language "
            + "scripture could be read in was still an open question and publishing the answer was "
            + "easier abroad than at home: he and the physicist Ivan Puliui brought the New "
            + "Testament out in Vienna in 1871. The Old Testament manuscript burned at his farm at "
            + "Motronivka in November 1885 and he began it again, and it was still unfinished when "
            + "he died in 1897. Puliui and Ivan Nechui-Levytsky translated the books he had not "
            + "reached, and the British and Foreign Bible Society printed the whole Bible in 1903 "
            + "— accounts differ on whether the imprint reads Vienna or London. The language is "
            + "Kulish's own and so is the spelling, which is a century older than the standard one: "
            + "сьвіт where a reader today expects світ, насїннє for насіння. It is kept as it was "
            + "printed.",
        RightsNote =
            "Settled, unusually for a Ukrainian text, and settled by arithmetic rather than by "
            + "anybody's grant. Public domain is a claim about the law and not a licence someone "
            + "issues, so it is worth what the dates under it are worth: the longest of the three "
            + "translators' terms ran out in 1989 on life-plus-seventy, and publication in 1903 "
            + "puts it well the far side of the 1929 line that settles the same question in the "
            + "United States. Neither of eBible's two statements of the terms names a rights "
            + "holder, and no Bible society has ever claimed this edition.",
        Citation =
            "Сьвяте письмо Старого і Нового Завіту, translated by Panteleimon Kulish, Ivan Puliui and "
            + "Ivan Nechui-Levytsky, British and Foreign Bible Society, 1903, in the digital edition "
            + "eBible.org publishes as ukr1871.",
    };

    public static TextSource Read(string folder)
    {
        var books = Directory.GetFiles(folder, "*.usfm")
            .Select(path => UsfmReader.Read(File.ReadAllText(path)))
            .ToDictionary(book => book.Book, StringComparer.Ordinal);

        var drafts = new List<BookDraft>(Canon.Length);

        for (var ordinal = 1; ordinal <= Canon.Length; ordinal++)
        {
            var code = Canon[ordinal - 1];
            if (!books.TryGetValue(code, out var book))
            {
                throw new InvalidOperationException(
                    $"{Slug} is missing {code}, and this is a complete Bible: the fetch was partial, or the "
                    + "folder holds a different edition. Run scripts/fetch-kulish.ps1 rather than loading "
                    + "part of a text as though it were the whole of one.");
            }

            drafts.Add(new BookDraft(
                CanonicalOrdinal: ordinal,
                Position: ordinal,
                Name: BookReferences.Name(ordinal),
                Slug: BookReferences.Slug(ordinal),
                Chapters: [.. book.Chapters.Select(Chapter)],
                NameNative: book.Name,
                Abbreviation: BookReferences.Abbreviation(ordinal)));
        }

        return new TextSource(Definition, drafts);
    }

    private static ChapterDraft Chapter(UsfmChapter chapter) => new(
        chapter.Number,
        [.. chapter.Verses.Select(verse => new VerseDraft(
            verse.Number,
            [.. verse.Words.Select(word => new WordDraft(word.Surface, word.Trailer))],
            verse.Label))]);
}
