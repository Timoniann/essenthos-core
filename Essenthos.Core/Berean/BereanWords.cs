using System.Text.RegularExpressions;

namespace Essenthos.Core.Berean;

/// <param name="Trailer">
/// Everything between this word and the next: the punctuation, the spaces, the dash that joins
/// <em>seas—no</em> into one printed token but two words.
/// </param>
internal readonly record struct BereanWord(string Surface, string Trailer);

/// <summary>
/// The Berean Standard Bible as published, and how its verses become words.
///
/// **The text comes from the published edition and not from the tables**, which is a decision worth
/// stating because the tables do contain the English. Rebuilding a verse from them means reassembling
/// six columns — the phrase, its punctuation, its opening and closing quotes, its spacing, its
/// <c>vvv</c> marker for a rendering that moved — and measured over 1,500 random verses that rebuild
/// matched the published text 90.8% of the time. A text that is right nine times in ten is not a
/// text. The published file is the edition itself, so it is exact by construction, and the tables
/// are then used for the only thing they alone can say: which English word renders which original
/// word.
///
/// <para>
/// Words are runs of letters rather than whitespace tokens. <em>the seas—no wonder</em> is four
/// words and three printed tokens, and a corpus that stores it as three cannot link the fourth to
/// anything. Everything between one word and the next becomes the first one's trailer, so the verse
/// rebuilds character for character — checked on 4,000 random verses, all 4,000 exact.
/// </para>
/// </summary>
internal static partial class BereanWords
{
    /// <summary>
    /// A word: a run of letters and digits, keeping an apostrophe or hyphen that stands inside one.
    /// <em>God's</em> is one word and so is <em>thirty-two</em>; <em>seas—no</em> is two, because an
    /// em dash is punctuation between words and a hyphen is part of one.
    /// </summary>
    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*")]
    private static partial Regex Word { get; }

    /// <summary>
    /// Every verse of the published edition, by its reference as the file writes it — *Genesis 1:1*.
    /// </summary>
    public static IEnumerable<(string Reference, string Text)> Verses(string path)
    {
        using var reader = new StreamReader(path);

        while (reader.ReadLine() is { } line)
        {
            var cells = line.Split('\t');
            if (cells.Length < 2 || !cells[0].Contains(':', StringComparison.Ordinal))
            {
                continue;
            }

            var reference = cells[0].Trim();
            if (reference.Length > 0)
            {
                yield return (reference, cells[1].Trim());
            }
        }
    }

    /// <summary>
    /// One verse as words and trailers. Anything before the first word — an opening quotation mark —
    /// joins that word's surface, because a verse has no room to put it anywhere else and dropping
    /// it would lose it.
    /// </summary>
    public static List<BereanWord> Split(string verse)
    {
        var words = new List<BereanWord>(32);
        var at = 0;

        foreach (var match in Word.EnumerateMatches(verse))
        {
            if (words.Count > 0)
            {
                words[^1] = words[^1] with { Trailer = verse[at..match.Index] };
                words.Add(new BereanWord(verse.Substring(match.Index, match.Length), string.Empty));
            }
            else
            {
                words.Add(new BereanWord(verse[..(match.Index + match.Length)], string.Empty));
            }

            at = match.Index + match.Length;
        }

        if (words.Count > 0)
        {
            words[^1] = words[^1] with { Trailer = verse[at..] };
        }

        return words;
    }

    /// <summary>
    /// The words of a Berean phrase, with the file's own notation removed: brackets and braces around
    /// a supplied word, and the <c>vvv</c> that marks a rendering standing away from its own word.
    /// A phrase that says only <c>-</c> or <c>. . .</c> is not a rendering and yields nothing.
    /// </summary>
    public static List<string> Rendering(string phrase)
    {
        var words = new List<string>(8);
        foreach (var match in Word.EnumerateMatches(phrase))
        {
            var word = phrase.Substring(match.Index, match.Length);
            if (!string.Equals(word, "vvv", StringComparison.Ordinal))
            {
                words.Add(word);
            }
        }

        return words;
    }

    /// <summary>Two words compared as a reader would: letters and digits only, case set aside.</summary>
    public static bool Same(string left, string right) =>
        string.Equals(Bare(left), Bare(right), StringComparison.Ordinal);

    private static string Bare(string word)
    {
        Span<char> bare = stackalloc char[word.Length];
        var length = 0;
        foreach (var c in word)
        {
            if (char.IsLetterOrDigit(c))
            {
                bare[length++] = char.ToLowerInvariant(c);
            }
        }

        return new string(bare[..length]);
    }
}
