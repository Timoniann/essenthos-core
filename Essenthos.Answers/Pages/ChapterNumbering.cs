using System.Globalization;
using Essenthos.Core.Endpoints;

namespace Essenthos.Core.Pages;

/// <summary>
/// A reader's address in its first text's numbering, and a chapter's number in the other numbering tradition: the same
/// rules the reader applies (essenthos-web <c>src/lib/numbering.ts</c>, <c>otherNumbering.ts</c>), so the server's copy
/// of a chapter is the chapter, headed by the number, that the reader shows once it takes over.
/// <para>
/// <c>/read/psalms/90?c=RUSV</c> is the Synodal's Psalm 90, which the frame holds as 91; an address that names no
/// text is in the frame's numbering. A text's numbering lists only the addresses it numbers otherwise than the frame
/// (<see cref="TextNumbering"/>).
/// </para>
/// </summary>
internal static class ChapterNumbering
{
    /// <summary>Where a chapter of the text starts in the frame: the frame's chapter, and its verse where it starts part-way.</summary>
    public static (int Chapter, int? Verse) ChapterInFrame(IReadOnlyList<NumberedAddressResponse> addresses, int chapter)
    {
        NumberedAddressResponse? first = null;
        foreach (var address in addresses)
        {
            if (address.Chapter == chapter && (first is null || address.Verse < first.Verse))
            {
                first = address;
            }
        }

        if (first is null || first.Verse > 1)
        {
            return (chapter, null);
        }

        return (first.FrameChapter, first.FrameVerse > 1 ? first.FrameVerse : null);
    }

    /// <summary>Where an address in the text's own numbering stands in the frame: a chapter where its first verse does.</summary>
    public static (int Chapter, int? Verse) InFrame(IReadOnlyList<NumberedAddressResponse> addresses, int chapter, int? verse)
    {
        if (verse is null)
        {
            return ChapterInFrame(addresses, chapter);
        }

        foreach (var address in addresses)
        {
            if (address.Chapter == chapter && address.Verse == verse)
            {
                return (address.FrameChapter, address.FrameVerse);
            }
        }

        return (chapter, verse);
    }

    /// <summary>
    /// The text's own chapters the frame's chapter holds: the Synodal's 114 and 115 in the frame's 116. A chapter it lists
    /// nothing in it numbers as the frame does, and so the verses before the first it lists.
    /// </summary>
    public static IReadOnlyList<int> OwnChapters(IReadOnlyList<NumberedAddressResponse> addresses, int frameChapter)
    {
        var own = new SortedSet<int>();
        var first = int.MaxValue;
        foreach (var address in addresses)
        {
            if (address.FrameChapter == frameChapter)
            {
                own.Add(address.Chapter);
                first = Math.Min(first, address.FrameVerse);
            }
        }

        if (own.Count == 0 || first > 1)
        {
            own.Add(frameChapter);
        }

        return [.. own];
    }

    /// <summary>Chapters said as one figure: <c>90</c>, or <c>114–115</c>.</summary>
    public static string Label(IReadOnlyList<int> chapters) =>
        chapters.Count == 0 ? ""
        : chapters[0] == chapters[^1] ? chapters[0].ToString(CultureInfo.InvariantCulture)
        : $"{chapters[0].ToString(CultureInfo.InvariantCulture)}–{chapters[^1].ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Where a place in the frame stands in the text's own numbering: a chapter by its own number, with the verse it
    /// starts at only where the text's chapter holds something of another chapter of the frame before it.
    /// </summary>
    public static (int Chapter, int? Verse) Own(IReadOnlyList<NumberedAddressResponse> addresses, int frameChapter, int? frameVerse)
    {
        static NumberedAddressResponse? FirstOwn(IEnumerable<NumberedAddressResponse> candidates) =>
            candidates.OrderBy(a => a.Chapter).ThenBy(a => a.Verse).FirstOrDefault();

        if (frameVerse is { } wanted)
        {
            var found = FirstOwn(addresses.Where(a => a.FrameChapter == frameChapter && a.FrameVerse == wanted));
            return found is null ? (frameChapter, wanted) : (found.Chapter, found.Verse);
        }

        var inChapter = addresses.Where(a => a.FrameChapter == frameChapter).ToList();
        if (inChapter.Count == 0 || inChapter.Min(a => a.FrameVerse) > 1)
        {
            return (frameChapter, null);
        }

        var start = inChapter.Min(a => a.FrameVerse);
        var own = FirstOwn(inChapter.Where(a => a.FrameVerse == start))!;
        if (own.Verse <= 1)
        {
            return (own.Chapter, null);
        }

        for (var verse = 1; verse < own.Verse; verse++)
        {
            var earlier = addresses.FirstOrDefault(a => a.Chapter == own.Chapter && a.Verse == verse);
            if (earlier is null ? own.Chapter != frameChapter : earlier.FrameChapter != frameChapter)
            {
                return (own.Chapter, own.Verse);
            }
        }

        return (own.Chapter, null);
    }

    /// <summary>The chapter on screen in the other numbering tradition, and the chapter that tradition numbers as this one.</summary>
    /// <param name="Tradition">greek, hebrewPsalms, english or hebrew, as the wording names them.</param>
    /// <param name="Label">The chapter as the other tradition numbers it, where that is not the number shown.</param>
    /// <param name="Sought">The chapter the other tradition numbers as the one shown, and where it stands in the frame.</param>
    public sealed record Other(string Tradition, string? Label, (int Chapter, int FrameChapter, int? FrameVerse)? Sought);

    /// <summary>
    /// The other numbering of the chapter on screen: answered by the frame's numbering where the text read numbers the
    /// chapter its own way, and by the other tradition's text where it numbers it as the frame does; null where both agree.
    /// </summary>
    public static Other? OtherOf(
        string book,
        int frameChapter,
        int chapterCount,
        IReadOnlyList<int> leadChapters,
        IReadOnlyList<NumberedAddressResponse>? reference)
    {
        var (frame, other) = NumberingTraditions.Of(book);
        var leadLabel = Label(leadChapters);
        if (leadLabel != frameChapter.ToString(CultureInfo.InvariantCulture))
        {
            (int, int, int?)? sought = leadChapters is [var single] && single != frameChapter && single >= 1 && single <= chapterCount
                ? (single, single, null)
                : null;
            return new Other(frame, frameChapter.ToString(CultureInfo.InvariantCulture), sought);
        }

        if (reference is null)
        {
            return null;
        }

        var otherLabel = Label(OwnChapters(reference, frameChapter));
        var at = ChapterInFrame(reference, frameChapter);
        var label = otherLabel != leadLabel ? otherLabel : null;
        (int, int, int?)? soughtThere = at.Chapter != frameChapter ? (frameChapter, at.Chapter, at.Verse) : null;
        return label is null && soughtThere is null ? null : new Other(other, label, soughtThere);
    }
}
