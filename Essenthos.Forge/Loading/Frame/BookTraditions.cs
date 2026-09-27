using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Frame;

/// <summary>
/// Books an edition numbers in another tradition than the rest of it.
///
/// The Open New Ukrainian Translation numbers the Psalms as the Greek does, as its note on Psalm 9
/// says ("we use the numbering by the Greek translation that the Ukrainian reader is used to"): its
/// Psalm 22 is the Hebrew's 23, and it counts the titles as verses. Everything else it numbers as the
/// Hebrew and the English do. Placed by the Hebrew's rules throughout, each of its psalms from the
/// tenth on stood at the psalm before.
/// </summary>
internal static class BookTraditions
{
    private const int Psalms = 19;

    private static readonly Dictionary<string, IReadOnlyDictionary<int, Versification>> Editions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [EbibleTextSource.BiblicaUkrainian] = new Dictionary<int, Versification> { [Psalms] = Versification.Septuagint },
        };

    /// <summary>The books of this edition numbered in a tradition of their own; empty for most.</summary>
    public static IReadOnlyDictionary<int, Versification> For(string slug) =>
        Editions.GetValueOrDefault(slug) ?? new Dictionary<int, Versification>();
}
