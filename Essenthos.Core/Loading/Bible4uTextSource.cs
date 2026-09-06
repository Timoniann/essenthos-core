using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;
using Essenthos.Core.XmlBible;

namespace Essenthos.Core.Loading;

/// <summary>
/// The translations, as bible4u publishes them: the King James, the Synodal and Ohienko's
/// Ukrainian Bible, one reader for all three because they are one file format.
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

    /// <summary>
    /// The King James, by the identifier the world spells it with. Named here because three
    /// loaders read this text by name — it is the English every entity annotation and every place
    /// join is read through — and a literal in each of them is three places to forget.
    /// </summary>
    public const string KingJames = "KJV";

    /// <summary>The Russian Synodal, spelled as Bible Gateway spells it.</summary>
    public const string Synodal = "RUSV";

    /// <summary>
    /// Ohienko's Ukrainian Bible.
    ///
    /// Not <c>UKR</c>, which is the ISO 639-2 code for the Ukrainian *language* and says nothing
    /// about which Ukrainian Bible this is — it named an edition only because bible4u's file is
    /// called that. UBIO is the identifier the field publishes this translation under: YouVersion
    /// carries it as version 186, bolls.life keys it <c>ubio</c>, and Door43 publishes the text as
    /// <c>uk_ubio</c>. Both older spellings stay reachable as aliases.
    /// </summary>
    public const string Ohienko = "UBIO";

    /// <summary>
    /// bible4u names its files by the language, not by the edition, and one of the three no longer
    /// agrees with the identifier we publish that text under. Keyed by the file, therefore, so the
    /// download and the slug can differ without either of them being guessed from the other.
    /// </summary>
    private static readonly Dictionary<string, TextDefinition> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["KJV"] = Definition("KJV", KingJames, "King James Version", null, "eng", 1611, "Byzantine") with
        {
            Translators = "The six companies of about forty-seven translators appointed by James VI and I",
            Edition = "The modern standard text, not the 1611 printing",
            EditionYear = 1769,
            About =
                "Translated 1604-1611 by six companies working at Westminster, Oxford and Cambridge, from the "
                + "Hebrew and the Greek printed editions of the day, and revised against the Bishops' Bible. "
                + "The file served here is not the 1611 printing: its spelling is modernised throughout, and "
                + "Ruth 3:15 reads \"and she went into the city\", the reading the 1762 Cambridge and 1769 "
                + "Oxford revisions introduced where 1611 has \"he\". Which of the later standard editions it "
                + "follows exactly has not been established.",
        },

        ["RUSV"] = Definition(
            "RUSV", Synodal, "Russian Synodal Version", "Синодальный перевод", "rus", 1876, "Mixed") with
        {
            Translators =
                "The four Orthodox theological academies of Saint Petersburg, Moscow, Kazan and Kiev, "
                + "under the Most Holy Synod of the Russian Orthodox Church",
            Editors = "Filaret (Drozdov), Metropolitan of Moscow, who had the final editorship",
            About =
                "Begun in 1813 under the Russian Bible Society, halted in 1826 when the Society was dissolved, "
                + "and resumed under Alexander II; the Synod approved translating the Old Testament from the "
                + "Masoretic Text in 1862, and the complete Bible appeared in 1876. Where the Septuagint has "
                + "words the Masoretic Text does not, the edition prints them in square brackets — 4,247 spans "
                + "of them, which are loaded as the words this edition supplies rather than as text.",
        },

        // The owner's question, and it had no answer in the row: "Ukrainian Bible" names no
        // translator, and 1962 is when the finished translation was first printed rather than when
        // it was made. Both are established here.
        ["UKR"] = Definition(
            "UKR", Ohienko, "Ohienko Bible", "Біблія в перекладі Івана Огієнка", "ukr", 1962, null) with
        {
            Translators = "Ivan Ohienko, Metropolitan Ilarion (1882-1972)",
            RightsHolder = "British and Foreign Bible Society, which published the 1962 edition",
            Edition = "The first complete edition, printed in London in 1962",
            About =
                "Ohienko began translating in 1917 and worked from the Hebrew and the Greek, deliberately "
                + "clear of Russianisms. He signed a contract with the British and Foreign Bible Society in "
                + "1936; the Gospels appeared in 1937 and the rest of the New Testament with the Psalms in "
                + "1939; the complete text was finished in 1940 and, delayed by the war, first printed in "
                + "London in 1962. That this file is his translation was established two ways: Genesis 1:1 "
                + "reads \"На початку Бог створив Небо та землю\", and the file is 99.4% token-identical, "
                + "verse by verse, with the uk_ubio text on Door43 whose every book header reads "
                + "\"Біблія в пер. Івана Огієнка, 1962\".",
            RightsNote =
                "Not settled. bible4u distributes the file as public domain, and CrossWire and Ukrainian "
                + "Wikisource say the same — but each of the three rests on the others rather than on a "
                + "grant. Against that: Ohienko died in 1972, and the sixteen Door43 files carrying the same "
                + "text head every book \"Copyright British and Foreign Bible Society\". Whether the Society "
                + "has released the 1962 edition has not been asked of them. Everything known about who made "
                + "it and who published it is recorded here in the meantime.",
            Citation =
                "Біблія в перекладі Івана Огієнка (Metropolitan Ilarion), first complete edition, "
                + "British and Foreign Bible Society, London, 1962.",
        },
    };

    /// <summary>Every translation this reader knows, by the identifier its file carries.</summary>
    public static IReadOnlyDictionary<string, TextDefinition> Definitions => Known;

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
    ///
    /// The Synodal's square brackets are markup too, and the largest of it: 4,247 spans over 3,708
    /// verses, which tokenised as text put a stray bracket on 8,413 words and made a bare "[" a
    /// word of its own 145 times. They are the edition saying which words are its own and not its
    /// base text's, so they leave the surface and become the spans the loader records.
    ///
    /// The "(22-1)" leaves the surface too, and is now kept rather than only removed. It is the one
    /// place either Slavic file records the numbering its own readers hold — bible4u renumbered
    /// both of them to the King James — so it becomes the verse's stated address, and a pane of the
    /// Synodal can then say its Psalm 119 is printed as 118 the way Brenton's pane already does.
    /// </summary>
    private static VerseDraft Draft(XmlBibleVerse verse) =>
        new(verse.VNumber, VerseWords.Parse(verse.Text)
            .Select(word => new WordDraft(
                word.Word,
                word.Trailer,
                Elided: word.Word.Length == 0,
                SuppliedSpan: word.SuppliedSpan))
            .ToList())
        {
            Stated =
            [
                .. VerseWords.StatedAddresses(verse.Text)
                    .Select(address => new StatedNumberDraft(address.Chapter, address.Number)),
            ],
            MarksASuperscription = VerseWords.MarksASuperscription(verse.Text),
            OpensBeforeItsStatedAddress = VerseWords.OpensBeforeItsStatedAddress(verse.Text),
        };

    /// <param name="file">
    /// What bible4u calls the download. It is not the slug and must not be derived from one: the
    /// Ukrainian is <c>UKR_xml.tar.gz</c> there whatever this project publishes the edition as, and
    /// a URL built from the slug would point at a file bible4u does not have.
    /// </param>
    private static TextDefinition Definition(
        string file,
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
        SourceUrl: $"https://bible4u.net/static/bible_files/xml/{file}_xml.tar.gz",
        RightsHolder: null,
        Licence: Rights,
        LicenceUrl: "https://bible4u.net/",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: textualFamily);
}
