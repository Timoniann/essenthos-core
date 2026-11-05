using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Corpus;

/// <summary>One form a text prints for a record under one Strong number, and how often.</summary>
internal sealed record RecordSpellingCount(int EntityId, string StrongNumber, string Form, int Occurrences);

/// <summary>
/// The spellings a record page reads (<c>record_spelling</c>), counted for one text from the words that name a record
/// in it. Each word is counted under the Strong number the annotation went through, as <see cref="Annotations"/> reads
/// it for a verse: the word's own, else that of the original word its note says it was carried from, else the number the
/// note leads with. A word with no number, or printing no letters, is not counted.
/// </summary>
internal static class RecordSpellings
{
    public static async Task<List<RecordSpellingCount>> Count(AppDbContext db, int textId, CancellationToken cancellationToken)
    {
        var words = await db.WordEntities
            .AsNoTracking()
            .Where(a => a.Word!.TextId == textId)
            .Select(a => new { a.EntityId, a.Word!.Surface, a.Word.StrongNumber, a.Note })
            .ToListAsync(cancellationToken);
        if (words.Count == 0)
        {
            return [];
        }

        var sources = words
            .Where(w => w.StrongNumber is null)
            .Select(w => Annotations.CarriedFrom(w.Note))
            .OfType<long>()
            .Distinct()
            .ToList();
        var carried = new Dictionary<long, string>();
        foreach (var chunk in sources.Chunk(20_000))
        {
            foreach (var source in await db.Words
                         .Where(w => chunk.Contains(w.Id) && w.StrongNumber != null)
                         .Select(w => new { w.Id, w.StrongNumber })
                         .ToListAsync(cancellationToken))
            {
                carried[source.Id] = source.StrongNumber!;
            }
        }

        return
        [
            .. words
                .Select(w => (
                    w.EntityId,
                    Number: w.StrongNumber
                            ?? (Annotations.CarriedFrom(w.Note) is { } source ? carried.GetValueOrDefault(source) : null)
                            ?? Annotations.LeadingNumber(w.Note),
                    Form: w.Surface))
                .Where(w => w.Number is not null && w.Form.Length > 0)
                .GroupBy(w => (w.EntityId, Number: w.Number!, w.Form))
                .Select(g => new RecordSpellingCount(g.Key.EntityId, g.Key.Number, g.Key.Form, g.Count())),
        ];
    }
}
