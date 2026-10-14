using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// A text's own numbering of a book against the shared frame.
///
/// <para>
/// A verse's address in the text is the one its edition prints where it prints one — the Synodal
/// prints <em>(90-1)</em> at the head of what the frame lays out as Psalm 91:1 — and otherwise the
/// number its file gives it: Brenton's and the Douay's Psalm 90 are the frame's 91 by their own
/// numbering. Only the addresses that land elsewhere in the frame are listed; one the text numbers
/// as the frame does needs nothing. Where two verses give one address, a printed one outranks a
/// file's number, and otherwise the first in the frame keeps it.
/// </para>
/// </summary>
internal static class TextNumbering
{
    public static async Task<List<NumberedAddressResponse>> Of(
        AppDbContext db,
        int textId,
        int bookOrdinal,
        CancellationToken cancellationToken)
    {
        var verses = await db.VerseReferences
            .Where(r => r.IsPrimary
                        && r.CanonicalBook == bookOrdinal
                        && r.Verse!.TextId == textId
                        && r.Verse.Book!.CanonicalOrdinal == bookOrdinal
                        && r.Verse.Label == string.Empty)
            .OrderBy(r => r.CanonicalChapter).ThenBy(r => r.CanonicalVerse)
            .Select(r => new { r.VerseId, r.Verse!.ChapterNumber, r.Verse.Number, r.CanonicalChapter, r.CanonicalVerse })
            .ToListAsync(cancellationToken);
        if (verses.Count == 0)
        {
            return [];
        }

        var stated = (await db.StatedVerseNumbers
                .Where(s => s.Verse!.TextId == textId
                            && s.Verse.Book!.CanonicalOrdinal == bookOrdinal
                            && s.ChapterLabel == string.Empty)
                .OrderBy(s => s.Position)
                .Select(s => new { s.VerseId, s.ChapterNumber, s.Number })
                .ToListAsync(cancellationToken))
            .ToLookup(s => s.VerseId, s => (s.ChapterNumber, s.Number));

        // The printed addresses first: where an edition prints one, its file's number for a verse
        // is not an address a reader of it uses.
        var claims = verses
            .Where(verse => stated.Contains(verse.VerseId))
            .SelectMany(verse => stated[verse.VerseId].Select(address => (Address: address, verse.CanonicalChapter, verse.CanonicalVerse)))
            .Concat(verses
                .Where(verse => !stated.Contains(verse.VerseId))
                .Select(verse => (Address: (verse.ChapterNumber, verse.Number), verse.CanonicalChapter, verse.CanonicalVerse)));

        var seen = new HashSet<(int, int)>();
        var differing = new List<NumberedAddressResponse>();
        foreach (var (address, frameChapter, frameVerse) in claims)
        {
            if (seen.Add(address) && address != (frameChapter, frameVerse))
            {
                differing.Add(new NumberedAddressResponse(address.Item1, address.Item2, frameChapter, frameVerse));
            }
        }

        return [.. differing.OrderBy(a => a.FrameChapter).ThenBy(a => a.FrameVerse)];
    }
}
