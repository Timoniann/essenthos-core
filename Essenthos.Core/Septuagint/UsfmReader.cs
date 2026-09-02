using System.Text.RegularExpressions;

namespace Essenthos.Core.Septuagint;

/// <param name="Book">The three-letter code from <c>\id</c> — <c>GEN</c>, <c>TOB</c>, <c>DAG</c>.</param>
internal sealed record UsfmBook(string Book, IReadOnlyList<UsfmChapter> Chapters);

internal sealed record UsfmChapter(int Number, IReadOnlyList<UsfmVerse> Verses);

/// <param name="Words">
/// Split on whitespace, with trailing punctuation moved into the word's trailer so that a word is
/// the word and not the word plus a comma. This is what the other Greek texts do, and a corpus
/// where one witness carries its punctuation and another does not is a corpus that cannot compare
/// them.
/// </param>
internal sealed record UsfmVerse(int Number, IReadOnlyList<UsfmWord> Words, string Label = "");

internal sealed record UsfmWord(string Surface, string Trailer);

/// <summary>
/// Just enough USFM for Brenton.
///
/// The file is markers at the start of a line and running text after them, and this one uses
/// almost none of the standard: <c>\id</c>, <c>\c</c>, <c>\v</c>, and a handful of paragraph marks
/// that carry no text of their own. Everything else — <c>\h</c>, <c>\toc</c>, <c>\mt</c> — is a
/// title, and titles are not verses.
///
/// This is deliberately not a USFM implementation. It reads the file that is here, and says so
/// loudly when it meets a marker it has not been told about, rather than dropping the text after
/// it and leaving a verse quietly short.
/// </summary>
internal static partial class UsfmReader
{
    /// <summary>
    /// Markers that introduce or interrupt a paragraph and carry no verse of their own. Text on
    /// their line belongs to the passage — a Psalm's superscription is <c>\d</c> and is part of the
    /// psalm — so their content is kept and attached to whatever verse is open.
    /// </summary>
    private static readonly HashSet<string> Passage =
        ["p", "m", "nb", "b", "q", "q1", "q2", "pi", "mi", "d", "s", "s1", "s2", "ms", "ms1", "sp", "li"];

    /// <summary>Markers that are titles, notes or metadata: read and discarded.</summary>
    private static readonly HashSet<string> Matter =
        ["h", "toc1", "toc2", "toc3", "mt", "mt1", "mt2", "mt3", "is", "is1", "ip", "imt", "rem", "cl"];

    public static UsfmBook Read(string content)
    {
        string? book = null;
        var chapters = new List<UsfmChapter>();
        var verses = new List<UsfmVerse>();
        var words = new List<UsfmWord>();
        var chapter = 0;
        var verse = 0;
        var label = string.Empty;

        void CloseVerse()
        {
            // Words collected before any verse opened are not dropped: they are a heading that
            // introduces the passage, and in the Psalms they are the superscription — 150 of them,
            // which the Greek numbers as part of the psalm. They stay in hand and become the head
            // of the verse that opens next. Silently losing them would be the worse answer, and
            // inventing a verse number for them would be worse still.
            if (verse == 0)
            {
                return;
            }

            if (words.Count > 0)
            {
                verses.Add(new UsfmVerse(verse, [.. words], label));
            }

            words.Clear();
            verse = 0;
            label = string.Empty;
        }

        void CloseChapter()
        {
            CloseVerse();
            if (chapter > 0 && verses.Count > 0)
            {
                chapters.Add(new UsfmChapter(chapter, [.. verses]));
            }

            // A heading with no verse after it in the whole chapter has nowhere to belong.
            words.Clear();
            verses.Clear();
        }

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed[0] != '\\')
            {
                // A continuation line: the verse it belongs to is still open.
                Words(trimmed, words);
                continue;
            }

            var match = Marker().Match(trimmed);
            if (!match.Success)
            {
                throw new InvalidOperationException($"This is not a USFM marker: \"{trimmed}\"");
            }

            var name = match.Groups["marker"].Value;
            var rest = match.Groups["rest"].Value.Trim();

            switch (name)
            {
                case "id":
                    book = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    break;

                case "c":
                    CloseChapter();
                    chapter = int.Parse(rest.Split(' ')[0]);
                    break;

                case "v":
                    CloseVerse();
                    var space = rest.IndexOf(' ');
                    (verse, label) = Number(space < 0 ? rest : rest[..space]);
                    if (space >= 0)
                    {
                        Words(rest[(space + 1)..], words);
                    }

                    break;

                default:
                    if (Passage.Contains(name))
                    {
                        Words(rest, words);
                    }
                    else if (!Matter.Contains(name))
                    {
                        throw new InvalidOperationException(
                            $"Unknown USFM marker \\{name}. Decide whether it carries text before reading a " +
                            "file that uses it — a marker treated as matter drops whatever follows it.");
                    }

                    break;
            }
        }

        CloseChapter();

        return book is null
            ? throw new InvalidOperationException("The file has no \\id, so nothing says which book it is.")
            : new UsfmBook(book, chapters);
    }

    /// <summary>
    /// A verse number, and the letter after it where there is one.
    ///
    /// The Septuagint carries 317 of these — <c>50a</c>, <c>1b</c>, <c>1e</c> — mostly in Greek
    /// Esther, 1 Kings and Proverbs. They are how the Greek numbers material the Hebrew does not
    /// have: Genesis 31 runs 49, 50, 50a, 52, extending a verse rather than inventing one. Dropping
    /// the letter would collide two verses onto one number; dropping the verse would lose the text.
    /// </summary>
    private static (int Number, string Label) Number(string token)
    {
        var digits = 0;
        while (digits < token.Length && char.IsAsciiDigit(token[digits]))
        {
            digits++;
        }

        return digits == 0
            ? throw new InvalidOperationException($"\"{token}\" is not a verse number.")
            : (int.Parse(token[..digits]), token[digits..]);
    }

    /// <summary>
    /// The words of a run of text. Punctuation that trails a word goes into its trailer: Greek
    /// commas, the ano teleia, the full stop and the closing quote are marks on the sentence, not
    /// letters of the word, and a word that carries one cannot be matched against the same word
    /// in another witness that does not.
    /// </summary>
    private static void Words(string text, List<UsfmWord> into)
    {
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var end = token.Length;
            while (end > 0 && !char.IsLetterOrDigit(token[end - 1]) && token[end - 1] != 'ʼ')
            {
                end--;
            }

            if (end == 0)
            {
                // Punctuation standing alone belongs to the word before it.
                if (into.Count > 0)
                {
                    into[^1] = into[^1] with { Trailer = into[^1].Trailer + token + " " };
                }

                continue;
            }

            into.Add(new UsfmWord(token[..end], token[end..] + " "));
        }
    }

    [GeneratedRegex(@"^\\(?<marker>[a-z][a-z0-9]*)\*?(?<rest>.*)$")]
    private static partial Regex Marker();
}
