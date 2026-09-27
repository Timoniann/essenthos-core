using Essenthos.Core.Alexandrinus;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Swete;

namespace Essenthos.Core.Loading;

/// <summary>
/// Codex Alexandrinus — London, British Library, Royal MS 1 D V–VIII; Gregory–Aland 02, Rahlfs A —
/// as one witness, as far as its own text exists typed.
///
/// <para>
/// The New Testament is the manuscript itself, transcribed letter by letter by INTF
/// (<see cref="NtvmrTranscription"/>). Of the Old Testament only three printings give the
/// manuscript's own text: Ottley's Isaiah, read here through the same reader as <c>OTTLEY</c> so a
/// repair made to one is made to both; and Swete's Genesis to 46:28 and 1–4 Maccabees, where Swete
/// prints Alexandrinus (<see cref="SweteAlexandrinus"/>). Every other book the codex holds survives
/// typed only as readings in Swete's apparatus, and a text rebuilt from those failed the test against
/// Ottley's Isaiah, so it is not here. Nor, yet, are the Odes, which Swete prints from Alexandrinus
/// too and which are read into his text as <see cref="SweteOdes"/> reads them.
/// </para>
///
/// <para>
/// Its identifier is this project's own: no software that serves the manuscript publishes one for
/// both Testaments, and <c>GA02</c> would name the New Testament alone.
/// </para>
/// </summary>
internal static class AlexandrinusTextSource
{
    public const string Slug = Sources.AlexandrinusSlug;

    public const string Folder = "Alexandrinus";

    private static readonly TextPartSource NewTestament = new(
        Name: "Codex Alexandrinus (GA 02), transcription for the New Testament Virtual Manuscript Room",
        Author: "Institut für Neutestamentliche Textforschung, Münster",
        Licence: "CC-BY-4.0",
        LicenceUrl: "https://creativecommons.org/licenses/by/4.0/",
        Url: "https://ntvmr.uni-muenster.de/community/vmr/api/transcript/get/?docID=20002&pageID=ALL&format=teiraw",
        Covers: "the New Testament");

    private static readonly TextPartSource Genesis = new(
        Name: "Swete, The Old Testament in Greek, in the First1KGreek transcription (tlg0527.tlg001, tlg023–026)",
        Author: "Open Greek and Latin, University of Leipzig",
        Licence: "CC-BY-SA-4.0",
        LicenceUrl: "https://creativecommons.org/licenses/by-sa/4.0/",
        Url: "https://github.com/OpenGreekAndLatin/First1KGreek/tree/master/data/tlg0527",
        Covers: "Genesis 1:1–46:28 and 1–4 Maccabees");

    private static readonly TextPartSource Isaiah = new(
        Name: "Ottley, The Book of Isaiah according to the Septuagint (Codex Alexandrinus), in the First1KGreek "
              + "transcription (tlg0527.tlg048.1st1K-grc2)",
        Author: "Open Greek and Latin, University of Leipzig",
        Licence: "CC-BY-SA-4.0",
        LicenceUrl: "https://creativecommons.org/licenses/by-sa/4.0/",
        Url: "https://github.com/OpenGreekAndLatin/First1KGreek/blob/"
             + "b67137e6b82669d08fe6ad1c225999ca6aca362c/data/tlg0527/tlg048/tlg0527.tlg048.1st1K-grc2.xml",
        Covers: "Isaiah");

    /// <summary>
    /// The New Testament's licence is the one INTF's file states in its own header: <em>"This work is
    /// licensed under a Creative Commons Attribution 4.0 Unported License"</em>. The Old Testament's
    /// is First1KGreek's, Attribution-ShareAlike, and being the more demanding of the two it is the
    /// text's; each part is credited by name beside it.
    /// </summary>
    public static TextDefinition Definition => new(
        Slug: Slug,
        Name: "Codex Alexandrinus",
        NameNative: "ΚΩΔΙΞ ΑΛΕΞΑΝΔΡΙΝΟΣ",
        Kind: TextKind.ManuscriptTradition,
        Language: "grc",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.Septuagint,
        PublishedYear: null,
        SourceUrl: NewTestament.Url,
        RightsHolder: "The Institut für Neutestamentliche Textforschung over the New Testament transcription, and "
                      + "the Open Greek and Latin project over the Old Testament transcriptions. Nobody holds the "
                      + "manuscript's text itself.",
        Licence: "CC-BY-SA-4.0",
        LicenceUrl: "https://creativecommons.org/licenses/by-sa/4.0/",
        Redistribution: Redistribution.ShareAlike,
        TextualFamily: "Alexandrian")
    {
        Editors = "Institut für Neutestamentliche Textforschung (New Testament); Richard Rusden Ottley "
                  + "(Isaiah, 1904); Henry Barclay Swete (Genesis and 1–4 Maccabees, 1887–1894)",
        Edition = "London, British Library, Royal MS 1 D V–VIII, copied in the fifth century",
        About = "One of the three great early codices of the Greek Bible, copied in the fifth century and "
                + "given to Charles I in 1627. Its New Testament here is the manuscript itself, word by word as "
                + "the scribe wrote it — unaccented, with the sacred names contracted — from the transcription "
                + "the Institut für Neutestamentliche Textforschung made for its editions: where a later hand "
                + "corrected it, the first hand is the text and the correction a note; what the manuscript has "
                + "lost, Matthew to 25:6, John 6:50–8:52 and 2 Corinthians 4:13–12:6, is absent. Of the Old "
                + "Testament only Isaiah, in Ottley's printing of 1904, is the manuscript as it stands. Genesis "
                + "to 46:28 and 1–4 Maccabees are Swete's printing of it, which accents, punctuates and divides "
                + "the words as a modern edition does; in Genesis the passages the codex has lost and Swete "
                + "supplied from other manuscripts are left out. The rest of the codex's Old Testament exists "
                + "in print only as readings in Swete's notes, and is not here.",
        RightsNote = "The manuscript is out of copyright. What carries a licence is each transcription: the "
                     + "New Testament's, by INTF, is Creative Commons Attribution 4.0; the Old Testament's, "
                     + "made for Open Greek and Latin's First1KGreek, is Attribution-ShareAlike 4.0, which "
                     + "obliges anything derived from those books to be shared alike. Modified: the "
                     + "transcription labels 69 verses of 3 John, Jude, Hebrews 13 and 1 Timothy 1 in a form "
                     + "other than its own, and Essenthos places them by that form, checked verse for verse "
                     + "against CNTR's independent transcription of the manuscript. " + OttleyIsaiah.Note,
        Citation = "Codex Alexandrinus (GA 02), New Testament transcription by the Institut für "
                   + "Neutestamentliche Textforschung, New Testament Virtual Manuscript Room, CC BY 4.0; Isaiah "
                   + "from R. R. Ottley (ed.), The Book of Isaiah according to the Septuagint (Codex "
                   + "Alexandrinus), Cambridge 1904; Genesis and 1–4 Maccabees from H. B. Swete (ed.), The Old "
                   + "Testament in Greek, Cambridge 1887–1894; both in the Open Greek and Latin First1KGreek "
                   + "transcription, CC BY-SA 4.0.",
        PartSources = [NewTestament, Genesis, Isaiah],
    };

    /// <summary>
    /// The texts it is joined to verse by verse before any of its words are aligned: Nestle 1904 for
    /// the New Testament, and Brenton's and Swete's Septuagints for the Old. A book the frame does not
    /// place is joined only in the chapters where the two print the same verses.
    /// </summary>
    public static IReadOnlyList<DeclaredVersePair> VersePairs =>
    [
        .. new[] { Sources.NestleSlug, Sources.BrentonSeptuagintSlug, Sources.SweteSlug }
            .Select(to => new DeclaredVersePair(Slug, to, new HashSet<int>()) { AgreeingChaptersOnly = true }),
    ];

    /// <summary>The codex's books in the order it has them: the Law, the Prophets, Maccabees, then the New Testament.</summary>
    public static TextSource Read(string resources)
    {
        var folder = Path.Combine(resources, Folder);
        var transcription = Path.Combine(folder, NtvmrTranscription.File);
        if (!File.Exists(transcription))
        {
            throw new InvalidOperationException(
                $"{Slug} is missing {transcription}. Run scripts/fetch-alexandrinus.ps1, which fetches INTF's "
                + "transcription and Swete's books beside it.");
        }

        var books = new List<BookDraft>();
        foreach (var (work, canonical) in SweteAlexandrinus.Books.Take(1))
        {
            books.Add(Book(canonical, books.Count + 1, SweteAlexandrinus.Read(folder, work)));
        }

        books.Add(OttleyTextSource.Read(Path.Combine(resources, "Swete")).Books.Single() with
        {
            Position = books.Count + 1,
        });

        foreach (var (work, canonical) in SweteAlexandrinus.Books.Skip(1))
        {
            books.Add(Book(canonical, books.Count + 1, SweteAlexandrinus.Read(folder, work)));
        }

        books.AddRange(NtvmrTranscription.Books(transcription, books.Count + 1));
        return new TextSource(Definition, books);
    }

    private static BookDraft Book(int canonical, int position, IReadOnlyList<SweteChapter> chapters) => new(
        CanonicalOrdinal: canonical,
        Position: position,
        Name: BookReferences.Name(canonical),
        Slug: BookReferences.Slug(canonical),
        Abbreviation: BookReferences.Abbreviation(canonical),
        Chapters: [.. chapters.Select(chapter => new ChapterDraft(
            chapter.Number,
            [.. chapter.Verses.Select(verse => new VerseDraft(
                verse.Number,
                [.. verse.Words.Select(word => new WordDraft(word.Surface, word.Trailer))],
                verse.Label))]))]);
}
