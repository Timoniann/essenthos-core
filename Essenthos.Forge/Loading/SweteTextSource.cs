using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Corpus;
using Essenthos.Core.Swete;

namespace Essenthos.Core.Loading;

/// <summary>
/// Swete's Septuagint, Cambridge 1887-1894, and the corpus's second Greek Old Testament.
///
/// It is not a replacement for Brenton and the difference is the reason to hold both. Brenton
/// printed a serviceable Greek text to face his English translation; Swete printed a manuscript.
/// His edition is diplomatic: Codex Vaticanus is reproduced as it stands, its losses supplied from
/// Sinaiticus and Alexandrinus, and where the other uncials read otherwise that is recorded in an
/// apparatus at the foot of the page rather than allowed to change the text. So the two disagree
/// constantly and in small ways — forty-three of the fifty books they share divide their text
/// differently somewhere and seven agree exactly — and every one of those disagreements is a
/// reading a scholar can ask about, which is what a second witness is for.
///
/// <para>
/// **Its identifier is <c>LXX-SWETE</c> and it has no aliases.** Nothing else that publishes this
/// edition could be checked at its source: Accordance's module code for it could not be read, the
/// page refusing the request, and <c>LXX</c> belongs to a different Septuagint — CrossWire serves
/// Rahlfs under it — which is why the alias table already refuses that spelling.
/// </para>
///
/// <para>
/// It arrives with no annotation at all: no morphology, no lemmas, no Strong numbers and no
/// alignment to the Hebrew or to anything else. Nothing here links it.
/// </para>
/// </summary>
internal static class SweteTextSource
{
    public const string Slug = "LXX-SWETE";

    private const string FileExtension = ".txt";

    /// <summary>
    /// The files this edition is read from, in the order Swete prints them, with each one's place
    /// in the shared canon.
    ///
    /// The number in a file name is the work's number in the TLG catalogue of the Septuagint rather
    /// than any canonical order, which is why 37 is Amos and 39 is Joel: the Twelve stand in the
    /// Greek order and the shared canon is alphabetical in Hebrew order. The gaps are works nobody
    /// has transcribed — 07 and 09 are the second Greek forms of Joshua and Judges, 22 is the
    /// Sinaiticus Tobit, and 30 is Ecclesiastes, which is the one book of the Hebrew canon this
    /// edition cannot supply.
    ///
    /// Four files this folder holds are deliberately not here. Three are the Old Greek of Susanna,
    /// Daniel and Bel: Swete prints them beneath the text he takes from Vaticanus, and Vaticanus
    /// reads Theodotion in all three, so the Old Greek is another manuscript's reading of the same
    /// book rather than another edition of it — a second witness, which the model holds as a text
    /// of its own and not as a second book at one ordinal. The fourth is the Odes, whose chapters
    /// this edition numbers <c>iva</c> and <c>ivb</c> where the corpus addresses a chapter by an
    /// integer, and whose verses keep the numbering of the passages they are taken from.
    ///
    /// Isaiah is missing for a different and worse reason, recorded on <see cref="NotLoaded"/>.
    /// </summary>
    private static readonly (string File, int Canonical)[] Canon =
    [
        ("01.Genesis", 1), ("02.Exodus", 2), ("03.Leviticus", 3), ("04.Numeri", 4),
        ("05.Deuteronomium", 5), ("06.Josue", 6), ("08.Judices", 7), ("10.Ruth", 8),
        ("11.Regnorum_I", 9), ("12.Regnorum_II", 10), ("13.Regnorum_III", 11),
        ("14.Regnorum_IV", 12), ("15.Paralipomenon_I", 13), ("16.Paralipomenon_II", 14),
        ("17.Esdras_A", 68), ("18.Esdras_B", SecondEsdras.Ezra), ("19.Esther", 17),
        ("20.Judith", 71), ("21.Tobias", 70), ("23.Machabaeorum_i", 73),
        ("24.Machabaeorum_ii", 74), ("25.Machabaeorum_iii", 80), ("26.Machabaeorum_iv", 81),
        ("27.Psalmi", 19), ("29.Proverbia", 20), ("31.Canticum", 22), ("32.Job", 18),
        ("33.Sapientia_Salomonis", 75), ("34.Ecclesiasticus", 72), ("35.Psalmi_Salomonis", 84),
        ("36.Osee", 28), ("37.Amos", 30), ("38.Michaeas", 33), ("39.Joel", 29),
        ("40.Abdias", 31), ("41.Jonas", 32), ("42.Nahum", 34), ("43.Habacuc", 35),
        ("44.Sophonias", 36), ("45.Aggaeus", 37), ("46.Zacharias", 38), ("47.Malachias", 39),
        ("49.Jeremias", 24), ("50.Baruch", 67), ("51.Threni_seu_Lamentationes", 25),
        ("52.Epistula_Jeremiae", 76), ("53.Ezechiel", 26),
        ("55.Susanna_Theodotionis_versio", 77), ("57.Daniel_Theodotionis_versio", 27),
        ("59.Bel_et_Draco_Theodotionis_versio", 78),
    ];

    /// <summary>
    /// The files the folder holds and this edition does not read, so that each absence is a
    /// decision somebody can find rather than a book that quietly never arrived.
    ///
    /// <c>48.Isaias</c> is the serious one: it is not Swete's. The transcription it comes from
    /// holds two Greek editions of Isaiah — Swete's, and Ottley's *The Book of Isaiah according to
    /// the Septuagint (Codex Alexandrinus)*, Cambridge 1904 — and the script that produced these
    /// files names its output after the book rather than after the edition, so for the one book
    /// with two editions the second overwrote the first. What is in the file is Ottley's
    /// Alexandrinus text: it opens with the running head of Ottley's page and spells Uzziah Ὀζίου
    /// where Swete spells him Ὀζείου. Loading it would put a different manuscript into the corpus
    /// under this edition's name, which is the one thing a witness must never do.
    ///
    /// The other four are the Odes, whose chapters this edition numbers with Roman letters, and the
    /// Old Greek of Susanna, Daniel and Bel, which are a second witness rather than a second book.
    /// </summary>
    public static IReadOnlyList<string> NotLoaded =>
    [
        "48.Isaias",
        "28.Odae",
        "54.Susanna_translatio_Graeca",
        "56.Daniel_translatio_Graeca",
        "58.Bel_et_Draco_translatio_Graeca",
    ];

    public static string FileName(string book) => book + FileExtension;

    /// <summary>
    /// The licence is on the transcription and not on the text, and both statements attached to the
    /// bytes say the same thing. <c>nathans/lxx-swete</c>'s README: <em>"The Greek text and its
    /// annotations in the data directory are published under the terms of the Creative Commons
    /// Attribution-ShareAlike 4.0 International (CC BY-SA 4.0) license."</em> Its upstream, Open
    /// Greek and Latin's First1KGreek, ships the full CC BY-SA 4.0 legal code as
    /// <c>license.md</c> and declares <c>CC-BY-SA-4.0</c> in its Zenodo record. Both are kept
    /// beside the data, because a licence that lives only at a URL is one nobody can check offline.
    /// </summary>
    public static TextDefinition Definition => new(
        Slug: Slug,
        Name: "Swete's Septuagint",
        NameNative: "Η ΠΑΛΑΙΑ ΔΙΑΘΗΚΗ ΚΑΤΑ ΤΟΥΣ ΕΒΔΟΜΗΚΟΝΤΑ",
        Kind: TextKind.CriticalEdition,
        Language: "grc",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.Septuagint,
        PublishedYear: 1887,
        SourceUrl: "https://github.com/nathans/lxx-swete",
        RightsHolder: "Nathan D. Smith, and the Open Greek and Latin steering committee, over the "
                      + "machine-readable edition. Nobody holds the text itself.",
        Licence: "CC-BY-SA-4.0",
        LicenceUrl: "https://creativecommons.org/licenses/by-sa/4.0/",
        Redistribution: Redistribution.ShareAlike,
        TextualFamily: "Septuagint")
    {
        Editors = "Henry Barclay Swete (1835-1917), Regius Professor of Divinity at Cambridge",
        Edition = "The Old Testament in Greek according to the Septuagint, Cambridge University Press, "
                  + "three volumes, 1887-1894; transcribed from the later printings of volume 1 (1901), "
                  + "volume 2 (1896) and volume 3 (1905)",
        EditionYear = 1894,
        About = "A manuscript printed, rather than a text established. Swete set out to give students "
                + "Codex Vaticanus as it stands — its spellings, its divisions, its mistakes — filling "
                + "the places where the codex is lost from Sinaiticus and Alexandrinus and putting what "
                + "the other uncials read into an apparatus at the foot of the page instead of into the "
                + "text. That is the opposite of the method Rahlfs used a generation later, which was to "
                + "weigh the manuscripts and print what their editor judged the original to have been, "
                + "and it is why Swete is still opened: it is the shortest way to find out what one "
                + "fourth-century book actually says. Who first put these books into Greek is not known "
                + "— the translation was made in Alexandria between roughly the third and the first "
                + "century BC, by different hands book by book. The apparatus is not here; only the "
                + "text is, and it carries no annotation of any kind.",
        RightsNote = "Two rights questions and they have different answers. Swete's text is out of "
                     + "copyright everywhere: he died in 1917 and the volumes were printed between 1887 "
                     + "and 1905. What carries a licence is the transcription — Open Greek and Latin "
                     + "photographed and corrected the pages for its First1KGreek project, and Nathan D. "
                     + "Smith converted that into the one-token-per-line files loaded here — and both "
                     + "state Creative Commons Attribution-ShareAlike 4.0. So the obligation is on this "
                     + "digitisation of the edition and not on the edition, and it is an obligation to "
                     + "credit and to share alike anything derived from these files.",
        Citation = "Henry Barclay Swete (ed.), The Old Testament in Greek according to the Septuagint, "
                   + "Cambridge University Press, 1887-1894, in the digital edition of Nathan D. Smith "
                   + "(nathans/lxx-swete) derived from the Open Greek and Latin First1KGreek transcription "
                   + "(tlg0527), CC BY-SA 4.0.",
    };

    /// <summary>
    /// Esdras B, which is Ezra and Nehemiah under one heading: chapters 1 to 10 are Ezra and 11 to
    /// 23 are Nehemiah 1 to 13.
    ///
    /// Split here rather than left to the frame for the reason Brenton's is: the versification data
    /// numbers Greek Nehemiah from one, so its rules cannot be found by a verse calling itself Ezra
    /// 13:33, and kept whole the last thirteen chapters would have no address in the shared frame at
    /// all while Nehemiah went missing from a witness that has it.
    /// </summary>
    private static class SecondEsdras
    {
        public const string File = "18.Esdras_B";

        public const int Ezra = 15;

        public const int Nehemiah = 16;

        /// <summary>The last chapter of Esdras B that belongs to Ezra.</summary>
        public const int LastEzraChapter = 10;
    }

    public static TextSource Read(string folder)
    {
        var books = new List<BookDraft>(Canon.Length + 1);
        var position = 0;

        foreach (var (file, canonical) in Canon)
        {
            var path = Path.Combine(folder, FileName(file));
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"{Slug} is missing {FileName(file)}. Run scripts/fetch-swete.ps1 rather than "
                    + "loading part of an edition as though it were the whole of one — a book quietly "
                    + "absent from a witness reads as a book the witness does not contain.");
            }

            var read = SweteReader.Read(File.ReadLines(path));
            var chapters = read.Chapters.Select(Chapter).ToList();

            if (file == SecondEsdras.File)
            {
                books.Add(Book(SecondEsdras.Ezra, ++position,
                    [.. chapters.Where(chapter => chapter.Number <= SecondEsdras.LastEzraChapter)]));
                books.Add(Book(SecondEsdras.Nehemiah, ++position,
                    [.. chapters
                        .Where(chapter => chapter.Number > SecondEsdras.LastEzraChapter)
                        .Select(chapter => chapter with
                        {
                            Number = chapter.Number - SecondEsdras.LastEzraChapter,
                        })]));
                continue;
            }

            books.Add(Book(canonical, ++position, chapters));
        }

        return new TextSource(Definition, books);
    }

    private static BookDraft Book(int canonical, int position, IReadOnlyList<ChapterDraft> chapters) => new(
        CanonicalOrdinal: canonical,
        Position: position,
        Name: BookReferences.Name(canonical),
        Slug: BookReferences.Slug(canonical),
        Abbreviation: BookReferences.Abbreviation(canonical),
        Chapters: chapters);

    private static ChapterDraft Chapter(SweteChapter chapter) => new(
        chapter.Number,
        [.. chapter.Verses.Select(verse => new VerseDraft(
            verse.Number,
            [.. verse.Words.Select(word => new WordDraft(word.Surface, word.Trailer))],
            verse.Label))]);
}
