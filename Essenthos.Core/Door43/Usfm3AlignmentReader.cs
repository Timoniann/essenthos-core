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
internal sealed record AlignmentSpan(string Strong, string Content, IReadOnlyList<string> Words)
{
    private const char MorphemeBoundary = '⁠';

    public string[] Morphemes => Content.Split(MorphemeBoundary, StringSplitOptions.RemoveEmptyEntries);
}

internal sealed record AlignedVerse(int Chapter, int Number, IReadOnlyList<AlignmentSpan> Spans);

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
        var open = new List<(string Strong, string Content, List<string> Words)>();
        var spans = new List<AlignmentSpan>();

        void CloseVerse()
        {
            if (number > 0 && spans.Count > 0)
            {
                verses.Add(new AlignedVerse(chapter, number, [.. spans]));
            }

            spans.Clear();
            open.Clear();
            number = 0;
        }

        foreach (Match token in Tokens().Matches(content))
        {
            if (token.Groups["chapter"].Success)
            {
                CloseVerse();
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
                open.Add((
                    Attribute(attributes, "x-strong") ?? string.Empty,
                    Attribute(attributes, "x-content") ?? string.Empty,
                    []));
            }
            else if (token.Groups["end"].Success)
            {
                if (open.Count > 0)
                {
                    var (strong, text, words) = open[^1];
                    open.RemoveAt(open.Count - 1);
                    if (strong.Length > 0 && words.Count > 0)
                    {
                        spans.Add(new AlignmentSpan(strong, text, words));
                    }
                }
            }
            else if (token.Groups["word"].Success && open.Count > 0)
            {
                // The innermost open span is the one that names this word. An outer span in a nest
                // covers several original words at once and says nothing about which is which.
                open[^1].Words.Add(token.Groups["word"].Value.Trim());
            }
        }

        CloseVerse();
        return verses;
    }

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
        + @"|\\w[ ]+(?<word>[^|\\]+)\|")]
    private static partial Regex Tokens();
}
