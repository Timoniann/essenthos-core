using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Usfm;

namespace Essenthos.Core.Loading;

/// <summary>
/// The English Bibles that are not the King James in other spelling.
///
/// The corpus already held one English translation and the temptation with a second is to take
/// whichever is best known. Each of these earns its place by being made from a different underlying
/// text or by a different method: Tyndale from Erasmus's Greek eighty years before the King James
/// existed, the Geneva independently from the Hebrew and Greek, the American Standard from the
/// nineteenth-century critical text, Young by rule rather than by idiom, the World English Bible
/// from the Byzantine Majority Text, and the JPS TaNaKH from the Masoretic text by translators
/// outside the Christian tradition entirely. Which text each follows was read out of the file
/// rather than repeated from the literature — see each definition.
///
/// None of them reaches the originals by itself. Three of the seven English packages at eBible
/// arrive tagged with Strong numbers and none of that tagging is loaded: it is a verse-level list
/// of the right numbers sprayed across every English token in the verse, so the number on a word is
/// not a claim that the word renders that lemma. <see cref="TaggingIsNotOursToTake"/> sets out what
/// was measured. So these six reach the Hebrew and Greek through this project's own aligner and
/// through nothing else — with Young's the one that should reach them best, because Young rendered
/// one lexeme by one lexeme far more consistently than any other version here.
///
/// The seventh English text the survey took, the Douay-Rheims, is not loaded. It is the only route
/// in this set to the Vulgate and to a full deuterocanon, and both are what stop it: it follows the
/// Latin numbering, its Psalms are the Vulgate's, and its Esther runs to sixteen chapters and its
/// Daniel to fourteen, where the additions the Greek and Latin carry inside a protocanonical book
/// have to be given somewhere to stand. That is TSK-0234's decision and not a reader's.
/// </summary>
internal static class EnglishTextSource
{
    /// <summary>
    /// Tyndale's New Testament of 1534, in the spelling he printed it in.
    ///
    /// Which of the two matters, and so does whether the transcription was modernised: several
    /// digital Tyndales are in modern spelling and would be a paraphrase of the thing that makes
    /// this text worth holding. This one is not. Matthew 1:1 reads "This is the boke of the
    /// generacion of Iesus Christ the sonne of Dauid", and John 3:16 "For God so loveth the worlde
    /// that he hath geven his only sonne" — Tyndale's own orthography, with u for v and i for j.
    /// </summary>
    public const string Tyndale = "TYN1534";

    /// <summary>
    /// The Geneva Bible of 1599, the English Bible before the King James.
    ///
    /// Proved from the reading that named it: Genesis 3:7 ends "and made them selues breeches",
    /// which is the Geneva's and nobody else's — it is why the edition is called the Breeches
    /// Bible. The spelling is original throughout, "knewe" and "figge tree leaues" in the same
    /// verse, so this is a transcription of the printed text rather than a modernised one.
    /// </summary>
    public const string Geneva = "GNV1599";

    /// <summary>
    /// The American Standard Version of 1901, the King James line re-based on the critical Greek.
    ///
    /// The file carries its own title page — "BEING THE VERSION SET FORTH A.D. 1611, COMPARED WITH
    /// THE MOST ANCIENT AUTHORITIES AND REVISED A.D. 1881-1885, Newly Edited by the American
    /// Revision Committee A.D. 1901, STANDARD EDITION" — and the American Revisers' preface.
    /// </summary>
    public const string AmericanStandard = "ASV";

    /// <summary>
    /// Young's Literal Translation of 1898, and the reason this whole set is worth loading.
    ///
    /// Young translated by rule: Hebrew verb aspect rendered the same way regardless of what
    /// English does with it, source word order kept wherever English bears it, one lexeme to one
    /// lexeme. Genesis 1:1 reads "In the beginning of God's preparing the heavens and the earth"
    /// and John 3:16 ends "may have life age-during" — neither is idiomatic English and both are
    /// what a word-for-word rendering of the original produces.
    /// </summary>
    public const string Young = "YLT";

    /// <summary>
    /// The World English Bible, whose New Testament is made from the Byzantine Majority Text.
    ///
    /// Not the Classic edition, which is a different text under the same name: eBible publishes
    /// five packages called the World English Bible, and this is the updated one with the
    /// protocanon only, which renders the divine name as LORD where the Classic renders it Yahweh —
    /// 6,902 times in the Classic against 110 here. Established by fetching both.
    /// </summary>
    public const string WorldEnglish = "WEB";

    /// <summary>
    /// The Jewish Publication Society's TaNaKH of 1917, the Old Testament and no New.
    ///
    /// The 1917 and not the 1985: the later one is the JPS's and is licensed, and the two are
    /// separate translations rather than revisions of each other. This one prints the divine name
    /// as LORD, transliterates where a Christian version translates — Genesis 22:14 names the place
    /// Adonai-jireh — and punctuates speech with single quotation marks throughout.
    /// </summary>
    public const string JewishPublicationSociety = "JPS1917";

    /// <summary>
    /// Each text's folder under <c>Resources</c>, the definition it loads with, and the canonical
    /// ordinals it holds.
    ///
    /// The range is declared rather than inferred from the files, so a partial download is a
    /// failure instead of a shorter Bible. Two of the six are not whole Bibles and are not meant to
    /// be: Tyndale printed a New Testament, and the JPS TaNaKH has no New Testament to print.
    /// </summary>
    private static readonly Dictionary<string, Text> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tyndale1534"] = new(FirstBook: 40, LastBook: 66, Definition(
            Tyndale, "engtnt", "Tyndale New Testament", "eng", 1526, "Byzantine") with
        {
            Translators = "William Tyndale (c. 1494-1536)",
            Edition = "The revised New Testament of 1534, in its original spelling",
            EditionYear = 1534,
            About =
                "The first English New Testament translated from the Greek and the first printed in "
                + "English at all — at Worms in 1526, and again in his own revision of 1534, which "
                + "is what is here — and the text the King James is largely a revision of: the "
                + "committees of 1611 kept Tyndale's wording wherever they could, so the corpus has "
                + "held the revision for as long as it has held any English and never the thing "
                + "revised. Tyndale worked from Erasmus's Greek, which is why the readings the "
                + "Textus Receptus carries are all here — Acts 8:37, Matthew 17:21, John 5:4, the "
                + "heavenly witnesses of 1 John 5:7 and the doxology of the Lord's Prayer. He was "
                + "strangled and burned at Vilvoorde in 1536 for having made it. "
                + "The spelling is his own and is kept: boke, sonne, Iesus, geven. "
                + "Its verse numbers are not his — Stephanus first numbered verses in 1551, "
                + "seventeen years after this printing — so they are eBible's fitting of a later "
                + "division onto an earlier text, and three of the slots come out empty.",
            RightsNote =
                "Public domain by four centuries and stated so by eBible in all three of its places. "
                + "The one thing worth checking here is not the text but the transcription, and it "
                + "is: CrossWire reads one Wikisource transcription two ways, stamping its Tyndale "
                + "Public Domain and its Wycliffe from the same pages CC BY-SA 4.0, and both "
                + "readings of one source cannot be right. eBible's assertion is made on its own "
                + "behalf with nothing upstream of it, so it is the one taken.",
            Citation =
                "The New Testament, translated by William Tyndale, 1534, in the digital edition "
                + "eBible.org publishes as engtnt.",
        }),

        ["Geneva1599"] = new(FirstBook: 1, LastBook: 66, Definition(
            Geneva, "enggnv", "Geneva Bible", "eng", 1560, "Byzantine") with
        {
            Translators =
                "William Whittingham (c. 1524-1579), Anthony Gilby (c. 1510-1585) and the other "
                + "Marian exiles at Geneva",
            Edition = "The 1599 edition, in its original spelling and without the marginal notes",
            EditionYear = 1599,
            About =
                "The Bible England read for two generations before the King James, and the one the "
                + "Pilgrims took to Massachusetts. The exiles who fled Mary Tudor made it at Geneva "
                + "from the Hebrew and the Greek rather than from any English predecessor, printed "
                + "it in roman type with verse numbers and a marginal commentary, and made it the "
                + "first English Bible a household could afford. Genesis 3:7 has Adam and Eve sew "
                + "themselves breeches, which is what the edition is nicknamed for. The notes are "
                + "not in this file and their absence is the edition being served, not a loss: what "
                + "is loaded is the translation. Its verse division is its own in sixty-six places, "
                + "keeping the Hebrew seams the King James later smoothed — Daniel 3 runs to 33 "
                + "verses where the English numbering starts chapter 4, and Job 39 to 38.",
            RightsNote =
                "Public domain and stated so by eBible in all three places, with the copyright page "
                + "adding that the spelling is the original and not modern English. CrossWire also "
                + "distributes a module called geneva; that is the Geneva Bible Translation Notes, "
                + "the marginal commentary rather than the text, and its configuration states no "
                + "distribution licence at all.",
            Citation =
                "The Bible and Holy Scriptures conteyned in the Olde and Newe Testament, Geneva, "
                + "1599 edition, in the digital edition eBible.org publishes as enggnv.",
        }),

        ["AmericanStandard1901"] = new(FirstBook: 1, LastBook: 66, Definition(
            AmericanStandard, "eng-asv", "American Standard Version", "eng", 1901, "Alexandrian") with
        {
            Translators = "The American Revision Committee, from the English Revised Version of 1881-1885",
            Editors = "George E. Day and J. Henry Thayer, secretaries of the two companies",
            Edition = "The Standard American Edition, which is the only one there is",
            About =
                "The King James line brought forward onto the manuscripts the nineteenth century "
                + "found. The English and American revisers worked together from 1870, the English "
                + "companies held the deciding vote, and the American preferences were printed as "
                + "an appendix until the fourteen-year embargo ran out — whereupon the Americans "
                + "published their own recension, which is this. Its Greek is the critical text and "
                + "the difference is visible by counting: sixteen verse slots stand empty where the "
                + "Textus Receptus prints a verse, Acts 8:37, Matthew 17:21 and John 5:4 among "
                + "them, the heavenly witnesses of 1 John 5:7 are gone and so is the doxology of "
                + "the Lord's Prayer. It renders the divine name as Jehovah throughout, which is "
                + "the other thing a reader notices first. It marks 4,316 spans as words the "
                + "translators supplied, the italics of the printed edition.",
            RightsNote =
                "Public domain and stated so by eBible in all three places, and by the file itself: "
                + "its front matter carries Thomas Nelson & Sons' 1901 copyright notice and eBible's "
                + "note that the copyright has expired. The Strong tagging that arrives with it is "
                + "not eBible's to give and is not loaded — see the reader.",
            Citation =
                "The Holy Bible, American Standard Version, Thomas Nelson & Sons, 1901, in the "
                + "digital edition eBible.org publishes as eng-asv.",
        }),

        ["Young1898"] = new(FirstBook: 1, LastBook: 66, Definition(
            Young, "engylt", "Young's Literal Translation", "eng", 1862, "Byzantine") with
        {
            Translators = "Robert Young (1822-1888)",
            Edition = "The revised edition of 1898, published after Young's death",
            EditionYear = 1898,
            About =
                "The most useful English text this corpus holds and one of the least readable. "
                + "Robert Young, who compiled the analytical concordance that still carries his "
                + "name, translated the whole Bible on the principle that a translation should "
                + "reproduce the original's grammar rather than replace it: the Hebrew perfect "
                + "becomes an English past whether or not the sentence tolerates it, the "
                + "participle stays a participle, and one Hebrew or Greek lexeme is rendered by one "
                + "English word wherever he could manage it. \"In the beginning of God's preparing "
                + "the heavens and the earth\" is Genesis 1:1 and \"life age-during\" is what John "
                + "3:16 promises. His Greek is the Textus Receptus. What makes it worth holding is "
                + "that its awkwardness is a cost to a reader and a gift to an aligner: where an "
                + "aligner disagrees with Young about which original word an English word renders, "
                + "the disagreement is worth reading rather than smoothing over.",
            RightsNote =
                "Public domain and stated so by eBible in all three places. Young died in 1888 and "
                + "the revision this file holds was printed in 1898.",
            Citation =
                "The Holy Bible, Containing the Old and New Covenants, Literally and Idiomatically "
                + "Translated out of the Original Languages, by Robert Young, revised edition 1898, "
                + "in the digital edition eBible.org publishes as engylt.",
        }),

        ["WorldEnglish"] = new(FirstBook: 1, LastBook: 66, Definition(
            WorldEnglish, "engwebp", "World English Bible", "eng", null, "Byzantine") with
        {
            Translators =
                "Michael Paul Johnson and the volunteers of eBible.org, revising the American "
                + "Standard Version of 1901",
            Edition =
                "The updated edition, protocanon only, which renders the divine name as LORD; the "
                + "copy loaded is eBible's of 2026-08-26",
            About =
                "The only modern English translation here, and the one English text in the corpus "
                + "whose New Testament comes from the Byzantine Majority Text — Robinson and "
                + "Pierpont's and Hodges and Farstad's reconstructions, which is what makes it the "
                + "English partner to the Greek Byzantine Textform already loaded. Its own preface "
                + "says what it is: an update of the American Standard Version, archaic forms "
                + "replaced by a program and then proofread by volunteers, with the New Testament "
                + "conformed in places to the Byzantine text. So it is not an independent witness "
                + "to the American Standard but a revision of it, and a reader comparing the two is "
                + "reading a text beside its own descendant. "
                + "The Byzantine base is legible in the file rather than only in the preface: Acts "
                + "8:37 and the heavenly witnesses are absent, which the Textus Receptus prints, "
                + "while Matthew 17:21, John 5:4 and the doxology of the Lord's Prayer are present, "
                + "which the critical text does not print — the combination belongs to neither of "
                + "the other two. Romans ends its doxology at 14:24-26 rather than 16:25-27, which "
                + "is where the Byzantine manuscripts put it, and is the only place its verse "
                + "addresses differ from the American Standard's at all. "
                + "It has no publication year, which is why the row states none: it is revised "
                + "continuously, and eBible's own three statements about which edition this is "
                + "disagree — the copyright page inside the archive still reads 2020, the catalogue "
                + "names the module engweb2025peb, and the files are dated 2026-08-26.",
            RightsNote =
                "Public domain in all three of eBible's places and dedicated to it deliberately "
                + "rather than by expiry, which is the whole reason the translation exists. The one "
                + "qualification is a trademark and not a copyright: eBible asks that a changed "
                + "text not be called the World English Bible. Tokenising it and normalising its "
                + "punctuation is a change, so nothing served from these rows is the World English "
                + "Bible — it is this corpus's reading of it, which is what the row says of every "
                + "text here.",
            Citation =
                "The World English Bible, eBible.org, updated edition, in the digital edition "
                + "eBible.org publishes as engwebp. World English Bible is a trademark of eBible.org.",
        }),

        ["Jps1917"] = new(FirstBook: 1, LastBook: 39, Definition(
            JewishPublicationSociety, "engjps", "JPS TaNaKH", "eng", 1917, "Masoretic") with
        {
            Translators =
                "The board of editors of the Jewish Publication Society under Max Margolis (1866-1932)",
            Editors = "The Jewish Publication Society of America, Philadelphia",
            Edition = "The Old JPS, and not the separate translation of 1972-1985 that shares its name",
            About =
                "The first English Bible made by Jews for Jews, and the first text in this corpus "
                + "that is not a Christian translation. A board under Max Margolis worked from the "
                + "Masoretic text for a decade and published in 1917, and what came out is close "
                + "enough to the King James in register to be read beside it and independent enough "
                + "in exegesis to be worth reading beside it: measured against the King James "
                + "verse by verse after normalisation, about one verse in six is word for word the "
                + "same and five in six are not. It has no New Testament, which is not an omission. "
                + "The divine name is LORD, the names are transliterated where a Christian version "
                + "translates them — Genesis 22:14 names the place Adonai-jireh — and speech is set "
                + "in single quotation marks. "
                + "It is numbered here the way the King James is, because eBible renumbered it; "
                + "what it says about its own numbering is kept, 2,056 verses carrying the "
                + "Masoretic address they hold at home, 1,049 of them in the Psalms, where the "
                + "Hebrew counts the superscription as verse one and the English does not.",
            RightsNote =
                "Public domain and stated so by eBible in all three places, with the copyright page "
                + "pointing anyone wanting the 1972-1985 JPS at jps.org, which holds it. Whose "
                + "digitisation this is matters as much as whose translation: Mechon-Mamre claims "
                + "copyright over its own transcription of the same 1917 text, and Sefaria's JPS "
                + "directory holds several editions of which only the 1917 row is public domain. "
                + "This copy is eBible's own and its public-domain assertion is eBible's own.",
            Citation =
                "The Holy Scriptures According to the Masoretic Text, Jewish Publication Society of "
                + "America, 1917, in the digital edition eBible.org publishes as engjps.",
        }),
    };

    /// <summary>Every text this reader knows, by the folder its files are fetched into.</summary>
    public static IReadOnlyDictionary<string, TextDefinition> Definitions { get; } =
        Known.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Definition,
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The Strong tagging three of these arrive with, and why none of it is loaded.
    ///
    /// The American Standard carries 705,378 tags and the World English Bible 683,868, and eBible
    /// names no tagger and no terms for either. That is the same silence the Spanish tagging came
    /// under, and there the layer turned out to be a named third party's; here the layer fails on
    /// its own merits before the question of whose it is arises, which is why it is refused rather
    /// than investigated further.
    ///
    /// It is not a word-level claim. Genesis 1:1 of the American Standard tags "In" and "God" and
    /// "earth" all with H8064, which is the Hebrew for heavens, and does not use H430 for God
    /// anywhere in the verse. Over the whole Old Testament it puts 23.26 tags on an average verse
    /// drawn from a pool of only 8.44 distinct numbers, where Luther 1912 — a tagging made by hand
    /// — puts 10.73 tags from 9.53 distinct numbers on the same verses. So each Hebrew word's
    /// number is smeared across about three English tokens, and 59% of the tags stand on English
    /// function words: "and" carries H1121, son, 7,038 times, and "the" carries H5921, upon, 7,079
    /// times. Luther puts 1.8% of its tags on a function word.
    ///
    /// The verse-level pool is broadly right — 80% of the American Standard's tags name a number
    /// Luther also uses somewhere in that verse — and proper nouns land correctly, which is what
    /// makes the layer look usable until it is measured. That is exactly the failure RUL-0024 names:
    /// an inference persisted where a reader would take it for something the edition stated.
    ///
    /// The same layer is under both texts. Where the two tag an identical sequence of words, 83% of
    /// the time the sequence of numbers is identical too, and their per-verse number sets agree
    /// 95.3% of the time across the Old Testament.
    /// </summary>
    private static readonly HashSet<string> TaggingIsNotOursToTake =
        new(["AmericanStandard1901", "WorldEnglish"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The 66 books in canonical order, by the code each file states in its <c>\id</c> line, so
    /// that a book's place in this list is its canonical ordinal. The file names carry eBible's own
    /// numbering, which is neither the ordinal nor the position.
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

    /// <param name="folder">
    /// The folder the fetch script wrote, which is also the key of the definition: one text per
    /// folder, so a reader cannot be pointed at the Geneva and told to load Young's.
    /// </param>
    public static TextSource Read(string folder)
    {
        var name = new DirectoryInfo(folder).Name;
        if (!Known.TryGetValue(name, out var text))
        {
            throw new ArgumentException(
                $"There is no text definition for the folder \"{name}\". A translation cannot be loaded " +
                "without one: its licence and provenance are part of the definition, not something filled " +
                "in afterwards.",
                nameof(folder));
        }

        var books = Directory.GetFiles(folder, "*.usfm")
            .Select(File.ReadAllText)
            .Where(IsScripture)
            .Select(UsfmReader.Read)
            .ToDictionary(book => book.Book, StringComparer.Ordinal);

        var tagged = !TaggingIsNotOursToTake.Contains(name);
        var drafts = new List<BookDraft>(text.LastBook - text.FirstBook + 1);

        for (var ordinal = text.FirstBook; ordinal <= text.LastBook; ordinal++)
        {
            var code = Canon[ordinal - 1];
            if (!books.TryGetValue(code, out var book))
            {
                throw new InvalidOperationException(
                    $"{text.Definition.Slug} is missing {code}, and this edition holds books "
                    + $"{text.FirstBook} to {text.LastBook} of the canon: the fetch was partial, or the "
                    + "folder holds a different edition. Run scripts/fetch-ebible.ps1 rather than loading "
                    + "part of a text as though it were the whole of one.");
            }

            drafts.Add(new BookDraft(
                CanonicalOrdinal: ordinal,
                Position: ordinal - text.FirstBook + 1,
                Name: BookReferences.Name(ordinal),
                Slug: BookReferences.Slug(ordinal),
                Chapters: [.. book.Chapters.Select(chapter => Chapter(chapter, tagged))],
                NameNative: book.Name,
                Abbreviation: BookReferences.Abbreviation(ordinal)));
        }

        return new TextSource(text.Definition, drafts);
    }

    /// <summary>
    /// The codes eBible gives a file that is not scripture — a title page, a preface, a glossary.
    /// The fetch keeps none of them, so this only matters for a folder somebody filled by hand;
    /// without it the reader would meet the dozen markers a preface uses and refuse the whole text
    /// over its front matter.
    /// </summary>
    private static readonly string[] NotScripture = ["FRT", "INT", "GLO", "BAK"];

    private static bool IsScripture(string content) =>
        !NotScripture.Any(code => content.StartsWith($"\\id {code}", StringComparison.Ordinal));

    private static ChapterDraft Chapter(UsfmChapter chapter, bool tagged) => new(
        chapter.Number,
        [.. chapter.Verses.Select(verse => new VerseDraft(
            verse.Number,
            [.. verse.Words.Select(word => new WordDraft(
                word.Surface,
                word.Trailer,
                StrongNumber: tagged ? word.StrongNumber : null,
                SuppliedSpan: word.SuppliedSpan))],
            verse.Label)
        {
            Notes = [.. verse.Notes.Select(note => new VerseNoteDraft(
                note.Kind == UsfmNoteKind.Footnote ? VerseNoteKind.Footnote : VerseNoteKind.CrossReference,
                note.Content,
                note.AnchorWordPosition))],
            Stated = [.. verse.Stated.Select(address => new StatedNumberDraft(address.Chapter, address.Number))],
            OpensBeforeItsStatedAddress = verse.OpensBeforeItsStatedAddress,
        })]);

    /// <param name="FirstBook">The first canonical ordinal this edition holds, 1 for a whole Bible.</param>
    /// <param name="LastBook">The last, 66 for a whole Bible.</param>
    private sealed record Text(int FirstBook, int LastBook, TextDefinition Definition);

    /// <param name="translation">
    /// eBible's identifier, which is what the download and all three statements of the licence are
    /// keyed by. It is not the slug: the field spells these editions ASV, YLT and WEB, and an
    /// identifier nobody else uses is one nobody types.
    /// </param>
    /// <param name="publishedYear">
    /// Null where the text has no single one. The World English Bible is revised continuously and
    /// its own files carry three different answers, so a year on that row would be a guess with the
    /// authority of a column.
    /// </param>
    private static TextDefinition Definition(
        string slug,
        string translation,
        string name,
        string language,
        int? publishedYear,
        string textualFamily) => new(
        Slug: slug,
        Name: name,
        NameNative: null,
        Kind: TextKind.Translation,
        Language: language,
        Direction: TextDirection.LeftToRight,
        Versification: Versification.English,
        PublishedYear: publishedYear,
        SourceUrl: $"https://ebible.org/find/details.php?id={translation}",
        RightsHolder: null,
        Licence: "Public Domain",
        LicenceUrl: $"https://ebible.org/{translation}/copyright.htm",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: textualFamily);
}
