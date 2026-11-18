using System.Text.RegularExpressions;

namespace Essenthos.Core.Door43;

/// <summary>An original word a milestone names.</summary>
/// <param name="Strong">
/// The extended Strong code as unfoldingWord writes it: <c>H0325</c>, or <c>c:H1961</c> where the
/// original word carries a prefixed conjunction, or <c>b:H3117</c> for an inseparable preposition.
/// Each letter before the colon is a morpheme, and BHSA holds each of those as a word of its own —
/// which is why the pieces line up rather than needing to be reconciled.
/// </param>
/// <param name="Content">
/// The word exactly as the source edition writes it, with U+2060 between morphemes. Splitting on that
/// gives one piece per word BHSA holds.
/// </param>
/// <param name="Occurrence">
/// Which occurrence of <paramref name="Content"/> in the verse the milestone stands over, as the source
/// counts it; 0 where the file does not say.
/// </param>
/// <param name="Occurrences">How many times that spelling stands in the verse; 0 where the file does not say.</param>
internal sealed record AlignedOriginal(string Strong, string Content, int Occurrence = 0, int Occurrences = 0)
{
    private const char MorphemeBoundary = '⁠';

    public string[] Morphemes => Content.Split(MorphemeBoundary, StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>
/// How a translated word stands against the words printed beside it. The source cuts one printed word
/// into several <c>\w</c> words where its parts render different things or a part is supplied:
/// <c>bird{s}</c>, <c>he-goats</c>, <c>brother’s</c>. The text prints them as one word.
/// </summary>
/// <param name="Joined">Whether the word is printed against the one before it, with nothing but punctuation between.</param>
/// <param name="After">The words printed against it after it, in order, up to the next space.</param>
internal readonly record struct WordGlue(bool Joined, IReadOnlyList<string> After);

/// <param name="Strong">The innermost milestone's original word: see <see cref="AlignedOriginal.Strong"/>.</param>
/// <param name="Content">See <see cref="AlignedOriginal.Content"/>.</param>
/// <param name="Words">The translated words inside the span, in order.</param>
/// <param name="Occurrence">See <see cref="AlignedOriginal.Occurrence"/>.</param>
/// <param name="Occurrences">See <see cref="AlignedOriginal.Occurrences"/>.</param>
/// <param name="WordOccurrences">
/// For each of <paramref name="Words"/>, which occurrence of that spelling in the verse it is and how
/// many there are, as the source counts them; zeros where the file does not say.
/// </param>
/// <param name="Nest">
/// The original words of the milestones the span stands inside, outermost first. translationCore writes a
/// group of several original words over one phrase as milestones nested one in another, the phrase inside
/// the innermost: every word of the group renders the whole phrase, and the source says no more than that.
/// </param>
/// <param name="Glue">For each of <paramref name="Words"/>, how it is printed against its neighbours.</param>
internal sealed record AlignmentSpan(
    string Strong,
    string Content,
    IReadOnlyList<string> Words,
    int Occurrence = 0,
    int Occurrences = 0,
    IReadOnlyList<(int Occurrence, int Occurrences)>? WordOccurrences = null,
    IReadOnlyList<AlignedOriginal>? Nest = null,
    IReadOnlyList<WordGlue>? Glue = null)
{
    /// <summary>Every original word the span's words render: the nest's, outermost first, then its own.</summary>
    public IReadOnlyList<AlignedOriginal> Originals =>
        [.. Nest ?? [], new AlignedOriginal(Strong, Content, Occurrence, Occurrences)];
}

/// <param name="UnalignedWords">Translated words of the verse that stand outside every span.</param>
/// <param name="Originals">
/// Every original word the verse's milestones name, nested or not, once each: its Strong code, its
/// spelling and its occurrence. It is what says whether a lemma stands in the source verse once.
/// </param>
internal sealed record AlignedVerse(
    int Chapter,
    int Number,
    IReadOnlyList<AlignmentSpan> Spans,
    int UnalignedWords = 0,
    IReadOnlyList<(string Strong, string Content, int Occurrence)>? Originals = null);

/// <summary>
/// unfoldingWord's USFM 3 word alignment, which is a translation with each of its words tied to
/// the original word it renders — stated by the people who did the tying, not inferred by us.
///
/// This ecosystem is the only place a word-level correspondence for a Slavic text is published at
/// all: twelve books of the Ukrainian and three of the Synodal. Everything else the two of them
/// reach, they reach through a model.
///
/// The format nests: a <c>\zaln-s</c> milestone opens a span over one original word, the
/// <c>\w</c> words inside it are the translation of that word, and <c>\zaln-e\*</c> closes it.
/// Spans nest when several original words share a translated phrase: the phrase stands inside the
/// innermost, and renders every original word of the nest.
/// </summary>
internal static partial class Usfm3AlignmentReader
{
    /// <param name="unnumbered">
    /// Whether a milestone that names its original word by spelling alone still states a span. The
    /// earliest translationCore exports leave <c>x-strong</c> and <c>x-lemma</c> empty and write only
    /// <c>x-content</c> and its occurrence, which is everything the join places a word by; the files
    /// that number their words are read without it, so an empty number there stays what it was.
    /// </param>
    public static IReadOnlyList<AlignedVerse> Read(string content, bool unnumbered = false)
    {
        var verses = new List<AlignedVerse>(64);
        var chapter = 0;
        var number = 0;
        var open = new List<OpenSpan>();
        var closed = new List<OpenSpan>();
        var unaligned = 0;
        var originals = new List<(string Strong, string Content, int Occurrence)>();
        var wordsInVerse = 0;
        var run = new List<(OpenSpan? Span, int Index)>();
        var lastWordEnd = -1;

        void CloseVerse()
        {
            open.Clear();
            run.Clear();
            lastWordEnd = -1;

            // A psalm's title stands before the first verse, and the corpus prints it at the head of
            // that verse, so its spans are kept for it rather than dropped.
            if (number == 0)
            {
                return;
            }

            if (closed.Count > 0)
            {
                verses.Add(new AlignedVerse(chapter, number,
                    [.. closed.OrderBy(span => span.First).Select(span => span.Span())], unaligned, [.. originals.Distinct()]));
            }

            closed.Clear();
            originals.Clear();
            unaligned = 0;
            wordsInVerse = 0;
            number = 0;
        }

        foreach (Match token in Tokens().Matches(content))
        {
            if (token.Groups["chapter"].Success)
            {
                CloseVerse();
                closed.Clear();
                originals.Clear();
                wordsInVerse = 0;
                chapter = int.Parse(token.Groups["chapter"].Value);
            }
            else if (token.Groups["verse"].Success)
            {
                CloseVerse();
                number = int.Parse(token.Groups["verse"].Value);
            }
            else if (token.Groups["start"].Success)
            {
                var attributes = token.Groups["start"].Value;
                var original = new AlignedOriginal(
                    Attribute(attributes, "x-strong") ?? string.Empty,
                    Attribute(attributes, "x-content") ?? string.Empty,
                    Occurrence(attributes, "x-occurrence"),
                    Occurrence(attributes, "x-occurrences"));
                originals.Add((original.Strong, original.Content, original.Occurrence));
                var named = original.Strong.Length > 0 || (unnumbered && original.Content.Length > 0);
                open.Add(new OpenSpan(named ? original : null,
                    [.. open.Where(span => span.Original is not null).Select(span => span.Original!)]));
            }
            else if (token.Groups["end"].Success)
            {
                if (open.Count > 0)
                {
                    var span = open[^1];
                    open.RemoveAt(open.Count - 1);
                    if (span.Words.Count > 0 && (span.Original is not null || span.Nest.Count > 0))
                    {
                        closed.Add(span);
                    }
                }
            }
            else if (token.Groups["word"].Success)
            {
                var word = token.Groups["word"].Value.Trim();
                var joined = lastWordEnd >= 0 && Printed(content, lastWordEnd, token.Index);
                if (!joined)
                {
                    run.Clear();
                }

                foreach (var (before, index) in run)
                {
                    before?.Glue[index].After.Add(word);
                }

                OpenSpan? into = null;
                if (open.Count > 0)
                {
                    // The innermost open span is the one that holds this word; the milestones around
                    // it name the rest of the original words it renders.
                    into = open[^1];
                    if (into.Words.Count == 0)
                    {
                        into.First = wordsInVerse;
                    }

                    into.Words.Add(word);
                    var attributes = token.Groups["attributes"].Value;
                    into.WordOccurrences.Add(
                        (Occurrence(attributes, "x-occurrence"), Occurrence(attributes, "x-occurrences")));
                    into.Glue.Add((joined, []));
                }
                else if (number > 0)
                {
                    unaligned++;
                }

                run.Add((into, into?.Words.Count - 1 ?? -1));
                wordsInVerse++;
                lastWordEnd = token.Index + token.Length;
            }
        }

        CloseVerse();
        return verses;
    }

    /// <summary>
    /// Whether two words are printed as one: nothing stands between them, once the milestones are out,
    /// but punctuation. The text the corpus reads is made the same way, so such words are one word there.
    /// </summary>
    private static bool Printed(string content, int from, int to)
    {
        var between = Milestone().Replace(content[from..to], string.Empty);
        return !between.Any(char.IsWhiteSpace) && !between.Contains('\\');
    }

    private static int Occurrence(string attributes, string name) =>
        int.TryParse(Attribute(attributes, name), out var occurrence) ? occurrence : 0;

    private static string? Attribute(string attributes, string name)
    {
        var match = Regex.Match(attributes, $"{Regex.Escape(name)}=\"(?<value>[^\"]*)\"");
        return match.Success ? match.Groups["value"].Value : null;
    }

    /// <summary>
    /// Chapter marks, verse marks, alignment milestones and words, in the order they appear. One
    /// pass, because the format is a stream and the nesting is the only state that matters.
    /// </summary>
    [GeneratedRegex(
        @"\\c[ ]+(?<chapter>\d+)"
        + @"|\\v[ ]+(?<verse>\d+)"
        + @"|\\zaln-s[ ]*\|(?<start>[^\\]*)\\\*"
        + @"|(?<end>\\zaln-e\\\*)"
        + @"|\\w[ ]+(?<word>[^|\\]+)\|(?<attributes>[^\\]*)(?:\\w\*)?")]
    private static partial Regex Tokens();

    /// <summary>An alignment milestone, opening or closing, or a translator's section mark.</summary>
    [GeneratedRegex(@"\\zaln-s\s*\|[^\\]*\\\*|\\zaln-e\\\*|\\ts\\\*")]
    private static partial Regex Milestone();

    /// <param name="Original">The original word the milestone names; null where it names none the reader accepts.</param>
    /// <param name="Nest">The named original words of the milestones open around it, outermost first.</param>
    private sealed record OpenSpan(AlignedOriginal? Original, IReadOnlyList<AlignedOriginal> Nest)
    {
        /// <summary>The place in the verse of its first word, which orders the spans as the translation does.</summary>
        public int First { get; set; }

        public List<string> Words { get; } = [];

        public List<(int Occurrence, int Occurrences)> WordOccurrences { get; } = [];

        public List<(bool Joined, List<string> After)> Glue { get; } = [];

        public AlignmentSpan Span()
        {
            var own = Original ?? Nest[^1];
            var nest = Original is null ? Nest.Take(Nest.Count - 1).ToList() : Nest;
            return new AlignmentSpan(own.Strong, own.Content, Words, own.Occurrence, own.Occurrences, WordOccurrences,
                nest.Count > 0 ? nest : null,
                Glue.Any(glue => glue.Joined || glue.After.Count > 0)
                    ? [.. Glue.Select(glue => new WordGlue(glue.Joined, glue.After))]
                    : null);
        }
    }
}
