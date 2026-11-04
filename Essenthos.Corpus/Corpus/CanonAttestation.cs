using Essenthos.Core.Database.Entities;

namespace Essenthos.Core.Corpus;

/// <summary>
/// The books behind each canon a record can be attested in (<see cref="RecordCanonGroups"/>), and which of those canons a
/// reading canon (<see cref="Canons"/>) receives: one book of a group is enough. The Protestant canon receives the Hebrew
/// Bible and the New Testament, so a record attested only in the deuterocanon is hidden from it; the Catholic receives the
/// deuterocanon too; only the Ethiopian receives Enoch and Jubilees.
/// </summary>
internal static class CanonAttestation
{
    public static readonly IReadOnlySet<int> HebrewBooks = new HashSet<int>(Canons.List.Single(c => c.Slug == "tanakh").Ordinals);

    /// <summary>Messale and Tägsas: Proverbs as the Ethiopian Bible divides it, which attests the Hebrew Bible.</summary>
    public static readonly IReadOnlySet<int> ProverbsInTwo = new HashSet<int> { 91, 92 };

    public static readonly IReadOnlySet<int> NewTestamentBooks = new HashSet<int>(
        Canons.List.Single(c => c.Slug == "protestant").Sections.Single(s => s.Slug == "new-testament").Ordinals);

    /// <summary>The books a Greek, Latin or Slavonic canon receives beyond the Hebrew Bible and the New Testament.</summary>
    public static readonly IReadOnlySet<int> WiderBooks = new HashSet<int>(
        Canons.List.Where(c => c.Slug != "ethiopian").SelectMany(c => c.Ordinals)
            .Where(book => !HebrewBooks.Contains(book) && !NewTestamentBooks.Contains(book)));

    /// <summary>The books only the Ethiopian canon receives.</summary>
    public static readonly IReadOnlySet<int> EthiopicBooks = new HashSet<int>(
        Canons.List.Single(c => c.Slug == "ethiopian").Ordinals
            .Where(book => !HebrewBooks.Contains(book) && !NewTestamentBooks.Contains(book) && !WiderBooks.Contains(book)
                           && !ProverbsInTwo.Contains(book)));

    /// <summary>The record canons a reading canon receives: those it holds at least one book of.</summary>
    public static IReadOnlyList<string> Received(CanonDefinition canon)
    {
        var books = canon.Ordinals.ToHashSet();
        return
        [
            .. RecordCanonGroups.All.Where(group => group switch
            {
                RecordCanonGroups.Hebrew => books.Overlaps(HebrewBooks) || books.Overlaps(ProverbsInTwo),
                RecordCanonGroups.GreekNt => books.Overlaps(NewTestamentBooks),
                RecordCanonGroups.Deuterocanon => books.Overlaps(WiderBooks),
                _ => books.Overlaps(EthiopicBooks),
            }),
        ];
    }

    /// <summary>
    /// The canon mode a reader asked for: no canon (every record), or the canon by its slug. False for a slug no canon
    /// has, so a misspelt mode is refused rather than quietly showing everything.
    /// </summary>
    public static bool TryMode(string? slug, out CanonDefinition? mode)
    {
        mode = slug is { Length: > 0 } ? Canons.List.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase)) : null;
        return mode is not null || slug is not { Length: > 0 };
    }

    /// <summary>
    /// The records a canon mode shows: every record a canon it receives attests, and every record no canon attests (one
    /// named in no word, which no mode hides). No mode shows every record.
    /// </summary>
    public static IQueryable<Entity> InCanon(this IQueryable<Entity> records, CanonDefinition? mode)
    {
        if (mode is null)
        {
            return records;
        }

        var received = Received(mode).ToList();
        return records.Where(e => !e.Canons.Any() || e.Canons.Any(c => received.Contains(c.Canon)));
    }
}
