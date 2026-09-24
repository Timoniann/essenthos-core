using System.Text.RegularExpressions;

namespace Essenthos.Core.Door43;

/// <param name="Strong">
/// The extended Strong code as unfoldingWord writes it: <c>H0325</c>, or <c>c:H1961</c> where the
/// original word carries a prefixed conjunction, or <c>b:H3117</c> for an inseparable preposition.
/// Each letter before the colon is a morpheme, and BHSA holds each of those as a word of its own —
/// which is why the pieces line up rather than needing to be reconciled.
/// </param>
/// <param name="Content">
/// The original-language word this span renders, exactly as the source edition writes it, with
/// U+2060 between morphemes. Splitting on that gives one piece per word BHSA holds.
/// </param>
/// <param name="Words">The translated words inside the span, in order.</param>
/// <param name="Occurrence">
/// Which occurrence of <paramref name="Content"/> in the verse the span stands over, as the source
/// counts it; 0 where the file does not say.
/// </param>
/// <param name="Occurrences">How many times that spelling stands in the verse; 0 where the file does not say.</param>
/// <param name="WordOccurrences">
/// For each of <paramref name="Words"/>, which occurrence of that spelling in the verse it is and how
/// many there are, as the source counts them; zeros where the file does not say.
/// </param>
internal sealed record AlignmentSpan(
    string Strong,
    string Content,
    IReadOnlyList<string> Words,
    int Occurrence = 0,
    int Occurrences = 0,
    IReadOnlyList<(int Occurrence, int Occurrences)>? WordOccurrences = null)
{
    private const char MorphemeBoundary = '⁠';

    public string[] Morphemes => Content.Split(MorphemeBoundary, StringSplitOptions.RemoveEmptyEntries);
}

/// <param name="SharedOriginals">
/// Original words standing in a nest over the translated words of an inner span. The source states
/// them; <see cref="Spans"/> does not keep them, because only the innermost span names its words.
/// </param>
/// <param name="UnalignedWords">Translated words of the verse that stand outside every span.</param>
/// <param name="Originals">
/// Every original word the verse's milestones name, nested or not, once each: its Strong code, its
/// spelling and its occurrence. It is what says whether a lemma stands in the source verse once.
/// </param>
internal sealed record AlignedVerse(
    int Chapter,
    int Number,
    IReadOnlyList<AlignmentSpan> Spans,
    int SharedOriginals = 0,
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
/// Spans nest when several original words share a translated phrase; this reads the innermost
/// open span for each word, which is the one that names it.
/// </summary>
internal static partial class Usfm3AlignmentReader
{
    public static IReadOnlyList<AlignedVerse> Read(string content)
    {
        var verses = new List<AlignedVerse>(64);
        var chapter = 0;
        var number = 0;
        var open = new List<OpenSpan>();
        var spans = new List<AlignmentSpan>();
        var shared = 0;
        var unaligned = 0;
        var originals = new List<(string Strong, string Content, int Occurrence)>();

        void CloseVerse()
        {
            open.Clear();

            // A psalm's title stands before the first verse, and the corpus prints it at the head of
            // that verse, so its spans are kept for it rather than dropped.
            if (number == 0)
            {
                return;
            }

            if (spans.Count > 0)
            {
                verses.Add(new AlignedVerse(chapter, number, [.. spans], shared, unaligned, [.. originals.Distinct()]));
            }

            spans.Clear();
            originals.Clear();
            shared = 0;
            unaligned = 0;
            number = 0;
        }

        foreach (Match token in Tokens().Matches(content))
        {
            if (token.Groups["chapter"].Success)
            {
                CloseVerse();
                spans.Clear();
                originals.Clear();
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
                originals.Add((Attribute(attributes, "x-strong") ?? string.Empty,
                    Attribute(attributes, "x-content") ?? string.Empty, Occurrence(attributes, "x-occurrence")));
                open.Add(new OpenSpan(
                    Attribute(attributes, "x-strong") ?? string.Empty,
                    Attribute(attributes, "x-content") ?? string.Empty,
                    Occurrence(attributes, "x-occurrence"),
                    Occurrence(attributes, "x-occurrences")));
            }
            else if (token.Groups["end"].Success)
            {
                if (open.Count > 0)
                {
                    var span = open[^1];
                    open.RemoveAt(open.Count - 1);
                    if (span.Strong.Length > 0 && span.Words.Count > 0)
                    {
                        spans.Add(new AlignmentSpan(
                            span.Strong, span.Content, span.Words, span.Occurrence, span.Occurrences, span.WordOccurrences));
                    }
                    else if (span.Strong.Length > 0 && number > 0)
                    {
                        shared++;
                    }
                }
            }
            else if (token.Groups["word"].Success && open.Count > 0)
            {
                // The innermost open span is the one that names this word. An outer span in a nest
                // covers several original words at once and says nothing about which is which.
                open[^1].Words.Add(token.Groups["word"].Value.Trim());
                var attributes = token.Groups["attributes"].Value;
                open[^1].WordOccurrences.Add(
                    (Occurrence(attributes, "x-occurrence"), Occurrence(attributes, "x-occurrences")));
            }
            else if (token.Groups["word"].Success && number > 0)
            {
                unaligned++;
            }
        }

        CloseVerse();
        return verses;
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
        + @"|\\w[ ]+(?<word>[^|\\]+)\|(?<attributes>[^\\]*)")]
    private static partial Regex Tokens();

    private sealed record OpenSpan(string Strong, string Content, int Occurrence, int Occurrences)
    {
        public List<string> Words { get; } = [];

        public List<(int Occurrence, int Occurrences)> WordOccurrences { get; } = [];
    }
}
