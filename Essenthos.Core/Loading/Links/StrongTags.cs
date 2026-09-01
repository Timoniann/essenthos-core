using Essenthos.Core.Strong;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The Strong tags a Zefania text carries, which are nearly but not quite Strong numbers.
/// </summary>
internal static class StrongTags
{
    /// <summary>
    /// The mark the source puts on a tag it could not settle. Such a tag names no word, and storing
    /// it in a column that says it does is how asterisked tags came to sit in the corpus as though
    /// they were Strong numbers.
    /// </summary>
    private const char Unsettled = '*';

    /// <summary>
    /// Turns a tag into a Strong number, or null where it is not one. The source writes some tags
    /// bare, some with a language letter, and some with a trailing morphology code after a space.
    /// </summary>
    public static string? Read(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tag.Contains(Unsettled))
        {
            return null;
        }

        var value = tag.Trim();
        var space = value.IndexOf(' ');
        if (space >= 0)
        {
            value = value[..space];
        }

        if (value.Length > 0 && !char.IsLetter(value[0]))
        {
            value = "G" + value;
        }

        return StrongNumbers.Normalize(value);
    }
}
