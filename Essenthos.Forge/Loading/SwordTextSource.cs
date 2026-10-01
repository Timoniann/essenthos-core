using System.Globalization;
using System.Text;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Sword;

namespace Essenthos.Core.Loading;

/// <summary>How a module's words are told apart, which is a question about its language.</summary>
internal enum SwordSegmentation
{
    /// <summary>
    /// Chinese prints no space between words, so a word is what the edition's own tagging says one
    /// is: each <c>&lt;w&gt;</c> element is a word, and a run of characters no element claims is one
    /// word up to the next punctuation mark. The spaces CrossWire writes between elements are its
    /// markup and are dropped; the full-width space the edition prints before 神 is kept.
    /// </summary>
    Tagged,

    /// <summary>
    /// A language that writes spaces between words: a word is a run of letters, and what stands
    /// between two of them is the first one's trailer.
    /// </summary>
    Spaced,
}

/// <param name="Folder">Where the module's root is, under the corpus sources.</param>
/// <param name="Renumbered">
/// Verses the module stores at the edition's own number where the English frame numbers the same
/// words otherwise: each is moved to the English address and records the edition's.
/// </param>
internal sealed record SwordText(
    string Folder,
    SwordSegmentation Segmentation,
    TextDefinition Definition,
    IReadOnlyList<((int Book, int Chapter, int Verse) Stored, (int Book, int Chapter, int Verse) English)>? Renumbered = null)
{
    /// <summary>
    /// Whether the module writes the Faith Hope Love foundation's own numbers for the Hebrew
    /// prefixes, which are read as the corpus's (<see cref="UnionStrongNumbers"/>). Every other
    /// module numbers only what Strong's dictionaries hold.
    /// </summary>
    public bool FhlPrefixes { get; init; }
}

/// <param name="Numbers">
/// The Strong numbers the edition writes on the element the word came from, in the corpus's series;
/// empty for a word no element claims. Never stored: <see cref="UnionStrongLinkLoader"/> reads them.
/// </param>
/// <param name="Unit">The element the word came from, so a phrase tagged once is one rendering.</param>
internal sealed record SwordWord(string Surface, string Trailer, IReadOnlyList<string> Numbers, int Unit, bool Elided);

/// <summary>
/// The Chinese Union Version and the Korean Revised Version, as CrossWire publishes them in SWORD
/// modules — the one digital source of each whose terms are stated where the bytes are and agree
/// with the rights holder's own statement.
///
/// <para>
/// **Which Chinese Union Version.** Two are published as public domain, and only one is. eBible's
/// <c>cmn-cu89s</c> is titled 新标点和合本, the Hong Kong Bible Society's new-punctuation edition of
/// 1988, and its text is that edition's: Genesis 49:3 reads 吕便, the society's modern name for
/// Reuben, where the 1919 printing has 流便. That edition is © the society, so it is not taken. FHL's
/// text, which CrossWire rebuilt its modules from in 2021, was typed in 1995 from the 1919 printing,
/// and FHL lists 2,203 places where it differs from the 1988 one. It is not a facsimile either: FHL
/// added modern punctuation, replaced the old transliterations of place names with the ones Taiwan's
/// National Institute for Compilation and Translation uses (大馬士革 for 大馬色), and wrote 她 and 牠
/// for the feminine and the animal, which the 1919 printing could not have, since 她 was coined in
/// 1920. The words are the 1919 translation's; the typography is FHL's.
/// </para>
///
/// <para>
/// **A Chinese word is what FHL's tagging makes one.** Chinese prints no spaces, and no segmenter is
/// run here: FHL marked every span it gave a Strong number, and that span is the word — 起初, 神,
/// 創造 — whether it is one character or a phrase like 於是女人, which FHL tagged whole as H802. A run
/// no element claims is a word up to the next punctuation mark. So a word is a claim FHL made about
/// what renders what, and the reader shows the characters exactly as printed.
/// </para>
///
/// <para>
/// **A Korean word is an eojeol**, the space-separated unit Korean prints: a stem with its particles
/// attached, so 하나님이 is God and the subject marker and 천지를 heaven-and-earth and the object
/// marker. It is loaded as printed, because the Korean Bible Society's terms keep the integrity of
/// the text and splitting the particles off would be this project's analysis presented as the
/// edition's words.
/// </para>
///
/// <para>
/// **Both are placed in the English frame.** The modules are numbered by SWORD's NRSV scheme, which
/// the King James's numbering matches except for 3 John 1:15 and Revelation 12:18. Those words are
/// joined to the verse the King James prints them in, and the verse says which two addresses the
/// edition gives it.
/// </para>
/// </summary>
internal static class SwordTextSource
{
    /// <summary>The Chinese Union Version of 1919 in the traditional characters it was printed in.</summary>
    public const string ChineseUnion = "CUV";

    /// <summary>The same text in simplified characters, which is how most of its readers read it.</summary>
    public const string ChineseUnionSimplified = "CUVS";

    /// <summary>개역한글, the Korean Revised Version of 1961.</summary>
    public const string KoreanRevised = "KRV";

    /// <summary>J. N. Darby's French Bible, in the 2024 revision of Bibles et Publications Chrétiennes.</summary>
    public const string DarbyFrench = "JND2024";

    /// <summary>Franz Eugen Schlachter's German Bible in the Geneva Bible Society's revision of 1951.</summary>
    public const string Schlachter = "SCH1951";

    /// <summary>Michael W. Jones's Revised Literal Translation of the King James.</summary>
    public const string RevisedLiteral = "RLT2018";

    /// <summary>ISO 15924 for the two ways Chinese is printed.</summary>
    public const string TraditionalChinese = "Hant";

    public const string SimplifiedChinese = "Hans";

    private const string NrsvVersification = "NRSV";
    private const int Psalms = 19;
    private const char OpeningParenthesis = '（';

    /// <summary>
    /// The verses SWORD's NRSV numbers apart and the King James prints within a neighbour: where each
    /// goes, and whether it stands at the end of that verse or at its head.
    /// </summary>
    private static readonly (int Book, int Chapter, int Verse, int IntoChapter, int IntoVerse, bool AtEnd)[] Joined =
    [
        (64, 1, 15, 1, 14, true),
        (66, 12, 18, 13, 1, false),
    ];

    private static readonly Dictionary<string, SwordText> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ChiUn"] = new(Path.Combine("ChineseUnion1919", "ChiUn"), SwordSegmentation.Tagged, ChineseUnionDefinition(
            ChineseUnion, "和合本", "ChiUn", TraditionalChinese, "traditional characters, as the 1919 edition was printed"))
        {
            FhlPrefixes = true,
        },

        ["ChiUns"] = new(Path.Combine("ChineseUnion1919", "ChiUns"), SwordSegmentation.Tagged, ChineseUnionDefinition(
            ChineseUnionSimplified, "和合本（简体字）", "ChiUns", SimplifiedChinese,
            "simplified characters, FHL's conversion of the same text"))
        {
            FhlPrefixes = true,
        },

        ["KorRV"] = new(Path.Combine("KoreanRevised1961", "KorRV"), SwordSegmentation.Spaced, new TextDefinition(
            Slug: KoreanRevised,
            Name: "Korean Revised Version",
            NameNative: "성경전서 개역한글판",
            Kind: TextKind.Translation,
            Language: "kor",
            Direction: TextDirection.LeftToRight,
            Versification: Versification.English,
            PublishedYear: 1961,
            SourceUrl: "https://www.crosswire.org/sword/modules/ModInfo.jsp?modName=KorRV",
            RightsHolder: "Korean Bible Society (대한성서공회), moral rights only",
            Licence: "Public Domain",
            LicenceUrl: "https://www.bskorea.or.kr/bbs/board.php?bo_table=copyright_faq&wr_id=5",
            Redistribution: Redistribution.PublicDomain,
            TextualFamily: "Alexandrian")
        {
            Translators = "The Korean Bible Society (대한성서공회)",
            Edition = "The 1961 revision of the 1938 개역, as transcribed on Korean Wikisource",
            About =
                "The Korean Protestant Bible for most of the twentieth century. The Bible Society's "
                + "translators revised the whole Korean Bible of 1911 into the 개역 of 1938, and revised "
                + "that again into the 개역한글 of 1961, whose spelling follows the Hangul standard "
                + "of its day; the 개역개정 that Korean churches read now is its 1998 revision. Its New "
                + "Testament follows the critical Greek: Matthew 18:11, Mark 9:44 and 9:46, Acts 8:37 "
                + "and the other verses the older Greek lacks have no text here, and the passage of the "
                + "woman taken in adultery is printed in brackets. Seven times the Korean sentence runs "
                + "two verses into one and the second has no text of its own — Psalm 72:20, Isaiah 30:2 "
                + "and 48:2, Jeremiah 21:2, Ezekiel 24:5, Acts 15:26 and Romans 9:2 — and 2 Corinthians "
                + "13 is printed in thirteen verses, so its last two stand at the King James's 13:13 and "
                + "13:14. Korean writes its words as eojeol, a stem with its particles attached, and "
                + "each is loaded as one word.",
            RightsNote =
                "The Korean Bible Society says its economic rights in this edition have run out and it "
                + "may be used without paying a fee (copyright FAQ, 2017-10-27), and Korean Wikisource "
                + "marks it public domain. The society keeps the moral rights Korean law gives an "
                + "author: to be named, which is why it is credited here as the translator, and to "
                + "the integrity of the work, which is why the words are loaded as printed and not "
                + "divided or corrected.",
            Citation =
                "성경전서 개역한글판, 대한성서공회 1961, in the transcription of Korean Wikisource as "
                + "CrossWire publishes it in the SWORD module KorRV.",
        })
        {
            // The Korean prints 2 Corinthians 13 in thirteen verses, the holy kiss inside verse 11,
            // and the module keeps its numbers in a fourteen-verse chapter: its 13:12 is the King
            // James's 13:13 and its 13:13 the benediction, 13:14, with the last slot empty.
            Renumbered =
            [
                ((47, 13, 13), (47, 13, 14)),
                ((47, 13, 12), (47, 13, 13)),
            ],
        },

        ["FreJND"] = new(Path.Combine("DarbyFrench", "FreJND"), SwordSegmentation.Spaced, new TextDefinition(
            Slug: DarbyFrench,
            Name: "Darby Bible (French)",
            NameNative: "La Sainte Bible, traduction J.N. Darby",
            Kind: TextKind.Translation,
            Language: "fra",
            Direction: TextDirection.LeftToRight,
            Versification: Versification.Original,
            PublishedYear: 1885,
            SourceUrl: "https://www.crosswire.org/sword/modules/ModInfo.jsp?modName=FreJND",
            RightsHolder: null,
            Licence: "Public Domain",
            LicenceUrl: "https://www.crosswire.org/ftpmirror/pub/sword/raw/mods.d/frejnd.conf",
            Redistribution: Redistribution.PublicDomain,
            TextualFamily: "Alexandrian")
        {
            Translators = "John Nelson Darby (1800-1882), with the Brethren who worked with him",
            Editors = "Bibles et Publications Chrétiennes (Valence), whose revision JND v2.0 of 2024 is the text served",
            Edition = "The 2024 revision of Bibles et Publications Chrétiennes, as CrossWire publishes it in FreJND 3.3",
            EditionYear = 2024,
            About =
                "Darby made his French Bible from the Hebrew and from the Greek text he had established "
                + "himself, alongside his German and English ones; the complete Bible appeared in 1885 "
                + "and was reissued in 1916. Bibles et Publications Chrétiennes revised the text in 2024 "
                + "and released it free of rights. Its New Testament follows the critical Greek: Acts "
                + "8:37 has no text and the Lord's Prayer ends without the doxology. The Old Testament "
                + "is numbered as the Hebrew is, a psalm's title counted as its first verse. The asterisk "
                + "the edition prints at the head of a paragraph is printed here too.",
            RightsNote =
                "CrossWire publishes the module as Public Domain, and its description says the 2024 text "
                + "is free of rights as BPC provides it. The Strong numbers in the module are the work of "
                + "Concordances et Traductions de la Bible (concordance.bible); they are read to draw "
                + "links and are not stored on the words.",
            Citation =
                "La Sainte Bible, traduction J.N. Darby, révision BPC 2024 (JND v2.0), as CrossWire "
                + "publishes it in the SWORD module FreJND, with the Strong numbers of concordance.bible.",
        }),

        ["GerSch"] = new(Path.Combine("Schlachter1951", "GerSch"), SwordSegmentation.Spaced, new TextDefinition(
            Slug: Schlachter,
            Name: "Schlachter Bible 1951",
            NameNative: "Schlachter-Bibel 1951",
            Kind: TextKind.Translation,
            Language: "deu",
            Direction: TextDirection.LeftToRight,
            Versification: Versification.Original,
            PublishedYear: 1905,
            SourceUrl: "https://www.crosswire.org/sword/modules/ModInfo.jsp?modName=GerSch",
            RightsHolder: "Genfer Bibelgesellschaft (Geneva Bible Society)",
            Licence: "Copyrighted; Free non-commercial distribution",
            LicenceUrl: "https://www.crosswire.org/ftpmirror/pub/sword/raw/mods.d/gersch.conf",
            Redistribution: Redistribution.NonCommercialOnly,
            TextualFamily: "Byzantine")
        {
            Translators = "Franz Eugen Schlachter (1859-1911)",
            Editors = "The Genfer Bibelgesellschaft, whose revision of 1951 is the text served",
            Edition = "The 1951 revision, as CrossWire publishes it in GerSch 2.1",
            EditionYear = 1951,
            About =
                "Franz Eugen Schlachter, a Swiss preacher, translated the whole Bible alone from the "
                + "Hebrew and Greek and published it in 1905 as the Miniaturbibel, for readers who did not "
                + "read Luther's German easily. The Geneva Bible Society revised it in 1951, and that "
                + "revision is the Schlachter German free churches read until the 2000 edition replaced it. "
                + "Its New Testament keeps Acts 8:37 and the doxology of the Lord's Prayer and does not "
                + "print the heavenly witnesses of 1 John 5:7. The Old Testament is numbered as the "
                + "Hebrew is, a psalm's title counted as its first verse.",
            RightsNote =
                "The 1951 revision is the Geneva Bible Society's (Copyright 1951 Genfer Bibelgesellschaft, "
                + "as the module's description prints it), and CrossWire publishes it under "
                + "\"Copyrighted; Free non-commercial distribution\". It is served for that use only. "
                + "Nobody is named for the Strong numbers in the module; they are read to draw links and "
                + "are not stored on the words.",
            Citation =
                "Die Heilige Schrift des Alten und Neuen Testaments, übersetzt von F. E. Schlachter, "
                + "Neue Überarbeitung 1951, © Genfer Bibelgesellschaft, as CrossWire publishes it in the "
                + "SWORD module GerSch.",
        }),

        ["RLT"] = new(Path.Combine("RevisedLiteral", "RLT"), SwordSegmentation.Spaced, new TextDefinition(
            Slug: RevisedLiteral,
            Name: "Revised Literal Translation",
            NameNative: null,
            Kind: TextKind.Translation,
            Language: "eng",
            Direction: TextDirection.LeftToRight,
            Versification: Versification.English,
            PublishedYear: 2018,
            SourceUrl: "https://www.crosswire.org/sword/modules/ModInfo.jsp?modName=RLT",
            RightsHolder: "Michael W. Jones, Sr.",
            Licence: "GPL",
            LicenceUrl: "https://www.crosswire.org/ftpmirror/pub/sword/raw/mods.d/rlt.conf",
            Redistribution: Redistribution.ShareAlike,
            TextualFamily: "Byzantine")
        {
            Translators = "Michael W. Jones, Sr., revising the King James Version of 1769",
            Edition = "The 2018 text, as CrossWire publishes it in RLT 1.0 of 2022",
            About =
                "A light revision of the King James Version (1769) by Michael W. Jones, made to change "
                + "as little as possible: the LORD becomes Yhwh, church becomes assembly, and archaic "
                + "words and phrases are modernised where he judged them unclear or inaccurate, with the "
                + "Textus Receptus as the standard. The words the King James prints in italics are kept. "
                + "Its Strong numbers are the King James's own, from the Bible Foundation's Old "
                + "Testament and the KJV2003 project's New Testament.",
            RightsNote =
                "Jones \"hereby grants a general public license to use this text for any purpose\", and "
                + "CrossWire publishes the module under the GPL; anything published as an adaptation of "
                + "it carries the same terms. The base text is the King James, whose rights in the "
                + "United Kingdom are the Crown's. The Strong numbers are read to draw links and are not "
                + "stored on the words.",
            Citation =
                "Revised Literal Translation (RLT) by Michael W. Jones, Sr., as CrossWire publishes it "
                + "in the SWORD module RLT.",
        }),
    };

    /// <summary>
    /// Modules read only for the Strong numbers they put on a text the corpus loads from elsewhere,
    /// and never loaded as texts: their words are another digitisation of that text, so the numbers
    /// are laid onto its words and nothing else of them is used. Each is keyed by its module name, and
    /// its definition is the text the numbers are laid onto.
    /// </summary>
    private static readonly Dictionary<string, SwordText> NumberingOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FreSegond1910"] = new(
            Path.Combine("Segond1910Strong", "FreSegond1910"),
            SwordSegmentation.Spaced,
            EbibleTextSource.Definitions["Segond1910"]),
    };

    /// <summary>Every text this reader knows, by the module it is read from.</summary>
    public static IReadOnlyDictionary<string, SwordText> Texts => Known;

    /// <summary>The modules read only for their numbers, by module name.</summary>
    public static IReadOnlyDictionary<string, SwordText> Numberings => NumberingOnly;

    /// <summary>What each of those texts is, by the same key.</summary>
    public static IReadOnlyDictionary<string, TextDefinition> Definitions =>
        Known.ToDictionary(text => text.Key, text => text.Value.Definition, StringComparer.OrdinalIgnoreCase);

    /// <param name="folder">The module's root: the folder holding <c>mods.d</c>.</param>
    public static TextSource Read(string folder)
    {
        var (text, verses) = Verses(folder);
        var chapters = verses
            .GroupBy(verse => (verse.Key.Book, verse.Key.Chapter))
            .ToDictionary(
                chapter => chapter.Key,
                chapter => new ChapterDraft(
                    chapter.Key.Chapter,
                    [.. chapter.OrderBy(verse => verse.Key.Verse).Select(verse => Draft(verse.Key.Verse, verse.Value))]));

        var books = new List<BookDraft>(BookReferences.CanonBookCount);
        foreach (var ordinal in BookReferences.Ordinals)
        {
            var held = chapters.Where(chapter => chapter.Key.Book == ordinal).OrderBy(chapter => chapter.Key.Chapter)
                .Select(chapter => chapter.Value).ToList();
            if (held.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{text.Definition.Slug} has no text for {BookReferences.Name(ordinal)}, and this is a complete "
                    + "Bible: the module was fetched partially or is a different one. Run scripts/fetch-crosswire.ps1 "
                    + "rather than loading part of a text as though it were the whole of one.");
            }

            books.Add(new BookDraft(
                CanonicalOrdinal: ordinal,
                Position: ordinal,
                Name: BookReferences.Name(ordinal),
                Slug: BookReferences.Slug(ordinal),
                Chapters: held,
                Abbreviation: BookReferences.Abbreviation(ordinal)));
        }

        return new TextSource(text.Definition, books);
    }

    /// <summary>
    /// The words of every verse with the numbers the edition writes on them, at the address the
    /// corpus stores the verse under — which is what <see cref="Read"/> loads, word for word.
    /// </summary>
    public static Dictionary<(int Book, int Chapter, int Verse), List<EditionWord>> Numbers(string folder) =>
        Verses(folder).Verses.ToDictionary(
            verse => verse.Key,
            verse => verse.Value.Words.Select(word => new EditionWord(word.Surface, word.Numbers, word.Unit)).ToList());

    private static VerseDraft Draft(int number, ReadVerse verse) =>
        new(number, [.. verse.Words.Select(word => new WordDraft(word.Surface, word.Trailer, Elided: word.Elided))])
        {
            Notes = verse.Notes,
            Stated = verse.Stated,
            MarksASuperscription = verse.Superscription,
        };

    private sealed record ReadVerse(
        List<SwordWord> Words,
        List<VerseNoteDraft> Notes,
        bool Superscription,
        List<StatedNumberDraft> Stated);

    private static (SwordText Text, Dictionary<(int Book, int Chapter, int Verse), ReadVerse> Verses) Verses(string folder)
    {
        var configuration = SwordModule.Configuration(folder);
        if (!Known.TryGetValue(configuration.Name, out var text)
            && !NumberingOnly.TryGetValue(configuration.Name, out text))
        {
            throw new ArgumentException(
                $"There is no text definition for the SWORD module \"{configuration.Name}\". A translation cannot be "
                + "loaded without one: its licence and provenance are part of the definition, not something filled "
                + "in afterwards.",
                nameof(folder));
        }

        var unit = 0;
        var verses = new Dictionary<(int, int, int), ReadVerse>(32_000);
        foreach (var (address, markup) in SwordModule.Verses(folder))
        {
            var pieces = OsisVerse.Parse(markup);
            var words = Segment(pieces, text.Segmentation, ref unit, out var notes, text.FhlPrefixes);
            if (words.Count == 0)
            {
                continue;
            }

            var superscription = pieces.Any(piece => piece.Title)
                                 || (text.Segmentation == SwordSegmentation.Tagged && address is (Psalms, _, 1)
                                     && words[0] is { Elided: true } opening
                                     && opening.Trailer.TrimStart().StartsWith(OpeningParenthesis));
            verses[address] = new ReadVerse(words, notes, superscription, []);
        }

        foreach (var (stored, english) in text.Renumbered ?? [])
        {
            if (verses.Remove(stored, out var moved) && !verses.ContainsKey(english))
            {
                verses[english] = moved with { Stated = [new StatedNumberDraft(stored.Chapter, stored.Verse)] };
            }
            else
            {
                throw new InvalidDataException(
                    $"{text.Definition.Slug} was expected to hold {stored} and nothing at {english}, which is how the "
                    + "module numbered the passage when its renumbering was established. The module has changed; "
                    + "read the passage again before moving any verse.");
            }
        }

        if (configuration.Values.GetValueOrDefault("Versification") == NrsvVersification)
        {
            JoinWhatTheKingJamesJoins(verses);
        }

        return (text, verses);
    }

    /// <summary>
    /// Moves the two verses the King James does not number into the verse it prints them in, and
    /// records both addresses on it, the way the Ukrainian's Revelation 13:1 records 12:18 and 13:1.
    /// </summary>
    private static void JoinWhatTheKingJamesJoins(Dictionary<(int, int, int), ReadVerse> verses)
    {
        foreach (var (book, chapter, verse, intoChapter, intoVerse, atEnd) in Joined)
        {
            if (!verses.Remove((book, chapter, verse), out var moved))
            {
                continue;
            }

            if (!verses.TryGetValue((book, intoChapter, intoVerse), out var into))
            {
                verses[(book, intoChapter, intoVerse)] = moved with { Stated = [new StatedNumberDraft(chapter, verse)] };
                continue;
            }

            var (first, second) = atEnd ? (into, moved) : (moved, into);
            verses[(book, intoChapter, intoVerse)] = new ReadVerse(
                [.. first.Words, .. second.Words],
                [.. first.Notes, .. second.Notes.Select(note => note with
                {
                    AnchorWordPosition = note.AnchorWordPosition + first.Words.Count,
                })],
                first.Superscription || second.Superscription,
                atEnd
                    ? [new StatedNumberDraft(intoChapter, intoVerse), new StatedNumberDraft(chapter, verse)]
                    : [new StatedNumberDraft(chapter, verse), new StatedNumberDraft(intoChapter, intoVerse)]);
        }
    }

    /// <summary>
    /// The verse's words, each with the punctuation after it as its trailer. Punctuation standing
    /// before the first word — an opening quotation mark, the parenthesis a psalm's title is printed
    /// in — is kept on a word with no letters, marked elided, as the other readers keep it.
    /// </summary>
    internal static List<SwordWord> Segment(
        IReadOnlyList<OsisPiece> pieces,
        SwordSegmentation segmentation,
        ref int unit,
        out List<VerseNoteDraft> notes,
        bool fhlPrefixes = true)
    {
        var words = new List<SwordWord>();
        var splits = new Dictionary<string, int>(StringComparer.Ordinal);
        notes = [];
        var tagged = segmentation == SwordSegmentation.Tagged;

        foreach (var piece in pieces)
        {
            if (piece.Kind == OsisPieceKind.Note)
            {
                if (piece.Text.Length > 0)
                {
                    notes.Add(new VerseNoteDraft(VerseNoteKind.Footnote, piece.Text, words.Count));
                }

                continue;
            }

            IReadOnlyList<string> numbers =
                piece.Kind == OsisPieceKind.Word ? UnionStrongNumbers.Read(piece.Lemma, fhlPrefixes) : [];
            var element = piece.Kind != OsisPieceKind.Word
                ? 0
                : piece.Split is not { } split
                    ? ++unit
                    : splits.TryGetValue(split, out var shared)
                        ? shared
                        : splits[split] = ++unit;
            var joinable = !tagged;

            foreach (var (run, letters) in Runs(piece.Text))
            {
                if (!letters)
                {
                    var trailer = tagged ? WithoutMarkupSpaces(run) : words.Count == 0 ? run.TrimStart() : run;
                    if (trailer.Length == 0)
                    {
                        continue;
                    }

                    if (words.Count == 0)
                    {
                        words.Add(new SwordWord(string.Empty, trailer, [], ++unit, Elided: true));
                    }
                    else
                    {
                        words[^1] = words[^1] with { Trailer = words[^1].Trailer + trailer };
                    }

                    joinable = false;
                    continue;
                }

                if (joinable && words.Count > 0 && words[^1] is { Trailer.Length: 0, Elided: false } last)
                {
                    words[^1] = last with { Surface = last.Surface + run };
                    continue;
                }

                words.Add(new SwordWord(run, string.Empty, numbers, element > 0 ? element : ++unit, Elided: false));
                joinable = !tagged;
            }
        }

        if (words.Count > 0)
        {
            words[^1] = words[^1] with { Trailer = words[^1].Trailer.TrimEnd() };
        }

        return words;
    }

    /// <summary>The piece split into runs of letters and runs of everything else, in order.</summary>
    private static IEnumerable<(string Run, bool Letters)> Runs(string text)
    {
        var run = new StringBuilder();
        bool? letters = null;
        foreach (var rune in text.EnumerateRunes())
        {
            var isLetter = IsLetter(rune);
            if (letters is { } current && current != isLetter)
            {
                yield return (run.ToString(), current);
                run.Clear();
            }

            letters = isLetter;
            run.Append(rune.ToString());
        }

        if (letters is { } last)
        {
            yield return (run.ToString(), last);
        }
    }

    private static bool IsLetter(Rune rune) =>
        Rune.IsLetterOrDigit(rune)
        || Rune.GetUnicodeCategory(rune) is UnicodeCategory.LetterNumber
            or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark;

    /// <summary>
    /// A Chinese trailer without the ASCII spaces CrossWire writes between elements. Chinese prints
    /// no such space; the ideographic space it does print is a different character and stays.
    /// </summary>
    private static string WithoutMarkupSpaces(string run) =>
        string.Concat(run.Where(c => c is not (' ' or '\t' or '\r' or '\n')));

    private static TextDefinition ChineseUnionDefinition(string slug, string nameNative, string module, string scriptCode, string script) =>
        new(
            Slug: slug,
            Name: slug == ChineseUnion ? "Chinese Union Version" : "Chinese Union Version (simplified)",
            NameNative: nameNative,
            Kind: TextKind.Translation,
            Language: "zho",
            Direction: TextDirection.LeftToRight,
            Versification: Versification.English,
            PublishedYear: 1919,
            SourceUrl: $"https://www.crosswire.org/sword/modules/ModInfo.jsp?modName={module}",
            RightsHolder: null,
            Licence: "Public Domain",
            LicenceUrl: "https://www.fhl.net/gb/fhl/fhl8.html",
            Redistribution: Redistribution.PublicDomain,
            TextualFamily: "Alexandrian")
        {
            Translators =
                "The Union Version committees of the missionary societies in China, among them Calvin Wilson "
                + "Mateer, Chauncey Goodrich and Frederick W. Baller, with their Chinese colleagues",
            Editors = "The Faith Hope Love foundation (信望愛), who typed it in 1995 and punctuated it",
            Script = scriptCode,
            Edition = $"The 1919 text in FHL's transcription, in {script}",
            About =
                "The Bible Chinese Protestants read: the missionary societies agreed in 1890 to make one "
                + "translation in the Mandarin people spoke rather than the classical language, and the "
                + "Union Version they published in 1919 is still the text most Chinese churches use. Its "
                + "New Testament was made from the Greek behind the English Revised Version, so the verses "
                + "that version drops stand here in parentheses. What is served is FHL's transcription of "
                + "the 1919 printing, with FHL's punctuation and FHL's modern spellings of place names, "
                + "not the 1988 new-punctuation edition of the Hong Kong Bible Society. Chinese prints no "
                + "spaces, so a word here is the span FHL tagged with a Strong number, which may be one "
                + "character or a phrase: 376,226 of its 405,940 words stand under one. John 7:53 is "
                + "printed at the head of 8:1, so the text holds 31,101 verses.",
            RightsNote =
                "The translation is public domain by age, and FHL says its transcription is of the 1919 "
                + "edition, whose protection has run out; CrossWire publishes the modules as Public Domain. "
                + "The 1988 new-punctuation edition, which eBible also publishes as public domain, is the "
                + "Hong Kong Bible Society's and is not what is loaded. FHL's Strong numbers are its own and "
                + "released under the GNU Free Documentation License; they are read to draw links and are "
                + "not stored on the words.",
            Citation =
                $"和合本 (1919), in the transcription of the Faith Hope Love foundation, bible.fhl.net, as "
                + $"CrossWire publishes it in the SWORD module {module}.",
        };
}
