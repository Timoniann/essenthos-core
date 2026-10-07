using Essenthos.Core.Loading;

namespace Essenthos.Core.Swete;

/// <summary>
/// What taking a verse's chapter number out does to its words, word by word: a numeral standing
/// alone is removed, one run into a word leaves the word, and every other word is the same word in
/// the same order. Read off the verse as the edition is read with the number and without it, so a
/// corpus holding the first can be brought to the second row by row.
/// </summary>
internal static class SweteChapterMarkerEdits
{
    /// <param name="Index">The word's place in the verse as read with the number, from zero.</param>
    /// <param name="Kept">What stays of the word, or null where the whole word goes.</param>
    internal sealed record Edit(int Index, string? Kept);

    public static bool IsNumeral(string surface) =>
        surface.Length > 0 && surface.All(c => "IVXLC".Contains(c)) && SweteReader.Roman(surface) is not null;

    /// <summary>
    /// The edits that make <paramref name="after"/> of <paramref name="before"/>, or a refusal where
    /// the two differ by anything else.
    /// </summary>
    public static List<Edit> Between(IReadOnlyList<WordDraft> before, IReadOnlyList<WordDraft> after)
    {
        var edits = new List<Edit>();
        var j = 0;
        for (var i = 0; i < before.Count; i++)
        {
            var word = before[i];
            if (j < after.Count && word == after[j])
            {
                j++;
                continue;
            }

            if (IsNumeral(word.Surface))
            {
                edits.Add(new Edit(i, null));
                continue;
            }

            var figures = word.Surface.TakeWhile(c => "IVXLC".Contains(c)).Count();
            if (j < after.Count && figures > 0 && SweteReader.Roman(word.Surface[..figures]) is not null
                && word with { Surface = word.Surface[figures..] } == after[j])
            {
                edits.Add(new Edit(i, after[j].Surface));
                j++;
                continue;
            }

            throw new InvalidOperationException(
                $"\"{word.Surface}{word.Trailer}\" differs between the verse read with its chapter number and without it, "
                + "and is not the number.");
        }

        return j == after.Count
            ? edits
            : throw new InvalidOperationException(
                "The verse read without its chapter number has words the verse read with it does not.");
    }
}
