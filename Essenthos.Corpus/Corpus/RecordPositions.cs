using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Corpus;

/// <summary>Where a record's name first stands in one verse of a text, the verse as the text numbers it.</summary>
internal sealed record RecordPositionCount(int EntityId, int Book, int Chapter, int Verse, int Position);

/// <summary>
/// The positions a record page orders a verse's names by (<c>record_position</c>), counted for one text: for every record and
/// every verse of the text that names it, the least position of the words naming it there.
/// </summary>
internal static class RecordPositions
{
    /// <summary>The text whose verses the positions are counted in: the King James, whose numbering every relationship cites.</summary>
    public const string Text = "KJV";

    public static async Task<List<RecordPositionCount>> Count(AppDbContext db, int textId, CancellationToken cancellationToken) =>
    [
        .. (await db.Words
                .AsNoTracking()
                .Where(w => w.TextId == textId)
                .Join(db.WordEntities, w => w.Id, a => a.WordId, (w, a) => new
                {
                    a.EntityId, Book = w.Verse!.Book!.CanonicalOrdinal, w.Verse.ChapterNumber, w.Verse.Number, w.Position,
                })
                .GroupBy(a => new { a.EntityId, a.Book, a.ChapterNumber, a.Number })
                .Select(g => new { g.Key.EntityId, g.Key.Book, g.Key.ChapterNumber, g.Key.Number, Position = g.Min(a => a.Position) })
                .ToListAsync(cancellationToken))
            .Select(p => new RecordPositionCount(p.EntityId, p.Book, p.ChapterNumber, p.Number, p.Position)),
    ];
}
