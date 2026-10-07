using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Frame;

/// <summary>
/// Books an edition numbers in another tradition than the rest of it.
///
/// <para>
/// The Open New Ukrainian Translation numbers the Psalms as the Greek does, as its note on Psalm 9
/// says ("we use the numbering by the Greek translation that the Ukrainian reader is used to"): its
/// Psalm 22 is the Hebrew's 23, and it counts the titles as verses. Everything else it numbers as the
/// Hebrew and the English do. Placed by the Hebrew's rules throughout, each of its psalms from the
/// tenth on stood at the psalm before.
/// </para>
/// <para>
/// The Synodal's books beyond the canon come from Russian Wikisource in the Synodal's own numbering,
/// which the versification data has no column for. Its tests take them for the Latin wherever a
/// chapter happens to count as the Latin's does — the Synodal's Wisdom 5 has the Latin's
/// twenty-four verses and divides them as the King James does — so they stand at their own numbers,
/// as <see cref="Versification.Unknown"/> says. Baruch, the Letter of Jeremiah and Maccabees, which
/// were read verse by verse, are placed.
/// </para>
/// </summary>
internal static class BookTraditions
{
    private const int Psalms = 19;

    private const int FirstKings = 11;

    private const int Malachi = 39;

    private static readonly Dictionary<string, IReadOnlyDictionary<int, Versification>> Editions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [EbibleTextSource.BiblicaUkrainian] = new Dictionary<int, Versification>
            {
                [Psalms] = Versification.Septuagint,
            },
            [Sources.SynodalSlug] = new[] { 68, 69, 70, 71, 72, 75 }
                .ToDictionary(book => book, _ => Versification.Unknown),

            // The Elizabeth Bible numbers the same books the Synodal's way, and the data has no column for it
            // either. It numbers the Septuagint's way everywhere else except in 1 Kings and Malachi, where it
            // divides the chapters as the King James does: placed by the Greek's rules there, 1 Kings 5 stood
            // at the Greek's chapter 4 and Malachi 4:4 at 4:5, against the Synodal's words.
            [Sources.ElizabethSlug] = new Dictionary<int, Versification>
            {
                [FirstKings] = Versification.English,
                [Malachi] = Versification.English,
                [68] = Versification.Unknown,
                [70] = Versification.Unknown,
                [71] = Versification.Unknown,
                [72] = Versification.Unknown,
                [75] = Versification.Unknown,
            },
        };

    /// <summary>
    /// The books of this edition numbered in a tradition of their own, or in none the data describes;
    /// empty for most.
    /// </summary>
    public static IReadOnlyDictionary<int, Versification> For(string slug) =>
        Editions.GetValueOrDefault(slug) ?? new Dictionary<int, Versification>();
}
