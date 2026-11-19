using Essenthos.Core.Mapping;
using Essenthos.Core.Products;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The words and shares the Berean tables are read with (<see cref="BereanLinks"/>), as <c>sources/words.tsv</c> and
/// <c>sources/parameters.tsv</c> declare them under the source <c>berean</c>. A product made with them names both files
/// among its inputs.
/// </summary>
/// <param name="SuppliedConjunctions">The English conjunctions a row of a conjunction and bracketed words is read for.</param>
/// <param name="WawConjunctions">Those of them that render a ו; «for» renders a preposition or כִּי, never a ו.</param>
/// <param name="OpeningConjunctions">The English a ו is rendered by, or a word that may stand for one: a row opening with one has its ו rendered.</param>
/// <param name="SubjectParts">The parts of a word that can stand for a person or thing the translators named with a pronoun or a name.</param>
/// <param name="Prefixes">
/// The prefixed preposition each English preposition names, as the table's parsing writes it and as BHSA numbers the word it
/// writes for it.
/// </param>
/// <param name="OpeningPrepositions">The English a prefixed preposition is rendered by: a row opening with one has its prefix rendered.</param>
/// <param name="PlacedRows">The share of a verse's rows that must be placed on the witness's words for the two to be one verse.</param>
/// <param name="PlacedBoth">
/// The share of the rows, and of the witness's words alike, that is enough for that where <paramref name="PlacedRows"/> is met
/// and this is not on one side only.
/// </param>
internal sealed record BereanLists(
    IReadOnlySet<string> SuppliedConjunctions,
    IReadOnlySet<string> WawConjunctions,
    IReadOnlySet<string> OpeningConjunctions,
    IReadOnlySet<string> SubjectParts,
    IReadOnlyDictionary<string, BereanLinks.Prefix> Prefixes,
    IReadOnlySet<string> OpeningPrepositions,
    double PlacedRows,
    double PlacedBoth)
{
    /// <summary>The numbers BHSA writes for the prefixed prepositions the English names (ב, כ, ל and מִן), where they are words of their own.</summary>
    public IReadOnlySet<string> PrepositionNumbers { get; } = Prefixes.Values.Select(prefix => prefix.Strong).ToHashSet(StringComparer.Ordinal);

    /// <summary>Whether a part of a word, as the table's parsing heads it, can stand for a person or thing.</summary>
    public bool SubjectPart(string part) => SubjectParts.Contains(part) || part.Contains(" person", StringComparison.Ordinal);

    /// <summary>The store's lists and shares.</summary>
    public static BereanLists Of(ProductStore store)
    {
        var words = SourceWords.Of(store);
        var shares = SourceParameters.Of(store);
        var parts = words.Map(SourceWords.Berean, SourceWords.BereanPrefixPart, StringComparer.Ordinal);
        var prefixes = words.Map(SourceWords.Berean, SourceWords.BereanPrefix, StringComparer.OrdinalIgnoreCase).ToDictionary(
            pair => pair.Key,
            pair => new BereanLinks.Prefix(parts.TryGetValue(pair.Value, out var part)
                ? part
                : throw new InvalidDataException($"{SourceWords.Source}: berean's prefix \"{pair.Key}\" reads {pair.Value}, which no prefix-part row names."), pair.Value),
            StringComparer.OrdinalIgnoreCase);
        return new BereanLists(
            words.Set(SourceWords.Berean, SourceWords.BereanSuppliedConjunction, StringComparer.OrdinalIgnoreCase),
            words.Set(SourceWords.Berean, SourceWords.BereanWawConjunction, StringComparer.OrdinalIgnoreCase),
            words.Set(SourceWords.Berean, SourceWords.BereanOpeningConjunction, StringComparer.OrdinalIgnoreCase),
            words.Set(SourceWords.Berean, SourceWords.BereanSubjectPart, StringComparer.Ordinal),
            prefixes,
            words.Set(SourceWords.Berean, SourceWords.BereanOpeningPreposition, StringComparer.OrdinalIgnoreCase),
            shares.Value(SourceWords.Berean, SourceParameters.BereanPlacedRows),
            shares.Value(SourceWords.Berean, SourceParameters.BereanPlacedBoth));
    }
}
