using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Corpus;
using Essenthos.Core.Usfm;

namespace Essenthos.Core.Loading;

/// <summary>
/// The German and Spanish Bibles, as eBible publishes them: one reader for all three because they
/// are one file format, and the same format Brenton and the Kulish Bible already arrive in.
///
/// They are here because the interface is to speak German and Spanish and the corpus held no text
/// in either. What could be taken was decided by reading licences rather than by fame: the Luther
/// everybody names is four different texts with four rights positions, and so is the Reina-Valera.
/// The three below are the ones whose terms are stated where the bytes are, in three places that
/// agree, and whose translators have been dead long enough for the arithmetic to say the same.
///
/// Two of them arrive carrying more than words. Luther 1912 and the Reina-Valera tag their words
/// with Strong numbers — 365,353 and 390,758 of them — which is the only thing either file says
/// about the Hebrew and the Greek, and the difference between reaching the originals through a
/// claim somebody made and reaching them through this project's own guess. Only Luther's are taken:
/// the Spanish tagging turned out to be a named third party's, republished here without his name on
/// it, and what is refused and why is set out below. Where a tag stands over a phrase rather than a
/// word, every word of the phrase is given the number, which is this reader's rendering of a claim
/// the edition made about a span and is said here because it is not what the file says.
/// </summary>
internal static class EbibleTextSource
{
    /// <summary>
    /// The Lutherbibel in its 1912 revision, which is the last one out of copyright.
    ///
    /// Not the 1545 and not the 1984 or the 2017. Which of them the file holds was established by
    /// reading it: Genesis 1:1 is "Am Anfang schuf Gott Himmel und Erde" where the 1545 prints "AM
    /// ANFANG SCHUFF Gott Himel vnd Erden", and John 3:16 opens "Also hat Gott die Welt geliebt"
    /// with "auf daß alle, die an ihn glauben" where 1984 opens "Denn also" and continues "damit
    /// alle". The spelling is the 1901 reform's and not the 1996 one's — daß, not dass — which is
    /// where a 1912 printing sits and a modern revision does not.
    /// </summary>
    public const string Luther = "LUTH1912";

    /// <summary>
    /// The unrevised Elberfelder of 1905, the German the Darby circle made and the plainest
    /// counterweight to Luther the language has.
    /// </summary>
    public const string Elberfelder = "ELB1905";

    /// <summary>
    /// The Reina-Valera in its 1909 revision.
    ///
    /// Not the 1960, which Sociedades Bíblicas Unidas holds and licenses by the verse. Established
    /// from the text: Genesis 1:1 reads "crió" where 1960 reads "creó", John 3:16 has "ha dado á su
    /// Hijo unigénito" with the accented preposition that 1960 drops, and Psalm 23:1 opens
    /// "JEHOVÁ es mi pastor".
    /// </summary>
    public const string ReinaValera = "RV1909";

    /// <summary>
    /// Each text's folder under <c>Resources</c> and the definition it loads with, so the download
    /// and the identifier we publish can differ without either being guessed from the other. The
    /// fetch script names the folder; eBible's own identifier is on the row, in the source URL.
    /// </summary>
    private static readonly Dictionary<string, TextDefinition> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Luther1912"] = Definition(
            Luther, "deu1912", "Luther Bible", "Lutherbibel", "deu", 1534, "Byzantine") with
        {
            Translators = "Martin Luther (1483-1546), with Philipp Melanchthon and the Wittenberg circle",
            Editors =
                "The revision commission of the German evangelical church conferences, whose 1892 "
                + "Probebibel and 1912 revision settled the text served here",
            Edition = "The 1912 revision, in the orthography of the 1901 reform",
            EditionYear = 1912,
            About =
                "Luther translated the New Testament in eleven weeks at the Wartburg in 1522 and "
                + "finished the Old Testament with a team at Wittenberg in 1534, working from the "
                + "Hebrew and from Erasmus's Greek, and the German he wrote it in is the reason the "
                + "language has a standard at all. What is served here is neither his printing nor a "
                + "modern one: the churches revised the text twice, in 1892 and again in 1912, and "
                + "the 1912 revision is the last that is out of copyright. Its base text is the "
                + "Textus Receptus and it shows: Acts 8:37, Matthew 17:21 and 18:11, Luke 17:36, "
                + "John 5:4, the doxology of the Lord's Prayer and the longer ending of Mark are all "
                + "printed as text. The heavenly witnesses of 1 John 5:7 are not, which is Luther's "
                + "own decision surviving the revision. It arrives tagged: 365,350 of its 696,963 "
                + "words carry a Strong number.",
            RightsNote =
                "Settled for the text and unstated for the tagging, and the two are worth keeping "
                + "apart. Luther died in 1546 and the 1912 revisers were a church commission whose "
                + "work is more than a century old, so the text is out of copyright by any "
                + "arithmetic; eBible says Public Domain in its catalogue, on the copyright page and "
                + "in the copy of that page shipped inside the archive. None of the three says who "
                + "assigned the Strong numbers or under what terms eBible obtained them, and a "
                + "tagging layer is somebody's work even when the text under it is nobody's.",
            Citation =
                "Die Bibel nach der Übersetzung Martin Luthers, revised text of 1912, in the digital "
                + "edition eBible.org publishes as deu1912.",
        },

        ["Elberfelder1905"] = Definition(
            Elberfelder, "deuelo", "Elberfelder Bible", "Elberfelder Bibel", "deu", 1871, "Mixed") with
        {
            Translators =
                "John Nelson Darby (1800-1882), Carl Brockhaus (1822-1899) and "
                + "Julius Anton von Poseck (1816-1896)",
            Edition = "The unrevised edition of 1905, before the revisions R. Brockhaus made from 1960",
            EditionYear = 1905,
            About =
                "The German the Brethren made, and the one German Bible written to be literal rather "
                + "than to be read aloud: Darby, Brockhaus and von Poseck brought out the New "
                + "Testament in 1855 and the whole Bible in 1871 at Elberfeld, keeping the Hebrew "
                + "and Greek word order wherever German would bear it and printing the divine name "
                + "as Jehova, which Psalm 23:1 shows — \"Jehova ist mein Hirte\" where Luther has "
                + "\"Der HERR\". Its Greek is not Luther's: the doxology of the Lord's Prayer and "
                + "the heavenly witnesses are absent, the longer ending of Mark stands in square "
                + "brackets, and Matthew 23:14 and Acts 8:37 are printed as a line saying the verse "
                + "does not belong to the original text — a note this reader loads as words, because "
                + "the edition put it where a verse goes. Acts 15:34 it prints as a bare dash, which "
                + "has no words to load, so this text holds 31,101 verses rather than 31,102. It "
                + "carries no annotation of any kind, and it states its own numbering in 261 places.",
            RightsNote =
                "Public domain by age and stated so by eBible in all three places, and the arithmetic "
                + "agrees: the latest of the three translators died in 1899. CrossWire distributes "
                + "the same edition marked Public Domain while its own description line reads "
                + "\"Copyright by R. Bockhaus Verlag, Germany\" — one file saying both things, which "
                + "is why the statement taken is eBible's rather than that one. What R. Brockhaus "
                + "does hold is the revised Elberfelder of 1960 and after, which is a different text.",
            Citation =
                "Elberfelder Bibel, unrevidierte Ausgabe von 1905, in the digital edition eBible.org "
                + "publishes as deuelo.",
        },

        ["ReinaValera1909"] = Definition(
            ReinaValera, "spaRV1909", "Reina-Valera Bible", "Santa Biblia Reina-Valera", "spa", 1569,
            "Byzantine") with
        {
            Translators = "Casiodoro de Reina (1520-1594), revised by Cipriano de Valera (1531-1602)",
            Editors = "The revisers the British and Foreign Bible Society engaged for the 1909 edition",
            Edition = "The 1909 revision, the last Reina-Valera that is out of copyright",
            EditionYear = 1909,
            About =
                "Casiodoro de Reina, a Spanish monk who left Seville ahead of the Inquisition, "
                + "printed the whole Bible in Spanish at Basel in 1569 — the Biblia del Oso, from "
                + "the bear on its title page — and Cipriano de Valera revised it in 1602. What "
                + "followed is a chain of revisions, and only the ones before 1909 are free: the "
                + "1960 that most Spanish readers know is held by Sociedades Bíblicas Unidas and "
                + "licensed by the verse. This is the 1909, and its Greek is the Textus Receptus "
                + "throughout — the heavenly witnesses of 1 John 5:7 are printed, and so are Acts "
                + "8:37, Matthew 17:21 and 18:11, Luke 17:36, John 5:4 and the doxology of the "
                + "Lord's Prayer. It arrives tagged more thoroughly than the German — 671,228 of its "
                + "703,737 words stand under a Strong number — and that tagging is not loaded, "
                + "because it is Rubén Gómez's work and eBible publishes it without his name on it. "
                + "What is loaded from the annotation is the 3,501 spans it marks as words the "
                + "translators supplied, which are the italics of a printed Reina-Valera and are the "
                + "edition's own. "
                + "It is the one text here that is not placed in the English numbering. eBible "
                + "renumbered both German files and recorded what each verse is called at home; it "
                + "did neither for this one, so 18 verse slots stand empty where the Spanish's own "
                + "division runs ahead of the English, the chapter after each seam is one to three "
                + "verses late, and its last verse holds two. Nothing is missing. Measured against "
                + "Luther by the Strong numbers both carry, 216 of 31,102 verses are at an address "
                + "other than the one the German uses for the same words, in ten chapters — "
                + "Numbers 13 and 30, 1 Samuel 24, 2 Chronicles 33, Job 39 and 40, Hosea 12, "
                + "Jonah 2, 1 Kings 22 and 1 Chronicles 21.",
            RightsNote =
                "The text is public domain and says so in all three of eBible's places, and Valera "
                + "died in 1602. The Strong tagging that arrives with it is not, and is not loaded. "
                + "It is Rubén Gómez's, established by comparison rather than assumed: eBible tags "
                + "3 John 1:12 as Todos|G5259,G3956 and misma|G5259,G0846, and bibliaparalela "
                + "publishes those same numbers on those same phrases as \"Reina-Valera 1909 con "
                + "números de Strong, cortesía de Rubén Gómez, utilizado con permiso\". His stated "
                + "terms are that the layer goes to software publishers and not to individuals and "
                + "that the module must be locked; CrossWire carries it encrypted as \"Copyrighted; "
                + "Permission to distribute granted to CrossWire\". eBible names him nowhere.",
            Citation =
                "Santa Biblia, Antigua versión de Casiodoro de Reina revisada por Cipriano de Valera, "
                + "revisión de 1909, in the digital edition eBible.org publishes as spaRV1909.",
        },
    };

    /// <summary>Every text this reader knows, by the folder its files are fetched into.</summary>
    public static IReadOnlyDictionary<string, TextDefinition> Definitions => Known;

    /// <summary>
    /// Texts whose Strong tagging is somebody else's work, republished by eBible without their
    /// name on it. The words are loaded and the numbers are not.
    ///
    /// The Reina-Valera's tagging is Rubén Gómez's. It was established rather than suspected: eBible
    /// tags 3 John 1:12 as <c>Todos|G5259,G3956</c> and <c>misma|G5259,G0846</c>, and bibliaparalela
    /// publishes the identical numbers on the identical phrases under the line "Reina-Valera 1909
    /// con números de Strong. Cortesía de Rubén Gómez. Utilizado con permiso." The G5259 standing on
    /// both of those words is an error, and a shared error is a copy. His own terms, published in
    /// 2012, are that he gives the layer to software publishers and not to individuals and that the
    /// module must be locked so only he can change it; CrossWire carries it as "Copyrighted;
    /// Permission to distribute granted to CrossWire", encrypted.
    ///
    /// eBible's Public Domain line is over the package and the 1909 text under it really is public
    /// domain. It is not a grant over a layer eBible names nobody for, and the most restrictive
    /// statement attached to these bytes is Gómez's. The text loses nothing that matters: the one
    /// hand-made Spanish word alignment that exists is published separately under CC BY 4.0 and is
    /// keyed to this very file.
    ///
    /// <para>
    /// The owner decided on 2026-09-20 that the numbers may be read as an input to the mapping and
    /// never shown — the position the Synodal's numbering is already loaded under
    /// (<see cref="Links.SynodalStrongLinkLoader"/>). This set is what keeps them off the word:
    /// nothing here stores a Spanish Strong number, so nothing serves one. A pass that reads them
    /// in memory to draw links has not been written yet.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> TaggingIsNotOursToTake =
        new(["ReinaValera1909"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The 66 books in canonical order, by the code each file states in its <c>\id</c> line. Their
    /// order here is the order they are loaded in, so a book's place in this list is its ordinal
    /// and its position alike — which was checked for each of the three rather than assumed, since
    /// the file names carry eBible's own numbering and that is neither.
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
    /// folder, so a reader cannot be pointed at Luther and told to load the Spanish.
    /// </param>
    public static TextSource Read(string folder)
    {
        var name = new DirectoryInfo(folder).Name;
        if (!Known.TryGetValue(name, out var definition))
        {
            throw new ArgumentException(
                $"There is no text definition for the folder \"{name}\". A translation cannot be loaded " +
                "without one: its licence and provenance are part of the definition, not something filled " +
                "in afterwards.",
                nameof(folder));
        }

        var books = Directory.GetFiles(folder, "*.usfm")
            .Select(path => UsfmReader.Read(File.ReadAllText(path)))
            .ToDictionary(book => book.Book, StringComparer.Ordinal);

        // Whose the numbers are is one question and whether they are word-level another; a layer
        // has to pass both.
        var tagged = !TaggingIsNotOursToTake.Contains(name) && StrongTagging.IsWordLevel(books.Values);
        var drafts = new List<BookDraft>(Canon.Length);

        for (var ordinal = 1; ordinal <= Canon.Length; ordinal++)
        {
            var code = Canon[ordinal - 1];
            if (!books.TryGetValue(code, out var book))
            {
                throw new InvalidOperationException(
                    $"{definition.Slug} is missing {code}, and this is a complete Bible: the fetch was "
                    + "partial, or the folder holds a different edition. Run scripts/fetch-ebible.ps1 rather "
                    + "than loading part of a text as though it were the whole of one.");
            }

            drafts.Add(new BookDraft(
                CanonicalOrdinal: ordinal,
                Position: ordinal,
                Name: BookReferences.Name(ordinal),
                Slug: BookReferences.Slug(ordinal),
                Chapters: [.. book.Chapters.Select(chapter => Chapter(chapter, tagged))],
                NameNative: book.Name,
                Abbreviation: BookReferences.Abbreviation(ordinal)));
        }

        return new TextSource(definition, drafts);
    }

    private static ChapterDraft Chapter(UsfmChapter chapter, bool tagged) => new(
        chapter.Number,
        [.. chapter.Verses.Select(verse => new VerseDraft(
            verse.Number,
            [.. verse.Words.Select(word => new WordDraft(
                word.Surface,
                word.Trailer,
                StrongNumber: tagged ? word.StrongNumber : null,
                SuppliedSpan: word.SuppliedSpan,
                Break: word.Break))],
            verse.Label)
        {
            Notes = [.. verse.Notes.Select(note => new VerseNoteDraft(
                note.Kind == UsfmNoteKind.Footnote ? VerseNoteKind.Footnote : VerseNoteKind.CrossReference,
                note.Content,
                note.AnchorWordPosition))],
            Stated = [.. verse.Stated.Select(address => new StatedNumberDraft(address.Chapter, address.Number))],
            OpensBeforeItsStatedAddress = verse.OpensBeforeItsStatedAddress,
        })]);

    /// <param name="translation">
    /// eBible's identifier, which is what the download and both statements of the licence are keyed
    /// by. It is not the slug: the field spells these editions LUTH1912, ELB1905 and RV1909, and an
    /// identifier nobody else uses is one nobody types.
    /// </param>
    private static TextDefinition Definition(
        string slug,
        string translation,
        string name,
        string nameNative,
        string language,
        int publishedYear,
        string textualFamily) => new(
        Slug: slug,
        Name: name,
        NameNative: nameNative,
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
