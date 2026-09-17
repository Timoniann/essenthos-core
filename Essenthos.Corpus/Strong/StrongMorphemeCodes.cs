namespace Essenthos.Core.Strong;

/// <summary>
/// ETCBC assigns codes in the H9000 range to Hebrew prefix morphemes — the conjunction, the
/// article, the inseparable prepositions — which are separate words in BHSA but have no entry in
/// Strong's concordance. 121,077 of the 564,369 original words in the corpus carry one, so
/// treating them as missing entries misreports the dictionary's coverage by 21%.
/// The descriptions below were derived from the corpus itself: each code resolves to exactly one
/// part of speech and one lexeme family.
/// </summary>
public static class StrongMorphemeCodes
{
    private const int MorphemeRangeStart = 9000;
    private const int MorphemeRangeEnd = 9099;

    private static readonly Dictionary<string, string> Descriptions = new()
    {
        ["H9000"] = "Prefixed conjunction waw (\"and\", \"but\", \"then\")",
        ["H9003"] = "Prefixed preposition bet (\"in\", \"by\", \"with\")",
        ["H9004"] = "Prefixed preposition kaf (\"like\", \"as\")",
        ["H9005"] = "Prefixed preposition lamed (\"to\", \"for\")",
        ["H9008"] = "Prefixed interrogative he (marks a question)",
        ["H9009"] = "Prefixed definite article he (\"the\")",
    };

    /// <summary>
    /// True when the number is a grammatical morpheme code rather than a concordance entry that
    /// happens to be missing from the dataset.
    /// </summary>
    public static bool IsMorphemeCode(string strongNumber)
    {
        if (strongNumber.Length < 2 || strongNumber[0] != 'H')
        {
            return false;
        }

        return int.TryParse(strongNumber.AsSpan(1), out var number)
               && number is >= MorphemeRangeStart and <= MorphemeRangeEnd;
    }

    /// <summary>
    /// A human-readable description of a morpheme code, or a generic one for codes in the range
    /// that the corpus has not shown us yet.
    /// </summary>
    public static string? GetDescription(string strongNumber)
    {
        if (Descriptions.TryGetValue(strongNumber, out var description))
        {
            return description;
        }

        return IsMorphemeCode(strongNumber) ? "Hebrew prefix morpheme with no Strong's entry" : null;
    }
}
