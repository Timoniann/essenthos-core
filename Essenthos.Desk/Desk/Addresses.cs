using Essenthos.Core.Corpus;

namespace Essenthos.Core.Desk;

/// <summary>
/// A verse as the review lists write it — <c>1KI 7:21</c>, <c>NUM 8:2#2</c> — as the address the
/// reading API takes, <c>1-kings:7:21</c>, so the page can show the verse in the King James and in
/// Ohienko beside the question about it.
/// </summary>
internal static class Addresses
{
    /// <summary>The address of one verse, or null where the reference names a chapter, a run, or nothing.</summary>
    public static string? Of(string reference)
    {
        var text = reference.Split('#')[0].Trim();
        var space = text.LastIndexOf(' ');
        if (space < 1 || BookReferences.ResolveOrdinal(text[..space]) is not { } book)
        {
            return null;
        }

        var point = text[(space + 1)..].Split(':');
        return point.Length == 2 && int.TryParse(point[0], out var chapter) && int.TryParse(point[1], out var verse)
            ? $"{BookReferences.Slug(book)}:{chapter}:{verse}"
            : null;
    }
}
