using Essenthos.Core.Berean;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading;

/// <summary>
/// The Berean Standard Bible, 2022.
///
/// A modern English translation, in the public domain since 30 April 2023 — its own licensing page:
/// *"The Berean Bible and Majority Bible texts are officially placed into the public domain… Licensing
/// is not required for any use."* Attributed anyway, per RUL-0181: the obligation is the smaller half
/// of the reason, and the larger half is that a reader has to be able to tell what is ours.
///
/// <para>
/// **It is here for its tables, not for itself.** Every stated word-level correspondence this corpus
/// holds comes from one file and one small interlinear, and the King James New Testament reaches only
/// 72.7% of the Greek. The Berean publishes a row per original word with the English that renders it,
/// for the whole Bible, which makes it a second independent English anchor and the only calibration
/// set the New Testament has. That the translation is also good and readable is a bonus.
/// </para>
/// </summary>
internal static class BereanTextSource
{
    public const string Slug = "bsb";

    public static TextDefinition Definition { get; } = new(
        Slug: Slug,
        Name: "Berean Standard Bible",
        NameNative: null,
        Kind: TextKind.Translation,
        Language: "eng",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.English,
        PublishedYear: 2022,
        SourceUrl: "https://bereanbible.com/bsb.txt",
        RightsHolder: null,
        Licence: "Public Domain",
        LicenceUrl: "https://berean.bible/licensing.htm",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: null)
    {
        Citation = "The Holy Bible, Berean Standard Bible, BSB. Produced in cooperation with Bible Hub, " +
                   "Discovery Bible, unfoldingWord, Bible Aquifer and OpenBible.com. Public domain.",
    };

    /// <summary>
    /// Reads the published edition — <c>bsb.txt</c>, one verse a line, reference and text.
    ///
    /// A verse with no words is kept as an empty verse rather than dropped. The Berean prints the
    /// verses a critical text leaves out as numbers with nothing after them, and a reader who asks
    /// for Matthew 17:21 should be told it is empty here rather than that it does not exist.
    /// </summary>
    public static TextSource Read(string path)
    {
        var books = new Dictionary<int, Dictionary<int, List<VerseDraft>>>();
        var names = new Dictionary<int, string>();

        var unresolved = new List<string>();

        foreach (var (reference, text) in BereanWords.Verses(path))
        {
            if (!Address(reference, out var book, out var chapter, out var verse))
            {
                unresolved.Add(reference);
                continue;
            }

            names.TryAdd(book, BibleBookAbbreviation.GetByOrdinal(book)?.FullName.Full ?? reference);

            var words = BereanWords.Split(text)
                .Select(word => new WordDraft(word.Surface, word.Trailer))
                .ToList();

            if (!books.TryGetValue(book, out var chapters))
            {
                chapters = [];
                books[book] = chapters;
            }

            if (!chapters.TryGetValue(chapter, out var verses))
            {
                verses = [];
                chapters[chapter] = verses;
            }

            verses.Add(new VerseDraft(verse, words));
        }

        var drafts = books
            .OrderBy(book => book.Key)
            .Select((book, index) => new BookDraft(
                book.Key,
                index + 1,
                names[book.Key],
                Slugs.Of(names[book.Key]),
                [.. book.Value.OrderBy(chapter => chapter.Key)
                    .Select(chapter => new ChapterDraft(
                        chapter.Key,
                        [.. chapter.Value.OrderBy(v => v.Number)]))],
                Abbreviation: BibleBookAbbreviation.GetByOrdinal(book.Key)?.StandardAbbreviation.Full))
            .ToList();

        // Refused rather than dropped. This skipped every reference it could not resolve and said
        // nothing, and the Berean names its nineteenth book "Psalm" where the table knew only
        // "Psalms" — so 2,461 verses, a whole book, were quietly absent from a text that reported
        // itself as covering 1 to 66. A span hid it (PRB-0188); silence made it.
        if (unresolved.Count > 0)
        {
            throw new InvalidOperationException(
                $"{unresolved.Count} of the Berean's verse references name a book this corpus does not " +
                $"know — the first is \"{unresolved[0]}\". Loading the rest would publish a text with a " +
                "hole in it that nothing reports. Add the name the edition uses, or say why it is not " +
                "a book.");
        }

        return new TextSource(Definition, drafts);
    }

    /// <summary>
    /// <c>Genesis 1:1</c>, <c>1 Samuel 3:4</c>, <c>Song of Solomon 2:1</c> — the book name is
    /// whatever stands before the last space, because five of the sixty-six have a space in them.
    /// </summary>
    internal static bool Address(string reference, out int book, out int chapter, out int verse)
    {
        book = chapter = verse = 0;

        var lastSpace = reference.LastIndexOf(' ');
        var colon = reference.LastIndexOf(':');
        if (lastSpace <= 0 || colon <= lastSpace)
        {
            return false;
        }

        var name = reference[..lastSpace];
        if (BibleBookAbbreviation.GetAbbreviation(name) is not { } known)
        {
            return false;
        }

        book = known.Ordinal;
        return int.TryParse(reference[(lastSpace + 1)..colon], out chapter)
               && int.TryParse(reference[(colon + 1)..], out verse);
    }
}
