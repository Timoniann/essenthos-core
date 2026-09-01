using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;
using Essenthos.Core.XmlBible;

namespace Essenthos.Core.Loading;

/// <summary>
/// The translations, as bible4u publishes them: the King James, the Synodal and the Ukrainian
/// Bible, one reader for all three because they are one file format.
///
/// Every one of them is numbered the way the King James is — 150 psalms with 9 and 10 separate,
/// Malachi in four chapters, Joel in three — whatever the printed editions do. That was measured,
/// not assumed, and it is why the Synodal can be placed in the shared frame at all: the
/// versification data has no Russian scheme, and this file does not need one.
/// </summary>
internal static class Bible4uTextSource
{
    /// <summary>
    /// bible4u states its terms in every file, and they are the same three sentences in each:
    /// publisher "Public Domain", and a rights line permitting copying, modification and
    /// distribution for free so long as the Biblical content is unchanged.
    /// </summary>
    private const string Rights =
        "Everyone is permitted to copy, modify and distribute copies of this document for free as long as " +
        "it's Biblical content remains unchanged.";

    private static readonly Dictionary<string, TextDefinition> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["KJV"] = Definition("kjv", "King James Version", null, "eng", 1611, "Byzantine"),
        ["RUSV"] = Definition("rusv", "Russian Synodal Version", "Синодальный перевод", "rus", 1876, "Mixed"),
        ["UKR"] = Definition("ukr", "Ukrainian Bible", "Біблія", "ukr", 1962, null),
    };

    public static TextSource Read(string path, string identifier)
    {
        if (!Known.TryGetValue(identifier, out var definition))
        {
            throw new ArgumentException(
                $"There is no text definition for \"{identifier}\". A translation cannot be loaded without one: " +
                "its licence and provenance are part of the definition, not something filled in afterwards.",
                nameof(identifier));
        }

        return Build(new XmlBibleParser().Parse(File.ReadAllText(path)), definition);
    }

    public static TextSource Build(XmlBible.XmlBible bible, TextDefinition definition)
    {
        var books = new List<BookDraft>(bible.Books.Count);
        var position = 0;

        foreach (var book in bible.Books)
        {
            position++;
            var canonical = BibleBookAbbreviation.GetAbbreviation(book.BsName)
                            ?? BibleBookAbbreviation.GetByOrdinal(book.BNumber)
                            ?? throw new InvalidOperationException(
                                $"{definition.Slug} names a book \"{book.BsName}\" at number {book.BNumber} that " +
                                $"has no canonical ordinal. Add it to {nameof(BibleBookAbbreviation)} — until then " +
                                "this text cannot be placed beside any other.");

            books.Add(new BookDraft(
                CanonicalOrdinal: canonical.Ordinal,
                Position: position,
                Name: canonical.FullName.Full,
                Slug: Slugs.Of(canonical.StandardAbbreviation.Full),
                Chapters: book.Chapters
                    .Select(chapter => new ChapterDraft(
                        chapter.CNumber,
                        chapter.Verses.Select(Draft).ToList()))
                    .ToList(),
                NameNative: book.BName,
                Abbreviation: canonical.StandardAbbreviation.Full));
        }

        return new TextSource(definition, books);
    }

    /// <summary>
    /// The editorial markup goes before tokenising, once, so that the loader and anything reading
    /// the file afterwards cannot disagree about what a verse's words are: these files write the
    /// Hebrew numbering of a differently numbered psalm as "(22-1)" and mark a superscription with
    /// "^^", and tokenising that as it stands put "(", "22", "1" and ")" into the corpus as
    /// scripture.
    /// </summary>
    private static VerseDraft Draft(XmlBibleVerse verse) =>
        new(verse.VNumber, VerseWords.Parse(verse.Text)
            .Select(word => new WordDraft(word.Word, word.Trailer))
            .ToList());

    private static TextDefinition Definition(
        string slug,
        string name,
        string? nameNative,
        string language,
        int publishedYear,
        string? textualFamily) => new(
        Slug: slug,
        Name: name,
        NameNative: nameNative,
        Kind: TextKind.Translation,
        Language: language,
        Direction: TextDirection.LeftToRight,
        Versification: Versification.English,
        PublishedYear: publishedYear,
        SourceUrl: $"https://bible4u.net/static/bible_files/xml/{slug.ToUpperInvariant()}_xml.tar.gz",
        RightsHolder: null,
        Licence: Rights,
        LicenceUrl: "https://bible4u.net/",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: textualFamily);
}
