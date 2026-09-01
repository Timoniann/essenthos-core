namespace Essenthos.Core.Loading.Links;

/// <summary>
/// One word, one token, always.
///
/// The tool reads a verse as a line and splits it on whitespace, and answers in indices into what
/// that split produced. So a word that writes no token, or two, does not shift itself — it shifts
/// every word after it in that verse, and the alignment that comes back is confidently about the
/// wrong words from there to the end.
///
/// This is not hypothetical. BHSA has 6,488 words with no letters at all: the article in לָאוֹר is a
/// vowel on the preposition and has no consonant of its own, and it was written as the empty string.
/// 4,804 verses — one Old Testament verse in five — were aligned with a tail shifted by one, and
/// nothing said so, because a shifted alignment has exactly the shape of a correct one.
/// </summary>
internal static class AlignmentTokens
{
    /// <summary>What stands in for a word with nothing writable, when even its Strong number is gone.</summary>
    public const string Nothing = "\u2205";

    /// <summary>Joins what would otherwise be two tokens, so a word with a space in it stays one.</summary>
    private const char Joined = '\u2011';

    public static string One(string? form)
    {
        var parts = (form ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? string.Join(Joined, parts) : Nothing;
    }

    /// <summary>
    /// One verse, checked rather than trusted. The check is cheap and the failure it catches is
    /// silent, which is the whole argument for it.
    /// </summary>
    public static string Line(IEnumerable<string> tokens, string reference)
    {
        var words = tokens as IReadOnlyList<string> ?? [.. tokens];
        var line = string.Join(' ', words);
        var written = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        return written == words.Count
            ? line
            : throw new InvalidOperationException(
                $"{reference} has {words.Count} words but writes {written} tokens: \"{line}\". Every word " +
                "has to write exactly one, or the alignment is off by one from there to the end of the verse.");
    }
}
