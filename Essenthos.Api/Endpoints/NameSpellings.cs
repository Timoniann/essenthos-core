using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>One name a record bears, with the Strong numbers of the words that bear it.</summary>
internal sealed record BorneName(string Label, IReadOnlyCollection<string> Numbers);

/// <summary>
/// Each name a record bears, as the texts of the reader's language print it.
///
/// <para>
/// A record holds several names and the corpus keeps a form in another language for the record only:
/// <em>Йосип</em> for the Joseph of Matthew 13:55, whose other name in the dataset is <em>Joses</em>.
/// The words that name him in the reader's texts say which of his names each one prints, through
/// the Strong number the annotation went through (the word's own, or that of the original word it
/// was carried from), as <see cref="Annotations"/> reads it for a verse. The spellings of one name are
/// counted over every text of the language and given as the nominative of the family printed most,
/// as a renamed heading is (<see cref="RenamedHeadings"/>); where that family is the one the record's
/// own form belongs to, the record's form stands: the Ohienko Bible prints Joses as <em>Йосип</em>.
/// </para>
///
/// <para>
/// A name no word of the language is counted under has no spelling, and the caller shows the
/// dataset's label for it, said to be the source's.
/// </para>
/// </summary>
internal static class NameSpellings
{
    public static async Task<Dictionary<string, string>> Of(
        AppDbContext db,
        int entityId,
        IReadOnlyCollection<BorneName> names,
        string? language,
        string? held,
        CancellationToken cancellationToken)
    {
        var local = EntityNames.Local(language);
        var wanted = names.SelectMany(n => n.Numbers).ToHashSet(StringComparer.Ordinal);
        if (local is null || wanted.Count == 0)
        {
            return [];
        }

        var words = await db.WordEntities
            .Where(a => a.EntityId == entityId && a.Word!.Text!.Language == local)
            .Select(a => new { a.WordId, a.Word!.Surface, a.Word.StrongNumber, a.Note })
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
        var carried = sources.Count == 0
            ? []
            : await db.Words
                .Where(w => sources.Contains(w.Id) && w.StrongNumber != null)
                .Select(w => new { w.Id, w.StrongNumber })
                .ToDictionaryAsync(w => w.Id, w => w.StrongNumber!, cancellationToken);

        var counted = words
            .Select(w => (
                Number: w.StrongNumber
                        ?? (Annotations.CarriedFrom(w.Note) is { } source ? carried.GetValueOrDefault(source) : null)
                        ?? Annotations.LeadingNumber(w.Note),
                Form: w.Surface))
            .Where(w => w.Number is not null && wanted.Contains(w.Number) && w.Form.Length > 0)
            .GroupBy(w => (w.Number!, w.Form))
            .Select(g => (g.Key.Item1, g.Key.Form, Occurrences: g.Count()))
            .ToList();

        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            List<(string Form, string Folded, int Occurrences)> spellings =
            [
                .. counted
                    .Where(c => name.Numbers.Contains(c.Item1))
                    .GroupBy(c => c.Form, StringComparer.Ordinal)
                    .Select(g => (g.Key, NameFolding.Fold(g.Key), g.Sum(c => c.Occurrences))),
            ];
            if (spellings.Count == 0)
            {
                continue;
            }

            if ((RenamedHeadings.Heading(spellings, held) ?? held) is { } spelled)
            {
                found[name.Label] = spelled;
            }
        }

        return found;
    }

    /// <summary>The numbers a name's column holds, as one comma-separated string: <c>"H1, NONE"</c>.</summary>
    public static IReadOnlyCollection<string> Numbers(params string?[] stored) =>
    [
        .. stored
            .Where(s => s is { Length: > 0 })
            .SelectMany(s => s!.Split(','))
            .Select(entry => entry.Trim())
            .Where(entry => entry.Length > 1 && entry[0] is 'H' or 'G' && char.IsDigit(entry[1])),
    ];
}
