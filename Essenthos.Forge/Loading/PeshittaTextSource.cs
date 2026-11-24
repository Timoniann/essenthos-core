using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.TextFabric;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading;

/// <summary>
/// The Peshitta's Old Testament: the Syriac Bible translated from the Hebrew in the second century, in the ETCBC's
/// Text-Fabric encoding of the Leiden edition (<c>Resources/Peshitta</c>, fetched by <c>scripts/fetch-etcbc.ps1</c>).
///
/// Bare text: one Syriac word per slot and what follows it, no lemma and no parsing, so nothing states which of its
/// words renders which Hebrew word. It arrives joined to BHSA verse by verse through the frame and to nothing word by
/// word.
///
/// <para>
/// Its numbering is the Hebrew Bible's, measured chapter by chapter against BHSA on 2026-11-24 (TSK-0230): a psalm's
/// title is not printed, so the psalm starts at the Hebrew's verse 2; the Peshitta of Chronicles leaves out verses and
/// the numbers skip them rather than close up; Daniel 3:1-33 are the Hebrew's and 3:34-90 are the Prayer of Azariah and
/// the Song of the Three, which no Hebrew verse answers. A verse 0 is a heading the edition sets above a book, a chapter
/// or a psalm, and is not read as text, except in Jeremiah 48 and 49, where it is the "concerning Moab" and "concerning
/// the sons of Ammon" the Hebrew prints at the head of verse 1, and is read there.
/// </para>
/// </summary>
internal static class PeshittaTextSource
{
    public const string Slug = "PESHITTA";

    public const string Folder = "Peshitta";

    /// <summary>
    /// Each book of the dataset the text holds, by its acronym, with the canonical book it is and the chapter the
    /// dataset numbers it under where that is not its own (Psalm 151 is chapter 151 of an apocryphal psalter there).
    /// Where a book comes in two witnesses, the one holding the whole book: Tobit A (B has only chapters 7-14),
    /// 1 Maccabees B (A stops at 14:25), and A for the Prayer of Manasseh and Psalm 151, which both hold whole.
    /// Not read: the Apocalypse of Baruch and its Letter (<c>ApBar</c>, <c>EpBar_A</c>, <c>EpBar_B</c>) and Psalms
    /// 152-155 (<c>ApcPs</c>), which have no book in the corpus.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, (int Book, int? Chapter)> Books =
        new Dictionary<string, (int, int?)>(StringComparer.Ordinal)
        {
            ["Gn"] = (1, null), ["Ex"] = (2, null), ["Lv"] = (3, null), ["Nm"] = (4, null), ["Dt"] = (5, null),
            ["Jos"] = (6, null), ["Jd"] = (7, null), ["Ru"] = (8, null), ["Sm1"] = (9, null), ["Sm2"] = (10, null),
            ["Rg1"] = (11, null), ["Rg2"] = (12, null), ["Chr1"] = (13, null), ["Chr2"] = (14, null), ["Ezr"] = (15, null),
            ["Neh"] = (16, null), ["Est"] = (17, null), ["Jb"] = (18, null), ["Ps"] = (19, null), ["Pr"] = (20, null),
            ["Ec"] = (21, null), ["Ct"] = (22, null), ["Is"] = (23, null), ["Jr"] = (24, null), ["Thr"] = (25, null),
            ["Ez"] = (26, null), ["Dn"] = (27, null), ["Hs"] = (28, null), ["Jl"] = (29, null), ["Am"] = (30, null),
            ["Ob"] = (31, null), ["Jon"] = (32, null), ["Mi"] = (33, null), ["Na"] = (34, null), ["Hb"] = (35, null),
            ["Zf"] = (36, null), ["Hg"] = (37, null), ["Sa"] = (38, null), ["Ml"] = (39, null),
            ["Bar"] = (67, null), ["Esr3"] = (68, null), ["Esr4"] = (69, null), ["Tb_A"] = (70, null), ["Jdt"] = (71, null),
            ["Sir"] = (72, null), ["Mc1_B"] = (73, null), ["Mc2"] = (74, null), ["Sap"] = (75, null), ["EpJr"] = (76, null),
            ["Sus"] = (77, null), ["BelDr"] = (78, null), ["OrM_A"] = (79, null), ["Mc3"] = (80, null), ["Mc4"] = (81, null),
            ["ApcPs_A"] = (82, 1), ["Oda"] = (83, null), ["PsS"] = (84, null),
        };

    /// <summary>The chapters whose verse 0 is text the Hebrew prints in verse 1, and is read there.</summary>
    private static readonly HashSet<(string Book, int Chapter)> HeadingIsText = [("Jr", 48), ("Jr", 49)];

    /// <summary>
    /// 2 Chronicles, whose Peshitta is a free rendering that reorders the Hebrew under the Hebrew's numbers: its 10:20-34
    /// render the Hebrew's 11:1-4 among others, and its eight verses of chapter 11 stand at numbers whose Hebrew verses
    /// they do not render (measured by shared letters against BHSA, 2026-11-24). A shared address says nothing there, so
    /// the book is not joined to BHSA verse by verse.
    /// </summary>
    private static readonly HashSet<int> SecondChronicles = [14];

    /// <summary>
    /// BHSA for the Hebrew Bible and Brenton's Septuagint for every book, so the Syriac stands beside the Hebrew it was
    /// translated from and beside the Greek where the Greek holds the book. The deuterocanon's numbering is the Leiden
    /// edition's, which no versification scheme describes, so those books are joined to the Greek only in the chapters
    /// where both print the same verses.
    /// </summary>
    public static IReadOnlyList<DeclaredVersePair> VersePairs =>
    [
        new(Slug, BhsaTextSource.Slug, SecondChronicles) { Never = SecondChronicles },
        new(Slug, SeptuagintTextSource.Slug, SecondChronicles) { AgreeingChaptersOnly = true, Never = SecondChronicles },
    ];

    public static TextDefinition Definition => new(
        Slug: Slug,
        Name: "The Peshitta (Syriac Old Testament)",
        NameNative: "ܦܫܝܛܬܐ",
        Kind: TextKind.ManuscriptTradition,
        Language: "syc",
        Direction: TextDirection.RightToLeft,
        Versification: Versification.Original,
        PublishedYear: null,
        SourceUrl: "https://github.com/ETCBC/peshitta/tree/9850f5addade26f681334aa475570bef9b0b440a/tf/0.2",
        RightsHolder: "The Eep Talstra Centre for Bible and Computer (ETCBC), Vrije Universiteit Amsterdam, over the "
                      + "electronic text; the Peshitta Institute Leiden and Brill over the printed edition, whose critical "
                      + "apparatus is not included",
        Licence: "CC-BY-NC-4.0",
        LicenceUrl: "https://creativecommons.org/licenses/by-nc/4.0/",
        Redistribution: Redistribution.NonCommercialOnly,
        TextualFamily: "Peshitta")
    {
        Script = "Syrc",
        Editors = "Willem Th. van Peursen, Geert Jan Veldman and Constantijn Sikkel (the electronic text), Hannes "
                  + "Vlaardingerbroek (its reading from the printed page) and Dirk Roorda (its Text-Fabric conversion); "
                  + "general editors of the Peshitta Project Bas ter Haar Romeny and Willem van Peursen",
        Edition = "The Old Testament in Syriac according to the Peshitta Version, Peshitta Institute Leiden (Brill, 1987), "
                  + "with Codex Ambrosianus for the books the Leiden edition has not reached; ETCBC Text-Fabric data 0.2",
        EditionYear = 2021,
        About = "The Bible of the Syriac churches: the Old Testament translated from the Hebrew into Syriac, a dialect of "
                + "Aramaic, probably in the second century, and still read in the liturgy of the churches that pray in "
                + "Syriac. It was made from a Hebrew text close to the one the Masoretes later fixed, so where the Hebrew "
                + "and the Greek differ it is a third witness, and its Chronicles is a free rendering that leaves out "
                + "verses. This is the main text of the Leiden edition as the ETCBC read it from the printed page, with "
                + "Codex Ambrosianus, the sixth- or seventh-century manuscript the edition rests on, for the books the "
                + "edition has not reached; the edition's critical apparatus is not included. The text is unvocalised, "
                + "as the edition prints it. It holds the thirty-nine books of the Hebrew Bible and the books beyond "
                + "them that the Syriac Bible carries: Wisdom, Sirach, Tobit, Judith, Baruch, the Letter of Jeremiah, "
                + "Susanna, Bel and the Dragon, 1 and 2 Esdras, four books of Maccabees, the Prayer of Manasseh, Psalm "
                + "151, the Odes and the Psalms of Solomon. Where the dataset gives a book in two witnesses, the one "
                + "holding the whole book is used. The Apocalypse of Baruch and Psalms 152 to 155, which it also holds, "
                + "are not included. The verses are numbered as in the Hebrew; the psalms' titles are not printed, and "
                + "the headings the edition sets above books and chapters are not part of the text.",
        RightsNote = "The ETCBC states that the plain text of the Peshitta and its conversion are under Creative Commons "
                     + "Attribution-NonCommercial 4.0, and that the conversion program is under the MIT licence. The "
                     + "critical apparatus of the printed edition is Brill's and is not in the dataset. The data files "
                     + "themselves name SEDRA of Beth Mardutho as their source, while the repository's own account says "
                     + "the text was read from the Leiden edition; the two accounts are recorded beside the data.",
        Citation = "Willem Th. van Peursen, Geert Jan Veldman, Constantijn Sikkel, Hannes Vlaardingerbroek and Dirk Roorda, "
                   + "ETCBC/peshitta, Zenodo, https://doi.org/10.5281/zenodo.1464757; github.com/ETCBC/peshitta at "
                   + "9850f5a, data version 0.2, CC BY-NC 4.0.",
    };

    public static TextSource Read(string folder)
    {
        if (!File.Exists(Path.Combine(folder, "word.tf")))
        {
            throw new InvalidOperationException(
                $"{Slug} needs the Peshitta's Text-Fabric files in {folder}. Run scripts/fetch-etcbc.ps1 -Only {Folder}.");
        }

        return Build(Project.Load(folder));
    }

    internal static TextSource Build(Project project)
    {
        var words = (IDocument<string>)project.Documents["word"];
        var trailers = (IDocument<string>)project.Documents["trailer"];
        var bookNames = (IDocument<string>)project.Documents["book"];
        var chapters = project.Documents["chapter"];
        var verses = project.Documents["verse"];

        string Word(int slot) => words.GetNullable(slot) ?? string.Empty;
        string Trailer(int slot) => trailers.GetNullable(slot) ?? string.Empty;
        IEnumerable<int> Slots(int node) =>
            project.ObjectSlotsMap[node].SelectMany(range => Enumerable.Range(range.Start, range.End - range.Start + 1));

        // A verse belongs to the book whose slots hold its first slot.
        var bookOfSlot = new Dictionary<int, string>();
        var bookRange = project.NodeTypeRanges["book"];
        for (var node = bookRange.Start; node <= bookRange.End; node++)
        {
            foreach (var slot in Slots(node))
            {
                bookOfSlot[slot] = bookNames[node];
            }
        }

        var read = new Dictionary<string, SortedDictionary<int, List<VerseDraft>>>(StringComparer.Ordinal);
        var headings = new Dictionary<(string, int), List<WordDraft>>();
        var verseRange = project.NodeTypeRanges["verse"];
        for (var node = verseRange.Start; node <= verseRange.End; node++)
        {
            var slots = Slots(node).ToList();
            var acronym = bookOfSlot[slots[0]];
            if (!Books.TryGetValue(acronym, out var placed))
            {
                continue;
            }

            var chapter = placed.Chapter ?? Convert.ToInt32(chapters[node]);
            var number = Convert.ToInt32(verses[node]);
            var drafts = slots.Select(slot => Draft(Word(slot), Trailer(slot))).ToList();
            if (number == 0)
            {
                if (HeadingIsText.Contains((acronym, chapter)))
                {
                    headings[(acronym, chapter)] = drafts;
                }

                continue;
            }

            if (headings.Remove((acronym, chapter), out var heading) && number == 1)
            {
                drafts = [.. heading, .. drafts];
            }

            if (!read.TryGetValue(acronym, out var book))
            {
                read[acronym] = book = [];
            }

            if (!book.TryGetValue(chapter, out var list))
            {
                book[chapter] = list = [];
            }

            list.Add(new VerseDraft(number, drafts));
        }

        if (headings.Count > 0)
        {
            throw new InvalidDataException(
                $"{Slug}: the heading of {string.Join(", ", headings.Keys.Select(k => $"{k.Item1} {k.Item2}"))} found no verse 1 to stand in.");
        }

        var missing = Books.Keys.Where(acronym => !read.ContainsKey(acronym)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidDataException($"{Slug}: the dataset holds no {string.Join(", ", missing)}; it is not the data this reader was written for.");
        }

        var position = 0;
        var books = new List<BookDraft>(read.Count);
        foreach (var (acronym, book) in read.OrderBy(entry => Books[entry.Key].Book))
        {
            var canonical = BibleBookAbbreviation.GetByOrdinal(Books[acronym].Book)
                            ?? throw new InvalidOperationException($"No canonical book {Books[acronym].Book} for {acronym}.");
            books.Add(new BookDraft(
                CanonicalOrdinal: canonical.Ordinal,
                Position: ++position,
                Name: canonical.FullName.Full,
                Slug: Slugs.Of(canonical.StandardAbbreviation.Full),
                Chapters: [.. book.Select(chapter => new ChapterDraft(chapter.Key, chapter.Value))],
                Abbreviation: canonical.StandardAbbreviation.Full));
        }

        return new TextSource(Definition, books);
    }

    /// <summary>
    /// A word as the edition prints it. A slot of punctuation alone (the paragraph mark ܀, a bullet) is kept as a word
    /// with no letters, so the verse reads as printed; its surface stays, and nothing is aligned to it.
    /// </summary>
    private static WordDraft Draft(string word, string trailer) =>
        new(Surface: word, Trailer: trailer, Elided: false);
}
