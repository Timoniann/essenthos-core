namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The Hebrew object marker אֵת, H853 — the one Strong number that names a word no translation
/// renders as a word.
///
/// <para>
/// **Bare, it marks the next word as the object and is rendered by nothing.** "God created the
/// heavens and the earth" has no word for either אֵת; the case they mark is the whole of what they
/// say, and a Slavic or German accusative already says it on the noun. With a pronominal suffix it
/// is a pronoun — אֹתוֹ is <em>him</em>, <em>его</em> — and a translation renders it like any other
/// word. BHSA tells the two apart by the suffix's person on the word, which is what
/// <see cref="Reachable"/> reads.
/// </para>
///
/// <para>
/// **The taggings number it anyway.** The King James convention, which the Synodal's and the Union
/// Version's numberings follow, hangs H853 on the verb whose object it marks — Genesis 1:1's
/// <em>сотворил</em> is <c>H1254 H853</c> — and puts it alone on whatever word stands where the
/// marker does, which is often a conjunction: the Synodal's <em>и</em> before <em>землю</em> carries
/// <c>H853</c> and nothing else. Matched as written, the verb reaches its verb and every אֵת in the
/// verse, and the conjunction reaches the markers instead of the וְ it renders.
/// </para>
///
/// <para>
/// So a Strong number reaches a bare marker never, and a tag naming other numbers besides it drops
/// it: the verb renders the verb. A tag naming the marker alone still reaches a suffixed one, which
/// is the pronoun the tag is then about.
/// </para>
/// </summary>
internal static class ObjectMarker
{
    public const string Number = "H853";

    /// <summary>The morphology feature BHSA writes on a word carrying a pronominal suffix.</summary>
    public const string SuffixFeature = "suffixPerson";

    /// <summary>Whether a witness word carrying this number is one a translated word can render.</summary>
    /// <param name="suffixed">Whether the word carries a pronominal suffix; read only for the marker.</param>
    public static bool Reachable(string? strong, bool suffixed) => strong != Number || suffixed;

    /// <summary>The numbers of a tag a match looks for: all of them, less the marker where it stands beside another.</summary>
    public static IReadOnlyList<string> Rendered(IReadOnlyList<string> numbers) =>
        numbers.Contains(Number) && numbers.Any(number => number != Number)
            ? [.. numbers.Where(number => number != Number)]
            : numbers;
}
