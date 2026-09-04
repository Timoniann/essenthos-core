using Essenthos.Core.Strong;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The Strong tags a Zefania text carries, which are lists of Strong numbers rather than single
/// ones.
///
/// A tag naming several numbers is an English word rendering several original words —
/// <c>1223 5124</c> is διὰ τοῦτο written <em>therefore</em>, <c>4480 6440</c> is מִפְּנֵי written
/// <em>from before</em>. The King James file writes 14,446 of them, 2,725 in the New Testament and
/// 11,721 in the Old, and they name 15,597 numbers beyond the first.
/// </summary>
internal static class StrongTags
{
    /// <summary>
    /// The mark the source puts on a number no English word of its own renders — the Hebrew object
    /// marker H853 above all, and the Greek particles it leaves inside an English idiom. Such a
    /// number names no English word, and storing it in a column that says it does is how asterisked
    /// tags came to sit in the corpus as though they were Strong numbers. It is read per number and
    /// not per tag: <c>3318 *853</c> states H3318 and says H853 is unrendered beside it.
    /// </summary>
    private const char Unsettled = '*';

    /// <summary>
    /// The numbers one tag names, in the order the source writes them and without the unsettled
    /// ones. Empty where the tag names none, which the caller reads as an untagged word.
    /// </summary>
    /// <param name="language">
    /// Which series the numbers belong to. The tag carries digits alone, and this file numbers its
    /// Old Testament in Hebrew and its New Testament in Greek, so only the caller knows which half
    /// it is reading — a number read out of the wrong half is a valid Strong number for the wrong
    /// word.
    /// </param>
    public static IReadOnlyList<string> Read(string? tag, char language)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return [];
        }

        var numbers = new List<string>(2);
        foreach (var token in tag.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Contains(Unsettled))
            {
                continue;
            }

            var value = char.IsLetter(token[0]) ? token : $"{language}{token}";
            if (StrongNumbers.Normalize(value) is { } number)
            {
                numbers.Add(number);
            }
        }

        return numbers;
    }
}
