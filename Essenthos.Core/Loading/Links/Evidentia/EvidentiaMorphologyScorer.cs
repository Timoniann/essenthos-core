namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Adds a small, positive-only grammatical agreement signal to an independently lexical edge.
/// Translation may legitimately change a part of speech, so disagreement never removes an edge.
///
/// Both sides are read through <see cref="EvidentiaMorphologyLabels"/>, because each witness
/// annotates in its own vocabulary and comparing the stored strings compares spellings.
/// </summary>
internal static class EvidentiaMorphologyScorer
{
    private const double CompatiblePartOfSpeechScore = 0.06;
    private const double CompatibleFeatureScore = 0.015;
    private const double MaximumFeatureScore = 0.04;
    private static readonly string[] ComparableFeatures = ["number", "gender", "person"];

    public static EvidentiaEvidence? Score(EvidentiaAnalysis source, EvidentiaAnalysis target)
    {
        var sourcePartOfSpeech = EvidentiaMorphologyLabels.PartOfSpeech(source.PartOfSpeech, source.Token.Language);
        var targetPartOfSpeech = EvidentiaMorphologyLabels.PartOfSpeech(target.PartOfSpeech, target.Token.Language);
        if (sourcePartOfSpeech is null || targetPartOfSpeech is null || sourcePartOfSpeech != targetPartOfSpeech)
        {
            return null;
        }

        var matchingFeatures = ComparableFeatures.Count(feature => SameFeature(source, target, feature));
        var score = CompatiblePartOfSpeechScore + Math.Min(MaximumFeatureScore, matchingFeatures * CompatibleFeatureScore);
        return new EvidentiaEvidence(
            EvidentiaEvidenceKind.Morphology,
            score,
            $"universal-pos:{sourcePartOfSpeech}; matching-features:{matchingFeatures}");
    }

    private static bool SameFeature(EvidentiaAnalysis source, EvidentiaAnalysis target, string feature) =>
        Value(source, feature) is { } left && Value(target, feature) is { } right && left == right;

    private static string? Value(EvidentiaAnalysis analysis, string feature) =>
        EvidentiaMorphologyLabels.Feature(
            analysis.Token.Morphology?.FirstOrDefault(pair => pair.Key.Equals(feature, StringComparison.OrdinalIgnoreCase)).Value,
            analysis.Token.Language);
}
