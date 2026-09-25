using Essenthos.Core.Utils;

namespace Essenthos.Core.Corpus;

/// <summary>
/// The form a word is searched by: its own letters, with everything that decorates them removed.
///
/// A reader types <c>bereshit</c> or <c>θεος</c> or <c>Бог</c>, and the corpus stores
/// <c>בְּ</c> with a vowel point, <c>Θεὸς</c> with an accent, and <c>Бог</c> capitalised at the
/// head of a verse. Nobody types the points. Nobody types the accents. So neither is searched.
///
/// **One implementation, used by both sides.** The stored form and the typed term go through this
/// same function — a fold written twice, once in C# for the load and once in SQL for the query,
/// is a fold that will one day disagree with itself and answer nothing for a word that is there.
/// It has happened here once already: <c>string.Normalize</c> is a silent no-op where globalisation
/// is invariant, so accent stripping passed its test and did nothing in the running loader.
/// </summary>
internal static class WordFolding
{
    /// <summary>Hebrew points, accents and cantillation: everything the Masoretes added.</summary>
    private const char PointsFrom = '֑';

    private const char PointsTo = 'ׇ';

    /// <summary>The tatweel, which stretches a word across a line and is no letter of it.</summary>
    private const char Tatweel = 'ـ';

    /// <summary>
    /// The alif of union, ٱ, which a fully vowelled Bible prints wherever the article joins what
    /// comes before it, and which nobody types: it is searched as the plain alif.
    /// </summary>
    private const char AlifOfUnion = 'ٱ';

    private const char Alif = 'ا';

    public static string Fold(string surface, string? language) => language switch
    {
        "hbo" or "arc" => Unpointed(surface),
        "grc" => GreekLetters.Bare(surface),
        "arb" => Unvowelled(surface),
        "gez" => Loading.Links.GeezStemmer.Fold(surface),
        _ => surface.ToLowerInvariant(),
    };

    /// <summary>
    /// The letters of an Arabic word without its vowels, its doubling mark and its case endings. The
    /// Van Dyck prints every one of them — فِي ٱلْبَدْءِ — and a reader types في البدء.
    /// </summary>
    private static string Unvowelled(string surface)
    {
        Span<char> kept = stackalloc char[surface.Length];
        var at = 0;
        foreach (var character in surface)
        {
            if (character == Tatweel ||
                char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            kept[at++] = character == AlifOfUnion ? Alif : character;
        }

        return new string(kept[..at]);
    }

    /// <summary>
    /// The consonants, which is what a Hebrew word is. The points are a reading tradition written
    /// a thousand years after the consonants, and a search for <em>ראשית</em> that misses
    /// <em>רֵאשִׁית</em> is a search nobody can use.
    /// </summary>
    private static string Unpointed(string surface)
    {
        Span<char> kept = stackalloc char[surface.Length];
        var at = 0;
        foreach (var character in surface)
        {
            if (character is < PointsFrom or > PointsTo)
            {
                kept[at++] = character;
            }
        }

        return new string(kept[..at]);
    }
}
