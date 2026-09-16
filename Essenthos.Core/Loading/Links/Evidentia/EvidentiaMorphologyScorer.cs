namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Adds a small, positive-only grammatical agreement signal to an independently lexical edge.
/// Translation may legitimately change a part of speech, so disagreement never removes an edge.
/// </summary>
internal static class EvidentiaMorphologyScorer
{
    private const double CompatiblePartOfSpeechScore = 0.06;
    private const double CompatibleFeatureScore = 0.015;
    private const double MaximumFeatureScore = 0.04;
    private static readonly string[] ComparableFeatures = ["number", "gender", "person"];

    public static EvidentiaEvidence? Score(EvidentiaAnalysis source, EvidentiaAnalysis target)
    {
        var sourcePartOfSpeech = UniversalPartOfSpeech(source.PartOfSpeech);
        var targetPartOfSpeech = UniversalPartOfSpeech(target.PartOfSpeech);
        if (sourcePartOfSpeech is null || targetPartOfSpeech is null || sourcePartOfSpeech != targetPartOfSpeech)
        {
            return null;
        }

        var matchingFeatures = ComparableFeatures.Count(feature => SameFeature(source.Token.Morphology, target.Token.Morphology, feature));
        var score = CompatiblePartOfSpeechScore + Math.Min(MaximumFeatureScore, matchingFeatures * CompatibleFeatureScore);
        return new EvidentiaEvidence(
            EvidentiaEvidenceKind.Morphology,
            score,
            $"universal-pos:{sourcePartOfSpeech}; matching-features:{matchingFeatures}");
    }

    private static bool SameFeature(IReadOnlyDictionary<string, string>? source, IReadOnlyDictionary<string, string>? target, string feature) =>
        Value(source, feature) is { } left && Value(target, feature) is { } right
        && left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static string? Value(IReadOnlyDictionary<string, string>? morphology, string feature) =>
        morphology?.FirstOrDefault(pair => pair.Key.Equals(feature, StringComparison.OrdinalIgnoreCase)).Value;

    private static string? UniversalPartOfSpeech(string? partOfSpeech) => partOfSpeech?.Trim().ToLowerInvariant() switch
    {
        "adj" or "adjective" => "adj",
        "adp" or "preposition" => "adp",
        "adv" or "adverb" => "adv",
        "aux" or "auxiliary" => "aux",
        "conj" or "cconj" or "sconj" or "conjunction" => "conj",
        "det" or "determiner" => "det",
        "noun" or "n" => "noun",
        "num" or "numeral" => "num",
        "part" or "particle" => "part",
        "pron" or "pronoun" => "pron",
        "propn" or "propernoun" => "propn",
        "verb" or "v" => "verb",
        _ => null,
    };
}
