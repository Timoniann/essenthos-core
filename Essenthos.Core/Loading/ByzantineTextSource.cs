using System.Text.Json;
using Essenthos.Core.Byzantine;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading;

/// <summary>
/// Robinson and Pierpont's Byzantine Textform, which is the reading of the majority of surviving
/// Greek manuscripts, established by counting them rather than by weighing a few early ones.
///
/// It is not a third Textus Receptus, and the distinction is the reason to hold it. The corpus
/// labels Scrivener 1894 and Stephanus 1550 Byzantine, and that label does more work than it should:
/// the Received Text is Erasmus's, assembled in 1516 from about half a dozen late minuscules that
/// happened to be in Basel, one of them borrowed for Revelation and missing its last leaf. It sits
/// inside the Byzantine tradition without being a statement of what that tradition reads. This is
/// that statement — so a reader asking what most manuscripts actually say here has, for the first
/// time, something to open.
///
/// It costs almost nothing to join to the rest. Every one of its words carries a Strong number, and
/// so do Nestle 1904, Scrivener and Stephanus, so the loader that already pairs two Greek editions
/// on the numbers both of them state reaches this one with no new alignment and no new guess.
/// </summary>
internal static class ByzantineTextSource
{
    public const string Slug = "robinsonpierpont2018";

    /// <summary>
    /// The file stem of each book. The repository numbers them in canonical order and the New
    /// Testament starts at 40, so the position in this list is the only ordering there is to keep.
    /// </summary>
    private static readonly string[] Canon =
    [
        "01_MAT", "02_MAR", "03_LUK", "04_JOH", "05_ACT", "06_ROM", "07_1CO", "08_2CO", "09_GAL",
        "10_EPH", "11_PHP", "12_COL", "13_1TH", "14_2TH", "15_1TI", "16_2TI", "17_TIT", "18_PHM",
        "19_HEB", "20_JAM", "21_1PE", "22_2PE", "23_1JO", "24_2JO", "25_3JO", "26_JUD", "27_REV",
    ];

    /// <summary>Matthew, and therefore the offset from this edition's order to the shared canon.</summary>
    private const int FirstCanonicalOrdinal = 40;

    public static IReadOnlyList<string> Books => Canon;

    /// <summary>
    /// Public domain by both statements attached to the bytes, which agree — unusually enough to be
    /// worth saying. <c>LICENSE.txt</c> is the Unlicense, and the README's final section reads
    /// <c>All the code and text contained in this folder is in the Public Domain</c>. No
    /// ShareAlike, no NonCommercial, no attribution condition.
    ///
    /// What could not be found is a first-person statement by Robinson placing this edition in the
    /// public domain; the claim is the repository maintainers' about his files. His own permission
    /// notice for the Byzantine Textform asserts a compilation copyright in its header and then
    /// releases all rights to it two paragraphs later, and asks — without requiring — that the
    /// editors' names and the title stay with the text. That request is honoured here.
    /// </summary>
    public static TextDefinition Definition => new(
        Slug: Slug,
        Name: "Robinson-Pierpont Byzantine Textform",
        NameNative: "Η ΚΑΙΝΗ ΔΙΑΘΗΚΗ",
        Kind: TextKind.CriticalEdition,
        Language: "grc",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.English,
        PublishedYear: 1991,
        SourceUrl: "https://github.com/byztxt/byzantine-majority-text",
        RightsHolder: null,
        Licence: "Unlicense",
        LicenceUrl: "https://github.com/byztxt/byzantine-majority-text/blob/master/LICENSE.txt",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: "Byzantine")
    {
        Editors = "Maurice A. Robinson and William G. Pierpont",
        Edition = "The 2018 edition, current as of 20 July 2023; the repository's release 3.3.2",
        EditionYear = 2018,
        About = "The reading of the majority of the surviving Greek manuscripts, which is a different "
                + "claim from any of the other three Greek editions here. Nestle 1904 is a critical text: "
                + "a few early manuscripts, weighed. Scrivener and Stephanus are the Received Text, which "
                + "descends from the handful of late minuscules Erasmus could reach in Basel in 1516. "
                + "Robinson and Pierpont went instead to what the tradition as a whole transmits, and "
                + "where the manuscripts divide among themselves they printed the division rather than "
                + "resolving it. First published in 1991; this is the 2018 edition, which Robinson "
                + "recommends over the 2005 because that one carried accent, breathing and punctuation "
                + "errors introduced by porting a critical-text file and altering only where the "
                + "Byzantine text differed.",
        RightsNote = "The public-domain statement is the repository maintainers' about Robinson's "
                     + "files rather than Robinson's own about this edition. His own permission notice "
                     + "for the Byzantine Textform contradicts itself — a 2005 compilation copyright in "
                     + "the header, all rights released two paragraphs later — and asks that the editors' "
                     + "names, the title and the disclaimer be kept with any reproduction. That is a "
                     + "request rather than a condition, and it is met.",
        Citation = "Robinson, M. A., & Pierpont, W. G. (2018). The New Testament in the Original "
                   + "Greek: Byzantine Textform.",
    };

    public static TextSource Read(string folder)
    {
        var books = new List<BookDraft>(Canon.Length);

        for (var position = 0; position < Canon.Length; position++)
        {
            var canonical = FirstCanonicalOrdinal + position;
            var verses = Bp5Reader.Read(
                File.ReadAllText(Path.Combine(folder, "strongs", $"{Canon[position]}.BP5")));

            var name = BibleBookAbbreviation.GetByOrdinal(canonical)?.FullName.Full
                       ?? throw new InvalidOperationException(
                           $"The Byzantine Textform book {Canon[position]} is canonical number {canonical}, " +
                           $"which has no name. Add it to {nameof(BibleBookAbbreviation)} — until then this " +
                           "text cannot be placed beside any other.");

            books.Add(new BookDraft(
                CanonicalOrdinal: canonical,
                Position: position + 1,
                Name: name,
                Slug: Slugs.Of(name),
                Chapters: [.. verses
                    // Four addresses in the file carry no words: Luke 17:36 and Acts 8:37, 15:34
                    // and 24:7, which the majority of the manuscripts do not have and the Received
                    // Text does. A verse row means the text has that verse, so this edition gets
                    // none of the four — the same thing the Nestle load does with the sixteen
                    // verses the critical text omits.
                    .Where(v => v.Words.Count > 0)
                    .GroupBy(v => v.Chapter)
                    .OrderBy(chapter => chapter.Key)
                    .Select(chapter => new ChapterDraft(
                        chapter.Key,
                        [.. chapter.OrderBy(v => v.Number).Select(Verse)]))]));
        }

        return new TextSource(Definition, books);
    }

    private static VerseDraft Verse(Bp5Verse verse) =>
        new(verse.Number, [.. verse.Words.Select((word, at) => new WordDraft(
            Surface: Bp5BetaCode.ToGreek(word.Surface),
            Trailer: at == verse.Words.Count - 1 ? string.Empty : " ",
            StrongNumber: $"G{word.Strong}",
            Morphology: Morphology(word)))]);

    /// <summary>
    /// Robinson's parse code, kept as he wrote it rather than expanded, which is what the Textus
    /// Receptus load does with the same codes. Nothing here reads them yet, and a code expanded by a
    /// guess is worse than one left alone.
    /// </summary>
    private static string Morphology(Bp5Word word) =>
        JsonSerializer.Serialize(new Dictionary<string, string>(1) { ["robinson"] = word.Morphology });
}
