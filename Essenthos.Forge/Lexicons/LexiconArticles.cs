using System.Globalization;
using Essenthos.Core.Commentary;
using Essenthos.Core.Strong;

namespace Essenthos.Core.Lexicons;

/// <summary>One article of a lexicon under one Strong number, as <c>lexicon_article</c> holds it.</summary>
internal sealed record LexiconArticleRow(
    string Dictionary,
    string StrongNumber,
    int Ordinal,
    string Entry,
    string Headword,
    string? Status,
    string Body,
    string Source);

/// <summary>
/// An article being written into the blocks a commentary is written in (<see cref="CommentBuilder"/>), with what a
/// lexicon adds: its Hebrew and Greek marked by the script they are written in, so the page sets each in its own font
/// and direction whichever markup the source used, and its senses nested.
/// </summary>
internal sealed class ArticleWriter
{
    private readonly CommentBuilder builder = new();

    public int Depth { get; private set; }

    /// <summary>Starts a paragraph at the depth given, or at the current one.</summary>
    public void Paragraph(int? depth = null)
    {
        Depth = Math.Max(0, depth ?? Depth);
        builder.Begin("p", Depth);
    }

    public void Push(CommentBuilder.Mark mark) => builder.Push(mark);

    public void Pop() => builder.Pop();

    public void Cite(int[]? verse) => builder.Cite(verse);

    /// <summary>The Strong number the text being written names, if any.</summary>
    public string? Named { get; private set; }

    public void Name(string? number)
    {
        Named = number;
        builder.Name(number);
    }

    /// <summary>Writes text, its runs of Hebrew and Greek letters marked as such; spaces and marks between two runs of one script stay in the run.</summary>
    public void Text(string text)
    {
        var at = 0;
        while (at < text.Length)
        {
            var script = ScriptOf(text[at]);
            var end = at + 1;
            while (end < text.Length)
            {
                var next = ScriptOf(text[end]);
                if (next == script)
                {
                    end++;
                    continue;
                }

                if (script != Script.Latin && next == Script.Neutral && Continues(text, end, script))
                {
                    end++;
                    continue;
                }

                break;
            }

            var run = text[at..end];
            var mark = script switch
            {
                Script.Hebrew => CommentBuilder.Mark.Hebrew,
                Script.Greek => CommentBuilder.Mark.Greek,
                _ => CommentBuilder.Mark.None,
            };
            if (mark == CommentBuilder.Mark.None)
            {
                builder.Text(run);
            }
            else
            {
                builder.Push(mark);
                builder.Text(run);
                builder.Pop();
            }

            at = end;
        }
    }

    public string? Json() => builder.Json();

    private enum Script
    {
        Latin,
        Neutral,
        Hebrew,
        Greek,
    }

    /// <summary>Whether the neutral characters from <paramref name="at"/> lead to another letter of the script.</summary>
    private static bool Continues(string text, int at, Script script)
    {
        for (var i = at; i < text.Length; i++)
        {
            var next = ScriptOf(text[i]);
            if (next != Script.Neutral)
            {
                return next == script;
            }
        }

        return false;
    }

    private static Script ScriptOf(char c) => c switch
    {
        >= '֑' and <= '״' or >= 'יִ' and <= 'ﭏ' => Script.Hebrew,
        >= 'Ͱ' and <= 'Ͽ' or >= 'ἀ' and <= '῿' => c is ';' or '·' ? Script.Neutral : Script.Greek,
        >= '̀' and <= 'ͯ' => Script.Neutral,
        ' ' or ' ' or '-' or ',' or '·' => Script.Neutral,
        _ => Script.Latin,
    };
}

/// <summary>What the lexicon readers share: Strong numbers as the sources write them, and the verses their references name.</summary>
internal static class LexiconText
{
    /// <summary>
    /// A number of Strong's dictionaries in the corpus's spelling (<c>H430</c>), from any of the ways a lexicon writes it:
    /// <c>430</c> with the language given, <c>H0430</c>, or an extended number whose trailing letters part what Strong
    /// left as one (<c>H1254a</c>, <c>G0001G</c>), which files under the number it extends. Null for a number Strong's
    /// dictionary has no entry at.
    /// </summary>
    public static string? Strong(string? written, char? language = null)
    {
        if (string.IsNullOrWhiteSpace(written))
        {
            return null;
        }

        var text = written.Trim();
        if (char.IsDigit(text[0]) && language is { } letter)
        {
            text = letter + text;
        }

        var digits = text.Length;
        while (digits > 1 && char.IsLetter(text[digits - 1]))
        {
            digits--;
        }

        if (StrongNumbers.Normalize(text[..digits]) is not { } number)
        {
            return null;
        }

        var value = int.Parse(number.AsSpan(1), CultureInfo.InvariantCulture);
        var last = number[0] == StrongNumbers.Hebrew ? StrongNumbers.LastHebrew : StrongNumbers.LastGreek;
        return value <= last ? number : null;
    }

    /// <summary>
    /// The verse an OSIS reference names, as commentaries cite them; a half verse (<c>Gen.30.20!a</c>) names its verse.
    /// Null for one in the Septuagint's own numbering (<c>LXX:Ps.21.2</c>), which the reader does not open by.
    /// </summary>
    public static int[]? Verse(string? osis)
    {
        if (string.IsNullOrWhiteSpace(osis) || osis.Contains(':'))
        {
            return null;
        }

        var bang = osis.IndexOf('!');
        return CommentMarkup.Verse(bang < 0 ? osis : osis[..bang]);
    }

    /// <summary>The rows of one lexicon numbered within each Strong number, in the order the lexicon gives them.</summary>
    public static IEnumerable<LexiconArticleRow> Numbered(IEnumerable<LexiconArticleRow> rows)
    {
        var next = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var ordinal = next.GetValueOrDefault(row.StrongNumber) + 1;
            next[row.StrongNumber] = ordinal;
            yield return row with { Ordinal = ordinal };
        }
    }
}
