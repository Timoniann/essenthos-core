using Essenthos.Core.TextFabric;

namespace Essenthos.Core.Samaritan;

/// <param name="Consonants">
/// The word as the manuscript writes it. The Samaritan tradition is unpointed, so this is the whole
/// of the text rather than a reading of it — there is no vocalised form to stand beside it.
/// </param>
/// <param name="ParsedFromMasoretic">
/// The dataset says this word's grammatical features were carried over from the Masoretic text
/// rather than established on the Samaritan. It is the difference between the annotation being
/// evidence about this witness and being evidence about the other one, and it is stated per word,
/// so it is kept per word.
/// </param>
public sealed record SamaritanWord(
    string Consonants,
    string Trailer,
    string? Lexeme,
    string? Gloss,
    string? Language,
    string? PartOfSpeech,
    string? Gender,
    string? Number,
    string? Person,
    string? Tense,
    string? SuffixGender,
    string? SuffixNumber,
    string? SuffixPerson,
    SamaritanMorphemes Morphemes,
    bool ParsedFromMasoretic);

/// <summary>
/// The word cut into the pieces the analysis recognises, each written in Hebrew letters and each
/// empty where the word has none. This is what the dataset carries that a plain transcription does
/// not, and it is the same segmentation BHSA states for the Masoretic side — which is what lets a
/// prefix on one side be compared with a prefix on the other rather than with a whole word.
/// </summary>
public sealed record SamaritanMorphemes(
    string Preformative,
    string VerbalStem,
    string Lexeme,
    string VerbalEnding,
    string NominalEnding,
    string UnivalentFinal,
    string PronominalSuffix);

public sealed record SamaritanVerse(int Number, IReadOnlyList<SamaritanWord> Words);

public sealed record SamaritanChapter(int Number, IReadOnlyList<SamaritanVerse> Verses);

public sealed record SamaritanBook(string Name, IReadOnlyList<SamaritanChapter> Chapters);

/// <summary>
/// The Text-Fabric dataset of the Samaritan Pentateuch, read into books, chapters, verses and words.
///
/// The same file format BHSA arrives in and a different shape inside it: BHSA's slot is the word,
/// and here the slot is the <em>sign</em> — one letter — with words, verses, chapters and books all
/// standing over spans of letters. So nothing can be read off a node id directly; everything is
/// reached through the sign it covers.
///
/// <para>
/// The words are morphemes, exactly as BHSA's are: <c>בראשית</c> is <c>ב</c> then <c>ראשית</c> in
/// both datasets, because both come out of the same ETCBC encoding practice. That is why the two
/// can be compared word for word at all, and it is the reason this witness is worth the reader
/// rather than a transcription would be.
/// </para>
/// </summary>
public sealed class SamaritanProject
{
    /// <summary>Which release of the dataset was read, taken from the files rather than the path.</summary>
    public required string Version { get; init; }

    public required IReadOnlyList<SamaritanBook> Books { get; init; }

    /// <summary>
    /// The two Hebrew letters this dataset writes as a single Unicode presentation form, against
    /// the ordinary letter and combining dot every other Hebrew source in the corpus uses.
    ///
    /// Left alone they are silent corruption rather than a visible defect: <c>U+FB2A</c> renders as
    /// שׁ and reads correctly on any screen, but it is not the letter shin, so a search for shin
    /// misses it, the consonantal folding drops it as punctuation, and 12,507 words quietly stop
    /// matching their Masoretic counterparts. Measured before the mapping was added: 20,242 words
    /// read as differing from BHSA against 6,328 after it.
    ///
    /// <para>
    /// Written as escapes on purpose: the presentation form and the pair it stands for are
    /// indistinguishable in any editor, so writing them as Hebrew would hide the defect inside the
    /// fix for it.
    /// </para>
    /// </summary>
    private const string ShinWithShinDot = "\uFB2A";

    private const string ShinWithSinDot = "\uFB2B";

    /// <summary>The letter shin followed by the shin dot, which is what BHSA writes.</summary>
    private const string Shin = "\u05E9\u05C1";

    /// <summary>The letter shin followed by the sin dot.</summary>
    private const string Sin = "\u05E9\u05C2";

    private const string SignType = "sign";
    private const string BookType = "book";
    private const string ChapterType = "chapter";
    private const string VerseType = "verse";
    private const string WordType = "word";

    /// <summary>What the dataset writes where a feature does not apply to a word.</summary>
    private static readonly string[] NotApplicable = ["NA", "unknown", "none", "absent"];

    public static SamaritanProject Load(string path)
    {
        var project = Project.Load(path);
        var signs = project.NodeTypeRanges[SignType];
        var bookNodes = project.NodeTypeRanges[BookType];
        var chapterNodes = project.NodeTypeRanges[ChapterType];
        var verseNodes = project.NodeTypeRanges[VerseType];
        var wordNodes = project.NodeTypeRanges[WordType];

        var otype = Document<string>(project, "otype");
        var bookNames = Document<string>(project, BookType);
        var chapterNumbers = Document<int>(project, ChapterType);
        var verseNumbers = Document<int>(project, VerseType);
        var consonants = Document<string>(project, "g_cons_utf8");
        var trailers = Document<string>(project, "trailer");
        var lexemes = Document<string>(project, "lex_utf8");
        var glosses = Document<string>(project, "gloss");
        var languages = Document<string>(project, "language");
        var partsOfSpeech = Document<string>(project, "sp");
        var genders = Document<string>(project, "gn");
        var numbers = Document<string>(project, "nu");
        var persons = Document<string>(project, "ps");
        var tenses = Document<string>(project, "vt");
        var suffixGenders = Document<string>(project, "prs_gn");
        var suffixNumbers = Document<string>(project, "prs_nu");
        var suffixPersons = Document<string>(project, "prs_ps");
        var preformatives = Document<string>(project, "g_pfm_utf8");
        var verbalStems = Document<string>(project, "g_vbs_utf8");
        var realisedLexemes = Document<string>(project, "g_lex_utf8");
        var verbalEndings = Document<string>(project, "g_vbe_utf8");
        var nominalEndings = Document<string>(project, "g_nme_utf8");
        var univalentFinals = Document<string>(project, "g_uvf_utf8");
        var pronominalSuffixes = Document<string>(project, "g_prs_utf8");
        var fromMasoretic = Document<string>(project, "mt_feat");

        var wordOfSign = Cover(project, wordNodes, signs.End);
        var chapterOfSign = Cover(project, chapterNodes, signs.End);
        var bookOfSign = Cover(project, bookNodes, signs.End);

        var words = new Dictionary<int, SamaritanWord>(wordNodes.End - wordNodes.Start + 1);
        for (var node = wordNodes.Start; node <= wordNodes.End; node++)
        {
            words[node] = new SamaritanWord(
                Consonants: Letters(consonants[node]),
                Trailer: trailers.GetNullable(node) ?? string.Empty,
                Lexeme: Stated(Letters(lexemes.GetNullable(node) ?? string.Empty)),
                Gloss: Stated(glosses.GetNullable(node)),
                Language: Stated(languages.GetNullable(node)),
                PartOfSpeech: Stated(partsOfSpeech.GetNullable(node)),
                Gender: Stated(genders.GetNullable(node)),
                Number: Stated(numbers.GetNullable(node)),
                Person: Stated(persons.GetNullable(node)),
                Tense: Stated(tenses.GetNullable(node)),
                SuffixGender: Stated(suffixGenders.GetNullable(node)),
                SuffixNumber: Stated(suffixNumbers.GetNullable(node)),
                SuffixPerson: Stated(suffixPersons.GetNullable(node)),
                Morphemes: new SamaritanMorphemes(
                    Preformative: Letters(preformatives.GetNullable(node) ?? string.Empty),
                    VerbalStem: Letters(verbalStems.GetNullable(node) ?? string.Empty),
                    Lexeme: Letters(realisedLexemes.GetNullable(node) ?? string.Empty),
                    VerbalEnding: Letters(verbalEndings.GetNullable(node) ?? string.Empty),
                    NominalEnding: Letters(nominalEndings.GetNullable(node) ?? string.Empty),
                    UnivalentFinal: Letters(univalentFinals.GetNullable(node) ?? string.Empty),
                    PronominalSuffix: Letters(pronominalSuffixes.GetNullable(node) ?? string.Empty)),
                ParsedFromMasoretic: string.Equals(
                    fromMasoretic.GetNullable(node), "True", StringComparison.Ordinal));
        }

        var chapters = new Dictionary<int, List<SamaritanVerse>>();
        var chapterOrder = new Dictionary<int, List<int>>();

        for (var node = verseNodes.Start; node <= verseNodes.End; node++)
        {
            var slots = project.ObjectSlotsMap[node];
            var verse = new SamaritanVerse(verseNumbers[node], [.. WordsOver(slots, wordOfSign).Select(w => words[w])]);
            var chapter = chapterOfSign[slots[0].Start];

            if (!chapters.TryGetValue(chapter, out var held))
            {
                chapters[chapter] = held = [];
                var book = bookOfSign[slots[0].Start];
                if (!chapterOrder.TryGetValue(book, out var order))
                {
                    chapterOrder[book] = order = [];
                }

                order.Add(chapter);
            }

            held.Add(verse);
        }

        var books = new List<SamaritanBook>(bookNodes.End - bookNodes.Start + 1);
        for (var node = bookNodes.Start; node <= bookNodes.End; node++)
        {
            books.Add(new SamaritanBook(
                bookNames[node],
                [.. chapterOrder[node].Select(c => new SamaritanChapter(chapterNumbers[c], chapters[c]))]));
        }

        return new SamaritanProject
        {
            Version = otype.Metadata.Version
                      ?? throw new InvalidOperationException(
                          "otype.tf states no @version, so there is no way to record which release of the " +
                          "Samaritan Pentateuch was loaded. Re-run scripts/fetch-samaritan.ps1."),
            Books = books,
        };
    }

    /// <summary>
    /// The nodes of one type, indexed by the sign each covers. Everything above the letter is a
    /// span of letters here, so this is the only way from a verse to the words in it.
    /// </summary>
    private static int[] Cover(Project project, Bhsa.RangeInt nodes, int signs)
    {
        var over = new int[signs + 1];
        for (var node = nodes.Start; node <= nodes.End; node++)
        {
            foreach (var slots in project.ObjectSlotsMap[node])
            {
                for (var sign = slots.Start; sign <= slots.End; sign++)
                {
                    over[sign] = node;
                }
            }
        }

        return over;
    }

    /// <summary>
    /// The words standing over a span of signs, in the order the text writes them. Walked letter by
    /// letter rather than taken as a node range: a word is contiguous and so is a verse, but that is
    /// a property of this release rather than of the format, and walking costs one pass over the
    /// four hundred thousand letters.
    /// </summary>
    private static List<int> WordsOver(IReadOnlyList<Bhsa.RangeInt> slots, int[] wordOfSign)
    {
        var words = new List<int>(32);
        var last = 0;
        for (int i = 0, count = slots.Count; i < count; i++)
        {
            for (var sign = slots[i].Start; sign <= slots[i].End; sign++)
            {
                var word = wordOfSign[sign];
                if (word == 0 || word == last)
                {
                    continue;
                }

                words.Add(word);
                last = word;
            }
        }

        return words;
    }

    /// <summary>
    /// Hebrew as the rest of the corpus writes it. Only the two presentation forms are rewritten;
    /// every other letter this dataset uses is already the ordinary one.
    /// </summary>
    private static string Letters(string value) =>
        value.Contains(ShinWithShinDot, StringComparison.Ordinal)
        || value.Contains(ShinWithSinDot, StringComparison.Ordinal)
            ? value.Replace(ShinWithShinDot, Shin, StringComparison.Ordinal)
                .Replace(ShinWithSinDot, Sin, StringComparison.Ordinal)
            : value;

    /// <summary>
    /// A feature value that says something. The dataset writes "NA" and "unknown" where a feature
    /// does not apply, and storing those is storing the absence of information as information.
    /// </summary>
    private static string? Stated(string? value) =>
        string.IsNullOrEmpty(value) || Array.IndexOf(NotApplicable, value) >= 0 ? null : value;

    private static IDocument<T> Document<T>(Project project, string name) =>
        project.Documents.GetValueOrDefault(name) is IDocument<T> document
            ? document
            : throw new KeyNotFoundException(
                $"The Samaritan Pentateuch dataset has no \"{name}\" feature of the expected type. Run " +
                "scripts/fetch-samaritan.ps1, which takes every feature file of the release it pins.");
}
