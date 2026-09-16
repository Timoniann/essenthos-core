using System.Text.RegularExpressions;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Uses an original-text source's English gloss only to create reviewable lexical candidates.
/// A gloss is a compact dictionary hint, not an assertion that a particular translation word
/// renders that occurrence, so this source is deliberately excluded from automatic selection.
/// </summary>
internal sealed partial class TargetGlossEvidenceSource : IEvidentiaEvidenceSource
{
    private const double GlossScore = 0.20;
    private readonly IReadOnlyDictionary<long, HashSet<string>> formsByTargetId;

    private TargetGlossEvidenceSource(IReadOnlyDictionary<long, HashSet<string>> formsByTargetId) =>
        this.formsByTargetId = formsByTargetId;

    public static TargetGlossEvidenceSource? For(IEnumerable<EvidentiaToken> target)
    {
        var formsByTargetId = target
            .Where(token => !string.IsNullOrWhiteSpace(token.Gloss))
            .Select(token => new
            {
                token.Id,
                Forms = Word().Matches(token.Gloss!)
                    .Select(match => EnglishStemmer.Stem(match.Value))
                    .ToHashSet(StringComparer.Ordinal),
            })
            .Where(entry => entry.Forms.Count > 0)
            .ToDictionary(entry => entry.Id, entry => entry.Forms);

        return formsByTargetId.Count == 0 ? null : new TargetGlossEvidenceSource(formsByTargetId);
    }

    public IEnumerable<EvidentiaEvidence> Find(EvidentiaAnalysis source, EvidentiaAnalysis target)
    {
        if (!source.Token.Language.Equals("eng", StringComparison.OrdinalIgnoreCase)
            || source.IsFunctionWord
            || !formsByTargetId.TryGetValue(target.Token.Id, out var forms)
            || !forms.Contains(source.Normalised))
        {
            yield break;
        }

        yield return new EvidentiaEvidence(
            EvidentiaEvidenceKind.TargetGloss,
            GlossScore,
            "target-gloss:source");
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();
}
